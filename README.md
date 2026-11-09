# Hotel Management API

A multi-tenant REST API for running hotels, built with **ASP.NET Core (.NET 10)**, **EF Core**, **PostgreSQL** and **Docker**.

Many companies share one deployment. Each company signs up, then manages its own hotels, rooms, guests, bookings, payments and staff accounts. A company can never see or change another company's data.

**Features**
- **Self-service sign-up:** a company registers and gets its first Admin account.
- **JWT authentication with roles:** Admin, Manager and Receptionist.
- **Hotels and rooms:** management of both, plus date-based availability search.
- **Bookings:** check-in, check-out and cancellation, with double-booking prevention enforced in the database.
- **Payments and invoices:** payments, refunds and invoices with a running balance.
- **List endpoints:** paging, filtering and search on every list.
- **Swagger UI:** interactive API docs.
- **Seed data:** demo data loaded on first run.
- **Tests:** 51 unit tests and 30 integration tests.

---

## Contents

1. [Prerequisites](#prerequisites)
2. [Getting started](#getting-started)
3. [Using the API](#using-the-api)
4. [Solution structure](#solution-structure)
5. [Architecture](#architecture)
6. [How a request flows through the code](#how-a-request-flows-through-the-code)
7. [Domain model](#domain-model)
8. [Authentication, roles and multi-tenancy](#authentication-roles-and-multi-tenancy)
9. [Business rules](#business-rules)
10. [Database](#database)
11. [Configuration](#configuration)
12. [Testing](#testing)
13. [Adding a new feature](#adding-a-new-feature)
14. [Troubleshooting](#troubleshooting)
15. [Known limitations](#known-limitations)

---

## Prerequisites

| Tool | Version | Needed for |
|------|---------|------------|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | recent | Running the app and database; integration tests |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | Building, debugging and testing outside Docker |
| Visual Studio 2022+ / VS Code / Rider | optional | IDE debugging |

---

## Getting started

### Option A: everything in Docker (simplest)

```bash
docker compose up -d --build
```

This starts two containers:

| Container | What | Address from your PC |
|-----------|------|----------------------|
| `db`  | PostgreSQL 17 | `localhost:5433` |
| `api` | The API | http://localhost:8080 |

Open **http://localhost:8080/swagger**. On first start, the API creates the database schema and loads demo data.

### Option B: database in Docker, API from your IDE

```bash
docker compose up -d db                 # just PostgreSQL
dotnet run --project "Hotel management" # API on http://localhost:5180
```

In **Visual Studio**, open `Hotel management.slnx` and press F5. Swagger opens automatically. All three launch profiles work:

| Profile | Runs | Connects to the DB via |
|---------|------|------------------------|
| `http` / `https` | On your PC (http://localhost:5180) | `localhost:5433` |
| `Container (Dockerfile)` | In a Docker container | `host.docker.internal:5433` |

### Demo accounts

The seeded data contains the company *Demo Hotels Group*, two hotels with 18 rooms in total, four guests and three bookings. One of those bookings is currently checked in with a partial payment.

| Email | Role | Password |
|-------|------|----------|
| `admin@demo.local` | Admin | `Passw0rd!` |
| `manager@demo.local` | Manager | `Passw0rd!` |
| `reception@demo.local` | Receptionist | `Passw0rd!` |

### Stopping and resetting

```bash
docker compose down        # stop (data is kept in the "pgdata" volume)
docker compose down -v     # stop AND delete the database; demo data is re-seeded on next start
```

---

## Using the API

### Swagger UI

1. Open http://localhost:8080/swagger (or http://localhost:5180/swagger when running from the IDE).
2. Run `POST /api/auth/login` with `{ "email": "admin@demo.local", "password": "Passw0rd!" }`.
3. Copy `accessToken` from the response, click **Authorize** and paste it. Leave out the `Bearer ` prefix.
4. Every endpoint can now be called. Endpoints marked with a lock need a token.

Prefer files over a browser? `Hotel management/Hotel management.http` has ready-made requests for VS and VS Code.

### Endpoints

All endpoints need `Authorization: Bearer <token>` except `register`, `login` and `/health`.

| Area | Endpoint | Who |
|------|----------|-----|
| **Auth** | `POST /api/auth/register` (new company and its first Admin) | anyone |
| | `POST /api/auth/login` | anyone |
| | `GET /api/auth/me` | signed in |
| **Users** | `GET /api/users`, `GET /api/users/{id}`, `POST /api/users`, `PUT /api/users/{id}` | Admin |
| **Hotels** | `GET /api/hotels`, `GET /api/hotels/{id}` | all roles |
| | `POST /api/hotels`, `PUT /api/hotels/{id}` | Admin, Manager |
| | `DELETE /api/hotels/{id}` | Admin |
| **Rooms** | `GET /api/rooms`, `GET /api/rooms/{id}` | all roles |
| | `GET /api/rooms/available?hotelId=&checkIn=&checkOut=&guests=` | all roles |
| | `POST /api/rooms`, `PUT /api/rooms/{id}`, `DELETE /api/rooms/{id}` | Admin, Manager |
| **Guests** | `GET /api/guests`, `GET /api/guests/{id}`, `POST /api/guests`, `PUT /api/guests/{id}` | all roles |
| | `DELETE /api/guests/{id}` | Admin, Manager |
| **Bookings** | `GET /api/bookings`, `GET /api/bookings/{id}`, `POST /api/bookings` | all roles |
| | `POST /api/bookings/{id}/check-in`, `/check-out`, `/cancel` | all roles |
| | `GET /api/bookings/{id}/payments`, `POST /api/bookings/{id}/payments` | all roles |
| | `GET /api/bookings/{id}/invoice` | all roles |
| **Health** | `GET /health` | anyone |

### Conventions

- **Paging:** list endpoints take `?page=1&pageSize=20` (`pageSize` is 1–100) and return:
  ```json
  { "items": [...], "page": 1, "pageSize": 20, "totalCount": 42, "totalPages": 3 }
  ```
- **Filtering:**
  - **Users:** `search`, `role`
  - **Hotels:** `search` (name or city), `country`
  - **Rooms:** `hotelId`, `type`, `minCapacity`, `maxPrice`, `isActive`
  - **Guests:** `search` (name, email or phone)
  - **Bookings:** `hotelId`, `roomId`, `guestId`, `status`, plus `from`/`to`, which match stays overlapping that window
- **Dates:** `yyyy-MM-dd` (`DateOnly`). A stay is `[checkIn, checkOut)`: the guest leaves on the check-out day.
- **Enums:** sent and returned as strings, e.g. `"Double"`, `"CheckedIn"`, `"Card"`.
- **IDs:** GUIDs (UUID v7).
- **Errors:** [RFC 7807](https://www.rfc-editor.org/rfc/rfc7807) `application/problem+json`, e.g.
  ```json
  { "status": 409, "title": "Conflict", "detail": "The room is already booked for the selected dates." }
  ```

| Status | Meaning |
|--------|---------|
| 400 | Invalid input or a broken rule (past date, too many guests, overpayment…) |
| 401 | Missing or invalid token, or wrong login |
| 403 | Your role isn't allowed to do this |
| 404 | Doesn't exist, **or belongs to another company** |
| 409 | Conflicts with current state (double booking, wrong status, unpaid balance, record in use…) |

---

## Solution structure

```
Hotel management/                          ← repository root
├── Hotel management.slnx                  Solution file (open this in Visual Studio)
├── docker-compose.yml                     PostgreSQL + API containers
├── .env.example                           Template for production-like secrets
├── dotnet-tools.json                      Local tool manifest (dotnet-ef)
│
├── HotelManagement.Domain/                ── DOMAIN: business entities and rules, no dependencies
│   ├── Common/Common.cs                   Entity / TenantEntity base classes, DomainException, guards
│   ├── Companies/Company.cs
│   ├── Users/User.cs                      + UserRole
│   ├── Hotels/Hotel.cs
│   ├── Rooms/Room.cs                      + RoomType
│   ├── Guests/Guest.cs
│   └── Bookings/Booking.cs, Payment.cs    Aggregate: lifecycle, payments, balance
│
├── HotelManagement.Application/           ── APPLICATION: use cases and the interfaces they need
│   ├── Common/Abstractions.cs             ICurrentUser, IUnitOfWork, ITokenService, IPasswordHasher
│   ├── Common/Paging.cs                   PageQuery, PagedResult<T>
│   ├── Contracts/Contracts.cs             Request/response DTOs (with validation attributes)
│   ├── Auth/AuthService.cs                Register, login, "me"
│   ├── Users/UserService.cs               + IUserRepository, IUserQueries
│   ├── Hotels/HotelService.cs             + IHotelRepository, IHotelQueries
│   ├── Rooms/RoomService.cs               + IRoomRepository, IRoomQueries
│   ├── Guests/GuestService.cs             + IGuestRepository, IGuestQueries
│   ├── Bookings/                          BookingService, PaymentService, InvoiceService + abstractions
│   └── DependencyInjection.cs             AddApplication()
│
├── HotelManagement.Infrastructure/        ── INFRASTRUCTURE: database, security
│   ├── Persistence/
│   │   ├── AppDbContext.cs                EF Core context, automatic tenant filters and stamping
│   │   ├── EntityConfigurations.cs        Table mappings (one class per entity)
│   │   ├── Repositories.cs                Write side: load/save entities
│   │   ├── Queries.cs                     Read side: SQL projections straight to DTOs
│   │   ├── UnitOfWork.cs                  Transactions; translates DB errors to DomainException
│   │   ├── DatabaseInitializer.cs         Migrate on startup + demo data seeder
│   │   └── Migrations/                    EF Core migrations
│   ├── Security/Security.cs               JWT token creation, password hashing, claim names
│   └── DependencyInjection.cs             AddInfrastructure()
│
├── Hotel management/                      ── API: HTTP layer and composition root
│   ├── Program.cs                         Wires everything together; middleware pipeline
│   ├── Controllers/                       Thin controllers: HTTP in, service call, HTTP out
│   ├── Auth/WebAuth.cs                    Role names, ICurrentUser from the JWT
│   ├── Errors/DomainExceptionHandler.cs   DomainException → 400/401/404/409 problem response
│   ├── OpenApi/OpenApiSetup.cs            OpenAPI document + Swagger UI + Authorize button
│   ├── Dockerfile
│   ├── appsettings*.json
│   └── Hotel management.http              Sample requests
│
└── tests/
    ├── HotelManagement.UnitTests/         Domain + service tests (no database, ~0.1 s)
    └── HotelManagement.Tests/             End-to-end HTTP tests against a real PostgreSQL
```

---

## Architecture

The code follows **Clean Architecture**. Dependencies only point *inward*, towards the domain, and project references enforce this at compile time: Domain can't reference EF Core even by accident.

```
        ┌────────────────────────────┐
        │   API  (Hotel management)  │  controllers, auth, error mapping, Program.cs
        └──────┬──────────────┬──────┘
               │              │
               ▼              ▼
┌──────────────────────┐   ┌──────────────────────────┐
│    Infrastructure    │──►│       Application        │  use-case services + interfaces
│ EF Core, Npgsql, JWT │   └────────────┬─────────────┘
└──────────────────────┘                │
                                        ▼
                           ┌──────────────────────────┐
                           │          Domain          │  entities + business rules
                           └──────────────────────────┘
```

**Application defines the interfaces and Infrastructure implements them.** For example, `BookingService` needs `IBookingRepository` and `IUnitOfWork`, but it has no idea that EF Core or PostgreSQL exist. `Program.cs` is the only place that knows every layer. It plugs the implementations in with `AddApplication()` and `AddInfrastructure()`.

### SOLID in this codebase

| Principle | Where you see it |
|-----------|------------------|
| **S**ingle responsibility | Controllers only handle HTTP. Services orchestrate one use-case area each; bookings are split into `BookingService` (lifecycle), `PaymentService` and `InvoiceService`. Repositories handle writes, query classes handle reads. Each entity has its own mapping class. |
| **O**pen/closed | Tenant filters are applied automatically to every `ITenantEntity`, so a new tenant-owned entity is isolated without any change to `AppDbContext`. New entity mappings are picked up automatically via `ApplyConfigurationsFromAssembly`. |
| **L**iskov substitution | All tenant-owned entities derive from `TenantEntity`, and the company-assignment rules hold for every one of them. |
| **I**nterface segregation | Small, role-specific interfaces: `IRoomRepository` (write) is separate from `IRoomQueries` (read), and `ITokenService` from `IPasswordHasher`. |
| **D**ependency inversion | Services depend on abstractions only. Even database errors are translated into `DomainException` inside Infrastructure, so no layer above it references Npgsql. |

### Rich domain model

Business rules live **on the entities**, not in services. Entities have private setters and expose behaviour:

```csharp
var booking = Booking.Create(room, guest, checkIn, checkOut, guests, notes, now); // validates dates, capacity…
booking.MarkCheckedIn(now);                                          // only if Confirmed and on/after arrival
booking.RecordPayment(100m, PaymentMethod.Card, "Deposit", now, userId); // can't exceed balance
booking.MarkCheckedOut(now);                                         // only if fully paid
```

So an entity can never be put into an invalid state, and the rules can be unit-tested without a database.

### Reads vs writes (CQRS-lite)

- **Writes** load the full entity through a repository, call a domain method and save through `IUnitOfWork`.
- **Reads** use query classes (`IHotelQueries`, `IBookingQueries`…) that project straight to response DTOs in SQL. They don't track changes and only fetch the needed columns.

---

## How a request flows through the code

Example: **`POST /api/bookings/{id}/payments`**

```
HTTP request
  │
  ▼
JwtBearer middleware ── validates token, builds ClaimsPrincipal (sub, role, company_id)
  │
  ▼
BookingsController.AddPayment ── model validation on CreatePaymentRequest ([Range], [Required]…)
  │                               then calls the service (no logic here)
  ▼
PaymentService.RecordAsync  (Application)
  ├─ unitOfWork.BeginTransactionAsync()
  ├─ bookings.LockAsync(id)            ── SELECT … FOR UPDATE (stops two payments racing)
  ├─ bookings.GetByIdAsync(id)         ── query filtered to the caller's company automatically
  ├─ booking.RecordPayment(...)        ── DOMAIN enforces "not more than the balance"
  ├─ unitOfWork.SaveChangesAsync()     ── AppDbContext stamps CompanyId on new rows
  └─ tx.CommitAsync()
  │
  ▼
201 Created + PaymentResponse

If any step throws DomainException → DomainExceptionHandler → 400 / 401 / 404 / 409 problem+json
```

---

## Domain model

```
Company (tenant)
  ├── User        email (globally unique), role, password hash, active flag
  ├── Guest       name, email (unique per company), phone
  └── Hotel       name, address, contact
        └── Room  number (unique per hotel), type, capacity, price/night, floor, active
              └── Booking   guest, dates [checkIn, checkOut), party size,
                            captured price, status, timestamps
                    └── Payment   type (Payment/Refund), amount, method, reference
```

| Entity | Key behaviour |
|--------|---------------|
| `Booking` | `Create`, `MarkCheckedIn`, `MarkCheckedOut`, `Cancel`, `RecordPayment`, `RecordRefund`. Exposes `Nights`, `AmountDue`, `TotalPaid`, `TotalRefunded` and `Balance`. |
| `Room` | `EnsureCanHost(guests)` (active and within capacity); belongs to a hotel of the same company |
| `User` | `Update(...)`: an Admin can't demote or deactivate themselves |
| All tenant entities | `AssignCompany` sets the company once and never changes it |

**Booking statuses:**

```
            check-in                check-out (balance must be 0)
Confirmed ───────────► CheckedIn ───────────────────────────► CheckedOut
    │
    │ cancel
    ▼
Cancelled  (amount due becomes 0; anything paid can be refunded)
```

---

## Authentication, roles and multi-tenancy

**Sign-up and login**
- `POST /api/auth/register` creates a **company** plus its first **Admin** user and returns a token.
- `POST /api/auth/login` returns a JWT that is valid for 60 minutes by default. Passwords are hashed with ASP.NET Core Identity's PBKDF2 hasher.
- Admins add staff with `POST /api/users`. Login emails are unique across the whole system.

**Token contents**

| Claim | Holds |
|-------|-------|
| `sub` | User ID |
| `email` | Email address |
| `name` | Full name |
| `role` | `Admin`, `Manager` or `Receptionist` |
| `company_id` | The user's company |

**Authorization**
- Every endpoint requires a token by default (a fallback policy). Public endpoints opt out with `[AllowAnonymous]`.
- Role limits use `[Authorize(Roles = Roles.AdminOrManager)]` and similar.

**Tenant isolation.** This is the core security feature, and it's enforced in the data layer:
1. `HttpCurrentUser` reads `company_id` from the token.
2. `AppDbContext` adds a **global query filter**, `CompanyId == current company`, to every entity implementing `ITenantEntity`. This happens automatically.
3. So *every* query, including lookups by ID, only sees the caller's rows. Another company's ID returns **404**: its existence isn't even revealed.
4. On save, new rows are automatically stamped with the caller's company. Changing an existing row's company throws.
5. Entities also check this themselves: a booking's room and guest must belong to the same company.

---

## Business rules

| Area | Rule |
|------|------|
| **Double booking** | Two active bookings (`Confirmed`/`CheckedIn`) for the same room can't overlap. There are two layers: a row lock serializes bookings per room, and a **PostgreSQL exclusion constraint** (`btree_gist` + `daterange`) makes overlap impossible even under heavy concurrency. Back-to-back stays are allowed. |
| **Booking input** | Check-out must be after check-in. Check-in can't be in the past, a stay is at most 365 nights, the room must be active and the party must fit its capacity. |
| **Pricing** | The room rate is captured when booking, so later price changes don't affect existing bookings. Total = rate × nights. |
| **Check-in** | Only `Confirmed` bookings, from the arrival day until the day before check-out. |
| **Check-out** | Only `CheckedIn` bookings, and the balance must be fully paid. |
| **Cancel** | Only `Confirmed` bookings. The amount due becomes 0. |
| **Payments** | The amount must be > 0 and ≤ outstanding balance, and the booking must be `Confirmed`/`CheckedIn`. Concurrent payments are serialized per booking. |
| **Refunds** | `"type": "Refund"`, allowed up to the net amount paid. A cancelled booking's balance goes negative (credit) until refunded. |
| **Deletion** | A hotel with rooms, or a room or guest with bookings, can't be deleted (409). Deactivate rooms with `isActive: false` instead. |
| **Concurrency** | Bookings have an optimistic concurrency token (PostgreSQL `xmin`), so simultaneous status changes return 409 instead of overwriting each other. |

---

## Database

- **PostgreSQL 17** in Docker. Data is kept in the `pgdata` volume.
- **Migrations:** EF Core migrations live in `HotelManagement.Infrastructure/Persistence/Migrations` and are **applied automatically on startup** (`Database:MigrateOnStartup`).
- **Seeding:** demo data is added on startup only when `Database:SeedSampleData` is true *and* the database has no companies yet. Seeding is on in Development and off by default otherwise.
- **Constraints beyond EF:** the `InitialCreate` migration adds the anti-overlap exclusion constraint and a `CheckOut > CheckIn` check constraint with raw SQL.

**Changing the model**

```bash
dotnet tool restore     # installs dotnet-ef from dotnet-tools.json
dotnet ef migrations add <Name> --project HotelManagement.Infrastructure --startup-project "Hotel management" -o Persistence/Migrations
# the migration is applied next time the API starts
```

**Connecting with a DB client** (pgAdmin, DBeaver, DataGrip…): host `localhost`, port **5433**, database `hotel`, user `hotel`, password `hotel`.

---

## Configuration

Configuration comes from `appsettings.json`, then `appsettings.{Environment}.json`, then **environment variables**. Each source overrides the one before it. Environment variables use `__` for nesting (`Jwt__Key`).

| Setting | Default | Purpose |
|---------|---------|---------|
| `ConnectionStrings__Default` | `localhost:5433` (`db:5432` in Docker) | PostgreSQL connection string |
| `Jwt__Key` | dev key in Development only | HMAC signing key, ≥ 32 chars. **The app refuses to start without it.** |
| `Jwt__Issuer` / `Jwt__Audience` | `HotelManagement` / `HotelManagement.Api` | Token issuer/audience |
| `Jwt__ExpiryMinutes` | `60` | Token lifetime |
| `Database__MigrateOnStartup` | `true` | Apply migrations at startup |
| `Database__SeedSampleData` | `true` in Development | Seed demo data into an empty database |
| `ASPNETCORE_ENVIRONMENT` | `Development` in Docker Compose | `Development` enables Swagger and the dev JWT key |

**Production-like run:** copy `.env.example` to `.env` (Docker Compose reads it automatically) and set real values:

```env
POSTGRES_PASSWORD=a-strong-password
JWT_KEY=a-long-random-secret-of-at-least-32-characters
ASPNETCORE_ENVIRONMENT=Production
SEED_SAMPLE_DATA=false
```

In Production, Swagger is off and the API refuses to start without a `JWT_KEY`.

---

## Testing

```bash
dotnet test                              # everything
dotnet test tests/HotelManagement.UnitTests   # fast tests only, no Docker needed
```

| Project | What it tests | How | Speed |
|---------|---------------|-----|-------|
| `HotelManagement.UnitTests` (51 tests) | Domain rules (booking lifecycle, payments, refunds, validation, tenant assignment) and application services (ordering of lock → load → save → commit, conflict paths, auth) | Pure C#. Services get mocked interfaces (NSubstitute) and a fake clock (`FakeTimeProvider`). | ~0.1 s |
| `HotelManagement.Tests` (30 tests) | The whole API over HTTP: auth, roles (403s), **tenant isolation**, double booking, **concurrent booking/payment races**, payments → invoice → check-out, paging and filters | `WebApplicationFactory` boots the real app against a throwaway PostgreSQL started by **Testcontainers** | ~5 s (+ container start) |

Integration tests need Docker running. Each test registers its own new company, so tests don't interfere with each other, and that doubles as a check of the tenant isolation.

---

## Adding a new feature

Example: adding **room service orders** to a booking.

1. **Domain:** create an `Order` entity deriving from `TenantEntity`, with its rules as methods. It automatically gets tenant isolation.
2. **Application:** in `Contracts/`, add request/response DTOs with validation attributes. In a new `Orders/` folder, define `IOrderRepository`/`IOrderQueries` and an `IOrderService` + `OrderService`. Register the service in `Application/DependencyInjection.cs`.
3. **Infrastructure:** add an `IEntityTypeConfiguration<Order>`, a `DbSet` in `AppDbContext`, and the repository and query implementations. Register them in `Infrastructure/DependencyInjection.cs`. Then create a migration (see [Database](#database)).
4. **API:** add a thin `OrdersController` that calls `IOrderService`, with `[Authorize(Roles = …)]` where needed.
5. **Tests:** unit-test the entity rules and the service. Add an integration test for the endpoint, including a cross-company check.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Failed to connect to 127.0.0.1:5432` / `Connection refused` | The database isn't running, or you're using an old port | `docker compose up -d db`. The hotel DB listens on **5433**. |
| `password authentication failed for user "hotel"` | Another PostgreSQL is answering, e.g. one installed in **WSL** or natively on Windows on port 5432 | Make sure your connection string uses port **5433** (the default here). Check with `netstat -ano \| findstr :5432`. |
| Running the **Container (Dockerfile)** profile can't reach the DB | In a container, `localhost` is the container itself | That profile already uses `host.docker.internal:5433`. Keep the `db` container running. |
| `JwtOptions` validation error at startup | No `Jwt:Key` (≥ 32 chars) outside Development | Set `JWT_KEY` in `.env`, or `Jwt__Key` as an environment variable |
| Integration tests fail to start | Docker isn't running | Start Docker Desktop. Unit tests run without it. |
| Want a clean database | | `docker compose down -v && docker compose up -d --build` |

---

## Known limitations

- **Revocation:** a deactivated user's existing token stays valid until it expires (60 min by default). There are no refresh tokens or token revocation yet.
- **Time zones:** "today", used for check-in and for blocking past-dated bookings, is computed in **UTC**, not each hotel's local time zone.
- **Payments:** payments and refunds are records only. No payment provider is integrated.
- **Cancellation fees:** cancelling a confirmed booking always makes the amount due 0.
