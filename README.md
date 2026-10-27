<p align="center">
  <img src="https://emarelmadineh.com/velo_arrow_red.png" alt="VELO Logo" width="250">
</p>

<p align="center">
  <strong>Hyperlocal Commerce & Ultra-Fast Delivery Platform</strong>
</p>

<p align="center">
  <em>Everything you need, delivered in minutes.</em>
</p>

<p align="center">
  <a href="#about">About</a> •
  <a href="#vision">Vision</a> •
  <a href="#architecture">Architecture</a> •
  <a href="#technology-stack">Technology Stack</a> •
  <a href="#development">Development</a> •
  <a href="#project-structure">Project Structure</a>
</p>

---

## About

**VELO** is a hyperlocal commerce and ultra-fast delivery platform designed to serve residential communities with a simple goal:

> **Deliver everyday essentials in under 10 minutes.**

VELO starts in **Madinaty, Egypt**, with a technology and operational model designed to scale to other residential communities and seasonal locations.

Unlike traditional delivery platforms, VELO focuses on bringing inventory **closer to customers** through a network of **Mobile Warehouses**.


## Vision

VELO is not just a delivery application.

It is a:

> **Hyperlocal Commerce Operating System**

The platform combines:

* Hyperlocal inventory
* Mobile warehouses
* Real-time fleet tracking
* Intelligent order assignment
* Event-driven microservices
* Demand forecasting
* Delivery optimization

The objective is to make fast delivery possible by placing the right inventory in the right location **before the customer places the order**.


## Technology Stack
Backend API foundation for a grocery delivery mobile application.

Built with C# / ASP.NET Core on .NET 10 as a modular monolith. SQL Server
is the selected database, with separate EF Core contexts for Identity, Catalog,
Inventory, Customers, Orders, Delivery, Payments and Notifications.

Authentication uses ASP.NET Core Identity with SQL Server. Customer sign-in
uses phone OTP with a local development adapter; a real SMS provider will be integrated later.

## Start

Open `Velo.slnx` in Visual Studio with .NET 10 support. Set `Velo.Api` as
the startup project and select the `http` launch profile.

```powershell
dotnet restore Velo.slnx
dotnet tool restore
dotnet build Velo.slnx --no-restore
dotnet run --project src/Velo.Api --launch-profile http
```

Check `http://localhost:5080/health/live`; a successful response is `Healthy`.

To run the API and an isolated SQL Server together in Docker Desktop (Linux
containers), run from the repository root:

```powershell
./tools/docker/Start-Local.ps1
```

This creates local secrets, builds the image, applies migrations and checks
`http://localhost:5080/health/ready`. Use `-Port 5081` if 5080 is busy.
See [Docker deployment](docs/deployment.md) for logs, shutdown and existing SQL options.

## Current scope

The solution contains one API host, eight module class libraries, Catalog/Customers/
Inventory contracts libraries and integration-test projects. Customer auth,
Catalog categories/products/variants and Inventory warehouses, stock movements,
transfers and expiring reservations are implemented. Management requires module permissions.
Saved customer addresses and the Orders cart are implemented with current Catalog
prices and protection against concurrent edits. COD checkout supports a fee/total
preview, automatic fulfillment selection, atomic stock reservations, confirmation,
cancellation and expiry. Fulfillment, courier assignment/tracking/receipt codes,
COD collection/cash handover and an in-app notification inbox are implemented.
Real SMS/push/card providers and production rollout require external setup.
Configure `ConnectionStrings:Velo`
and `Authentication:OtpHashKey` through user secrets before starting the API;
migrations are applied explicitly. Local OTP codes are exposed only in Development.

# Core Services

VELO is expected to evolve into multiple independent services.

| Service          | Responsibility                               |
| ---------------- | -------------------------------------------- |
| **Auth**         | Authentication and authorization             |
| **Customer**     | Customer profiles and addresses              |
| **Catalog**      | Products, categories and product information |
| **Inventory**    | Stock management and reservation             |
| **Order**        | Order lifecycle and orchestration            |
| **Payment**      | Payment processing                           |
| **Fleet**        | Mobile warehouses and fleet management       |
| **Driver**       | Driver management and operations             |
| **Location**     | Real-time driver and vehicle location        |
| **Assignment**   | Intelligent order-to-driver assignment       |
| **Notification** | Push, SMS and other notifications            |
| **Analytics**    | Operational and business analytics           |

Services should remain independently deployable and own their business responsibilities.

---

# Event-Driven Architecture

Services communicate asynchronously through domain events where appropriate.

Example events:

```text
OrderCreated
StockReserved
StockReleased
OrderConfirmed
MobileWarehouseAssigned
DriverAssigned
OrderPicked
OrderOutForDelivery
OrderDelivered
InventoryLow
MobileWarehouseRestockRequired
DriverLocationUpdated
```

Example:

```text
Customer
   │
   ▼
Order Service
   │
   │ OrderCreated
   ▼
Event Broker
   │
   ├──────────────► Inventory Service
   │
   ├──────────────► Assignment Service
   │
   └──────────────► Notification Service
```

This approach reduces direct coupling between services and allows VELO to scale individual components independently.


- [Project structure](docs/architecture/project-structure.md)
- [Authentication decision](docs/architecture/authentication.md)
- [Authentication API and local testing](docs/authentication-api.md)
- [Catalog API, management tokens and migrations](docs/catalog-api.md)
- [Inventory API, reservations and Postman cycle](docs/inventory-api.md)
- [Customer addresses and cart API](docs/customer-cart-api.md)
- [Checkout, order lifecycle and Postman cycle](docs/orders-api.md)
- [Full delivery/COD cycle and complete Postman collection](docs/fulfillment-api.md)
- [Docker, Kubernetes and staff provisioning](docs/deployment.md)
- [Database migrations and Visual Studio commands](docs/architecture/database-migrations.md)
- [Development and SQL Server setup](docs/development.md)
- [Test plan](tests/README.md)

