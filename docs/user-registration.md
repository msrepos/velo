# Admin user registration and account roles

Open `/admin/users` and use **Add user** to enter a name, email, international
phone number, account role, and active status. Editing a user also supports changing
their role. The phone number is immutable in this interface. All user operations
require an authenticated Staff session with `users.manage`.

| Account role | Access |
| --- | --- |
| Customer | Own shopping and order workflow; no staff or fleet profile |
| Admin | All six management permissions when newly assigned |
| Driver | Driver profile and authenticated assigned-delivery operations |
| Crew | Crew profile plus `orders.manage` for the current preparation app |

Accounts start with an unverified phone. Normal OTP sign-in verifies ownership;
admin registration does not send an OTP, set a password, or mark the phone verified.
Inactive users cannot sign in. Unverified drivers/crew cannot start a van shift.
Driver and Crew roles are reflected in the existing fleet pages automatically.
Crew's current `orders.manage` permission is still cross-warehouse, not limited
by their dispatch assignment.

Role selection is stored in Identity claims using `velo.account.role`; operational
access is enforced through trusted staff permission claims and active fleet
membership. Base customer authentication remains available to all accounts. No
database migration is needed beyond the existing fleet tables.

When changing a role, Identity changes and Delivery profile updates share one SQL
transaction. The previous fleet profile is deactivated, not deleted, preserving
shipment/shift history. Assigned, OutForDelivery, or Failed shipments and active
van shifts block removal of the relevant operational membership or deactivation.
Stale edits return 409. Changes to role or active state revoke existing sessions;
the user signs in again to receive their updated access.

Operators cannot change their own role or deactivate themselves. New Admin roles
grant `catalog.manage`, `inventory.manage`, `orders.manage`, `delivery.manage`,
`payments.manage`, and `users.manage`. Selecting Crew replaces management access
with `orders.manage`; selecting Customer or Driver removes staff permissions.
Saving an existing Admin without changing the role preserves any custom permission
set, including accounts provisioned earlier through the terminal. For legacy
accounts without a selected role, lists infer a role from existing permissions and
active fleet profiles. Existing clients that omit `role` in PUT preserve access.

## Requests

`POST /api/v1/admin/users`:

```json
{
  "name": "New crew member",
  "email": "crew@example.com",
  "phoneNumber": "+201012345678",
  "role": "Crew",
  "isActive": true
}
```

Duplicate normalized phone numbers return 409. Unknown role names and invalid
profiles return 400. `PUT /api/v1/admin/users/{id}` uses `name`, `email`, `role`,
`isActive`, and the latest `version`. Omit `phoneNumber` from edits; changing sign-in
identity needs a separate verified ownership flow.

Rebuild and deploy the API and PWA to publish this feature. No sample users or
permissions are created by deployment.
