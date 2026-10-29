# Fleet registration and team dispatching

The admin workspace has four fleet pages, all requiring `delivery.manage`:

| Page | Purpose |
| --- | --- |
| `/admin/drivers` | List, register, rename, activate/deactivate drivers |
| `/admin/minivans` | List Mobile warehouses; register/edit with `inventory.manage` |
| `/admin/crew` | List, register, rename, activate/deactivate preparation crew |
| `/admin/dispatching` | Start/end team shifts; view active shifts and history |

Registration from the Drivers and Crew fleet pages links an existing active, phone-verified user to a
profile. It does not create a login or mark a phone verified. With `users.manage`,
the operator can search accounts by phone/name; otherwise enter the verified user
UUID supplied by the account operator. Existing accounts sign in by OTP. Crew
registration does not grant `orders.manage` or other staff permissions; provision
those separately for the existing crew application. Alternatively, use
[Users → Add user](user-registration.md) to create a new account with Driver or
Crew role. That flow creates the fleet profile automatically, grants preparation
access for Crew, and requires OTP phone verification before operational assignment.

A minivan is the existing Inventory Mobile warehouse, not a second stock record.
Its unique warehouse code identifies the van. Coordinates are its configured
inventory location, not live vehicle telemetry. Existing Mobile warehouses appear
automatically. Registering or editing a minivan also requires `inventory.manage`.

## Shift workflow

1. Register a minivan and two verified accounts as driver and crew.
2. Open Dispatching and choose an available active minivan, driver, and crew member.
3. Save to start the shift immediately. One person cannot fill both seats, including
   when the same account has both a driver and crew profile.
4. End the shift before reassigning the team or van. History remains available.

SQL filtered unique indexes prevent a van, driver, or crew profile from occupying
two active shifts. Transaction locks also prevent the same user being dispatched
concurrently across both roles. The server rechecks active/verified accounts and
active Mobile warehouse membership when starting. Driver/crew deactivation is
blocked while their shift is active. A driver with an open Assigned,
OutForDelivery, or Failed shipment cannot start a new shift or end an active one;
finish, cancel, or return that shipment first. Stale edits/end requests return 409.

Start requests use a client-generated `operationId`, retained for retries of the
same payload. A replay returns the original shift, including if it has since
ended. Reusing the ID with a different actor or team returns 409. Start/end retain
the acting administrator and timestamps. Van name/code are captured at start;
driver and crew names reflect their current profiles.

## API

Under `/api/v1/admin/delivery` (`delivery.manage`):

| Method / path | Body or result |
| --- | --- |
| GET `/drivers`, GET `/crew`, GET `/minivans` | Paged records; `page`, `pageSize` (1–100) |
| POST `/drivers`, POST `/crew` | `userId`, `name` |
| PUT `/drivers/{id}`, PUT `/crew/{id}` | `name`, `isActive`, current base64 `version` |
| GET `/dispatches` | Paged history; optional `activeOnly=true` |
| POST `/dispatches` | `operationId`, `warehouseId`, `driverId`, `crewMemberId` |
| POST `/dispatches/{id}/end` | Current base64 `version` |

Minivan creation/edit uses the existing `/api/v1/admin/inventory/warehouses`
POST/PUT routes with `type: "Mobile"` and `inventory.manage`.

Authenticated `GET /api/v1/driver/dispatch` and `GET /api/v1/crew/dispatch` expose
only the caller's current team. Active membership is checked in SQL. They return
204 for a registered member with no active shift, and 403 for nonmembers. These
are integration endpoints; existing crew screens still select a warehouse
manually and the driver PWA has not been built yet.

Fleet records are not stored in browser storage or service-worker API caches.
The frontend refreshes lists explicitly and reloads availability when opening a
dispatch form; the server remains authoritative for concurrency conflicts.

## Deployment

Build/deploy both the API and customer PWA image (which contains admin). Apply the
new Delivery migration `20261029193333_VanDispatchTeams` using the existing migration
job or the local command:

```powershell
dotnet run --project src/Velo.Api --launch-profile http -- --migrate true
```

No migration is applied automatically by the admin UI. Grant operators
`delivery.manage`, plus `inventory.manage` for van registration/edit and optionally
`users.manage` for account search. Permission provisioning replaces the user's
permission list; include all permissions they should retain.

This feature records team shifts. Automatic order-to-team assignment, movement
during preparation, crew queue authorization by assigned van, and the driver PWA
are subsequent workflow integrations. Existing order assignment and staff
permissions retain their current behavior. Warehouse/user deactivation through
their separate management APIs is not locked to shift lifecycle; operators should
end shifts before retiring those resources.
