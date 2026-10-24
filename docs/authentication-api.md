# Customer phone authentication

The customer authentication flow is implemented with ASP.NET Core Identity,
SQL Server and ASP.NET Core's protected opaque bearer tokens. These tokens are
not JWTs and this API is not an OAuth/OIDC authorization server.

The implementation is for VELO's first-party mobile client. Administrative and
driver authentication, changing/recovering a phone number and social sign-in
are separate flows. Customer OTP never grants administrative roles.

## Run locally

1. Configure `ConnectionStrings:Velo` in Velo.Api User Secrets.
2. Configure a stable, randomly generated OTP HMAC key in the same secret store:

```powershell
# First-time setup only. Retain the existing key rather than changing it per run.
$otpHashKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Authentication:OtpHashKey" $otpHashKey --project src/Velo.Api
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet tool restore
dotnet ef database update --context IdentityDbContext --project src/Modules/Identity/Velo.Modules.Identity --startup-project src/Velo.Api
dotnet run --project src/Velo.Api --launch-profile http
```

The Development settings explicitly select the non-delivering development SMS
adapter and enable `developmentCode` in the OTP response. Nothing is sent and
codes are never logged. Use this only for local testing. Changing the HMAC key
invalidates outstanding challenges and resets origin hashing; do not rotate it
casually during active use.

## Routes

| Method | Route | Authentication | Effect |
| --- | --- | --- | --- |
| POST | /api/v1/auth/register | Anonymous | Submit phone, email and name; receive an OTP challenge |
| POST | /api/v1/auth/otp/request | Anonymous | Request a challenge for a phone |
| POST | /api/v1/auth/otp/verify | Anonymous | Consume a code and sign in/create a customer |
| POST | /api/v1/auth/refresh | Refresh token in body | Rotate the refresh token and issue access |
| GET | /api/v1/auth/me | Bearer access token | Current customer ID, phone, name, email and customer role |
| GET | /api/v1/auth/sessions | Bearer access token | Active sessions belonging to this customer |
| POST | /api/v1/auth/logout | Bearer access token | Revoke the current session immediately |
| POST | /api/v1/auth/logout-all | Bearer access token | Revoke all sessions and rotate security stamp |
| DELETE | /api/v1/auth/sessions/{id} | Bearer access token | Revoke an owned session; other IDs are a no-op |

### Register a customer

Send `POST /api/v1/auth/register` with all three fields:

```json
{
  "phoneNumber": "+201001234567",
  "email": "customer@example.com",
  "name": "Customer Name"
}
```

Name and email are required, trimmed and validated. Name is limited to 160
characters and email to 256. The details are bound to the OTP challenge and
copied to the new user only after verification. Re-registering an existing phone
does not overwrite its name/email. Email is not verified by phone OTP and is not
used to identify or merge accounts; duplicate emails are allowed.

`/me` reads name and email from the database. Existing users may have null names
or emails; the migration preserves their accounts. Invalid registration fields
return `invalid_name` or `invalid_email` (400) before an OTP challenge is created.

### Request an OTP for login

```json
{ "phoneNumber": "+201001234567" }
```

Use an international number. Spaces, parentheses, hyphens and a leading `00`
are normalized; local numbers without a country code are rejected. Validation
checks the shape, not whether the phone actually exists or belongs to a supported
service region. A successful response does not disclose whether an account exists.

Response:

```json
{
  "challengeId": "<uuid>",
  "expiresIn": 300,
  "resendAfter": 60,
  "developmentCode": "<six-digit code, Development only>"
}
```

### Verify

```json
{ "challengeId": "<uuid>", "code": "<six-digit code>" }
```

The server creates a user only after successful verification. It assigns the
Customer role. A registration challenge also supplies the name/email; the
existing phone-only OTP route remains backward compatible. No password is required. Response:

```json
{
  "tokenType": "Bearer",
  "accessToken": "<opaque token>",
  "expiresIn": 900,
  "refreshToken": "<opaque token>",
  "sessionId": "<uuid>"
}
```

