# Catalog API

Catalog owns categories, products and sellable product variants (SKUs). Inventory
will separately own warehouse quantities, stock movements and reservations.
A catalog product being visible does not yet mean stock is available.

## Data

- `catalog.Categories`: display name, canonical unique name, active flag and timestamps.
- `catalog.Products`: category, name, description, one main HTTPS image URL and active flag.
- `catalog.ProductVariants`: product, unique SKU, name, optional unique barcode,
  positive decimal(18,2) price and active flag. Currency is EGP for this first release.
- All three tables have SQL Server rowversion concurrency tokens.
- Migration history: `catalog.__EFMigrationsHistory`. No cross-module database
  foreign keys or direct access to Identity tables is used by Catalog.

There is no hard-delete API. Use a PUT with `isActive: false` to deactivate an
entity while preserving its IDs. Renaming SKUs, promotions, multiple images,
taxes, price history and regional prices need explicit business rules before
they are introduced; current SKU edits are allowed only through versioned updates.

## Public routes

| Method | Route | Behavior |
| --- | --- | --- |
| GET | /api/v1/catalog/categories | Active categories, paginated |
| GET | /api/v1/catalog/categories/{id} | Active category or 404 |
| GET | /api/v1/catalog/products | Visible products with active variants, paginated |
| GET | /api/v1/catalog/products/{id} | Visible product with active variants or 404 |

A product is visible only if it, its category and at least one variant are active.
Inactive variants are omitted. Active empty categories can still be listed.

List parameters: `page=1`, `pageSize=20` (maximum 100). Products additionally
accept `categoryId=<uuid>` and `search=<name fragment>` (maximum 100 characters).
Lists return `items`, `page`, `pageSize` and `totalCount`, ordered by name then ID.

## Management routes

All routes below require a live authenticated session AND the permission claim
`permission=catalog.manage`. Anonymous requests receive 401, customer OTP tokens
receive 403. Customer registration, login and refresh never grant this permission.

Prefix: `/api/v1/admin/catalog`.

| Method | Route |
| --- | --- |
| GET / POST | /categories |
| GET / PUT | /categories/{id} |
| GET / POST | /products |
| GET / PUT | /products/{id} |
| POST | /products/{productId}/variants |
| PUT | /products/{productId}/variants/{id} |

Management reads include inactive records and variants. A product detail response
contains its variants; there is no separate variant GET route.

Category create body:

```json
{ "name": "Dairy", "isActive": true }
```

Product create body:

```json
{
  "categoryId": "<category-id>",
  "name": "Whole milk",
  "description": "Fresh whole milk",
  "imageUrl": "https://example.com/milk.jpg",
  "isActive": true
}
```

Variant create body:

```json
{
  "sku": "MILK-1L",
  "name": "1 litre",
  "price": 45.50,
  "barcode": "6221234567890",
  "isActive": true
}
```

SKU is trimmed and uppercased, 1–64 ASCII letters/digits/dots/underscores/hyphens,
starting with a letter or digit. Barcode is optional and must not contain whitespace.
Names are trimmed and limited to 160 characters; descriptions to 2000 characters.

PUT replaces the editable fields and requires the corresponding response's
base64 `version` field. Always replace the saved version after a successful PUT:

```json
{
  "sku": "MILK-1L",
  "name": "1 litre",
  "price": 50.00,
  "isActive": true,
  "version": "<variant version from the latest product detail or write response>"
}
```

Product and variant versions are independent. A variant price update does not
change its parent product version. A stale version returns 409
`catalog_version_conflict`; reload before applying a new edit. Duplicate category
names, SKUs or non-null barcodes return 409 `duplicate_catalog_value`.
Invalid fields/references return 400; missing or hidden records return 404.

## Local management token

Production staff authentication/provisioning is not implemented by the customer
OTP flow. For local development only, an operator can mint an access-only session
for an existing active, phone-verified user:

1. Complete customer login and copy the ID returned by `/api/v1/auth/me`.
2. From the repository root, run:

```powershell
dotnet run --project src/Velo.Api --no-build --launch-profile http -- --issue-development-catalog-token <user-guid>
```

This command exits without starting another server and prints a JSON result with
`accessToken`, `expiresIn` and `sessionId`. Paste its accessToken into the Catalog
Postman collection's `staffAccessToken` variable.

The session expires after the configured access-token lifetime (15 minutes by
default), has no exposed refresh token and can be revoked via the normal session
management endpoints. Customer tokens remain unprivileged. This local terminal
command is guarded to Development and has no HTTP equivalent; it is not a
production staff login mechanism. Keep printed tokens local.

## Migrations and tests

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --context CatalogDbContext --project src/Modules/Catalog/Velo.Modules.Catalog --startup-project src/Velo.Api
dotnet ef migrations script --idempotent --context CatalogDbContext --project src/Modules/Catalog/Velo.Modules.Catalog --startup-project src/Velo.Api --output infrastructure/database/catalog.sql
dotnet test tests/Velo.Catalog.IntegrationTests
```

Visual Studio Package Manager Console:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
Update-Database -Context CatalogDbContext -Project Velo.Modules.Catalog -StartupProject Velo.Api
```

Tests replace and verify both DbContext connections before applying migrations to
a dedicated `velo_catalog_tests_<guid>` database. They cover access control,
publication rules, SKU/barcode uniqueness, validation, paging and concurrent edits.
No demo catalog rows are seeded into the development database.
