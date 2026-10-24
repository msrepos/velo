# Authentication decision

Status: accepted. Date: 2026-10-23.

## Selected approach

Use ASP.NET Core Identity with EF Core and SQL Server for user management.
Customer sign-in uses a phone number and an SMS one-time code, with a custom
mobile experience. Customers do not need an email address or password for this flow.
Keycloak and Microsoft Entra External ID are not part of this implementation.

Customer OTP endpoints, Identity stores, SQL Server migrations, sessions,
protected opaque bearer tokens and refresh rotation are implemented. The user
selected local development delivery for now; a real SMS provider will be integrated
later. See [the authentication API](../authentication-api.md) for routes,
limits, tests and deployment prerequisites, and [database migrations](database-migrations.md)
for database setup.

## Ownership

- Identity owns authentication users, verified phone numbers, OTP challenges,
  sessions and access permissions in its SQL Server schema.
- Customers owns customer profiles and addresses, linked through a stable user ID.
- Notifications owns provider-specific SMS delivery; Identity decides when a code
  is valid. Introduce an explicit contract for sending messages when implemented.
- The API host composes authentication middleware and authorization policies.
- Resource ownership and business rules remain in the relevant module: a customer
  can read their own orders, and a driver can operate only on assigned deliveries.

## Customer flow

1. Normalize and validate the phone number in international E.164 format.
2. Create a short-lived challenge and request delivery through an SMS provider.
3. Verify the code against the challenge, consuming it atomically on success.
4. Create a new customer identity or load the existing identity only after proof
   of phone ownership. Enforce uniqueness of the normalized phone in SQL Server.
5. Establish an authenticated session using the selected token mechanism.

Use one flow for new and returning customers. Never trust a user ID, role or
verified-phone flag supplied by the client. Self-service sign-in cannot grant
staff or driver privileges. Staff provisioning and MFA require a separate flow.

## OTP and session requirements

- Generate codes using a cryptographically secure random source. Store a protected
  verifier, not plaintext; do not log OTP values or authentication tokens.
- Enforce expiry, single use, bounded attempts, resend cooldown and request limits
  by phone and client origin. Persist challenge/attempt state so limits work across
  instances. Resending must have an explicit invalidation policy.
- Return non-enumerating responses. Account recovery and phone-number changes
  require explicit verification rules; a phone number is not an immutable user ID.
- Token storage on mobile uses platform secure storage. Support expiry, renewal,
  logout and session revocation; make renewal rotation/reuse behavior explicit.

ASP.NET Core Identity is user-management infrastructure, not an OAuth/OIDC server.
Its built-in bearer tokens are not JWTs. The selected mechanism is ASP.NET Core's
separate access/refresh ticket protectors with SQL-backed sessions and refresh
rotation. This API does not implement an OAuth/OIDC authorization server.

## Implementation sequence

1. Completed: Identity EF Core and SQL Server packages, user mapping and migrations.
2. Completed: OTP challenge storage, verification and abuse limits with tests for
   expiry, retries, concurrency and single-use consumption.
3. Completed: SMS delivery contract and local adapter; integrate a real provider
   after selecting one. A test adapter must not be enabled in production.
4. Completed: customer sessions, authenticated endpoints and session ownership checks.

Open decisions: real SMS provider, staff/driver authentication and phone recovery policy.
No external account setup is required for the local development implementation.