Use `Authorization: Bearer <accessToken>` for protected routes. Keep tokens in
platform secure storage. Auth success responses have `Cache-Control: no-store`.
Do not write token bodies to analytics, crash reports or request logs.

### Refresh

```json
{ "refreshToken": "<current refresh token>" }
```

Atomically replace both locally stored tokens with the successful response.
Serialize refresh operations in the mobile client. Reusing any previous refresh
token revokes that entire session, including the access token issued by the
successful rotation. Concurrent refreshes with the same token are therefore
treated as replay. Sessions have an absolute lifetime and do not slide forever.

## Security behavior and defaults

- OTP: six cryptographically random digits, five-minute lifetime, five attempts.
- Resend: minimum 60 seconds; a resend invalidates all older challenges for that phone.
- Phone limit: five requests/hour. Client-origin limits: 30 requests/hour and
  120 verifications/hour. These counters/attempts are stored in SQL Server.
- A per-instance IP limiter also caps the auth group at 120 requests/minute.
  It is supplementary; deploy edge/distributed limits for refresh traffic at scale.
- The client origin is the actual remote IP. Forwarded headers are not trusted
  automatically. Configure only known proxies before deploying behind a proxy.
- Codes are HMACed with a server secret and challenge/phone binding. Refresh tokens
  are hashed in session records. Raw tokens/codes are not stored in these tables.
- SQL transaction application locks serialize OTP consumption and refresh rotation
  across instances. Verification and user/session creation commit atomically.
- Access lifetime is 15 minutes; session/refresh lifetime is 30 days. Every
  authenticated request checks the session, user state and security stamp in SQL.
  Logout, disabling an account and a stamp change therefore revoke access immediately.
- Existing staff roles are not copied into customer OTP access tokens.
- Delivery is outside the SQL transaction. A failed delivery invalidates its challenge.
  If the process stops after challenge creation, the unsent challenge expires and
  is replaced by a later resend; the provider delivery is not a durable outbox yet.

Errors use Problem Details. Common codes: `invalid_phone_number` (400),
`invalid_request` (400), `invalid_or_expired_code` (400),
`otp_rate_limited` (429, with Retry-After), `invalid_session` (401),
and `otp_delivery_unavailable` (503). Protected routes return 401 for missing,
expired or revoked access tokens. Account lock/disable status is not exposed by OTP.

## Deployment prerequisites

A real SMS adapter implementing `IOtpSender` must be registered before production
use. The integration contract is in Identity/Application; a Notifications adapter
can implement it when a provider is selected. No real provider is configured now.

Development SMS and code exposure are rejected outside Development. Configure a
production SMS provider name and adapter, disable code exposure, provide the HMAC
key through a secret store, and use HTTPS. Never enable Development on a public host.

Data Protection keys persist in `identity.DataProtectionKeys` with application
name `Velo.Identity`, so restarts/instances share the same token protectors.
Outside Development, startup requires `DataProtection:CertificatePath` and,
if applicable, `DataProtection:CertificatePassword` for a PFX that encrypts the key
ring. Protect the certificate and share it securely between instances. Local
Development keys are not encrypted at rest; use a fresh protected ring when moving
to production rather than copying the development key table.

Schedule retention/cleanup of expired OTP challenges, old throttling windows and
expired/revoked sessions according to the audit policy. Retain OTP records for at
least the active one-hour quota window. Do not delete Data Protection keys while
tokens protected by them may still be valid.

## Tests

`Velo.Auth.IntegrationTests` runs HTTP flows through the real API with real SQL
Server migrations. It creates a unique `velo_auth_tests_<guid>` database and
drops only that database after the run. The development `velo` database is not
used for test writes.

```powershell
# Uses Velo.Api User Secrets unless VELO_TEST_SQL_CONNECTION is set.
dotnet test tests/Velo.Auth.IntegrationTests
```

The test login needs permission to create/drop its temporary database. Tests
cover expiry, attempt budgets, resends, quotas, concurrent verification/refresh,
replay revocation, logout, ownership, disabled users, role isolation and SMS failure.
