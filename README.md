# Hotel Management API

A multi-tenant REST API for running hotels, built with **ASP.NET Core (.NET 10)**, **EF Core**, **PostgreSQL** and **Docker**.

Many companies share one deployment. Each company signs up, then manages its own hotels, rooms, guests, bookings, payments and staff accounts. A company can never see or change another company's data.

**Features**
- **Self-service sign-up:** a company registers and gets its first Admin account.
- **JWT authentication with roles:** Admin, Manager and Receptionist. Access tokens last 15 minutes, with rotating refresh tokens and instant revocation.
- **Hotels and rooms:** management of both, plus date-based availability search.
- **Bookings:** check-in, check-out and cancellation, with double-booking prevention enforced in the database.
- **Online booking for guests:** guests book without an account through a public API, get a booking reference plus secret code by email, and can cancel themselves up to 24 hours before arrival.
- **Payments and invoices:** payments, refunds and invoices with a running balance.
- **List endpoints:** paging, filtering and search on every list.
- **Swagger UI:** interactive API docs.
- **Seed data:** demo data loaded on first run.
- **Security:** salted PBKDF2 password hashing, brute-force lockout, encryption of guest personal data at rest, GDPR export and erasure, HTTPS via Caddy, and startup checks that refuse unsafe production settings. See [Security](#security).
- **Tests:** 129 unit tests and 65 integration tests.

---

## Contents

1. [Prerequisites](#prerequisites)
2. [Getting started](#getting-started)
3. [Using the API](#using-the-api)
4. [Solution structure](#solution-structure)
5. [Architecture](#architecture)
6. [How a request flows through the code](#how-a-request-flows-through-the-code)
   - [Online booking for guests](#online-booking-for-guests)
7. [Domain model](#domain-model)
8. [Authentication, roles and multi-tenancy](#authentication-roles-and-multi-tenancy)
9. [Security](#security)
10. [Business rules](#business-rules)
11. [Database](#database)
12. [Configuration](#configuration)
13. [Testing](#testing)
14. [Adding a new feature](#adding-a-new-feature)
15. [Troubleshooting](#troubleshooting)
16. [Known limitations](#known-limitations)

---

## Prerequisites

| Tool | Version | Needed for |
|------|---------|------------|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | recent | Running the app and database; integration tests |
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 | Building, debugging and testing outside Docker |
| Visual Studio 2022+ / VS Code / Rider | optional | IDE debugging |
| `openssl` | any | Generating production secrets (included with Git for Windows, macOS and Linux) |

---

## Getting started

### Option A: everything in Docker (simplest)

```bash
docker compose up -d --build
```

This starts three containers:

| Container | What | Address from your PC |
|-----------|------|----------------------|
| `db`  | PostgreSQL 17 | `localhost:5433` |
| `mailpit` | Development mail catcher: every email the API sends lands here | http://localhost:8025 (SMTP on `localhost:1025`) |
| `api` | The API | http://localhost:8080 |

Open **http://localhost:8080/swagger**. On first start, the API creates the database schema and loads demo data.

### Option B: database in Docker, API from your IDE

```bash
docker compose up -d db mailpit         # PostgreSQL + mail catcher
dotnet run --project "Hotel management" # API on http://localhost:5180
```

In **Visual Studio**, open `Hotel management.slnx` and press F5. Swagger opens automatically. All three launch profiles work:

| Profile | Runs | Connects to the DB / SMTP via |
|---------|------|-------------------------------|
| `http` / `https` | On your PC (http://localhost:5180) | `localhost:5433` / `localhost:1025` |
| `Container (Dockerfile)` | In a Docker container | `host.docker.internal:5433` / `host.docker.internal:1025` |

### Demo accounts

The seeded data contains the company *Demo Hotels Group* (public slug **`demo-hotels-group`**), two hotels with 18 rooms in total, four guests and three bookings. One of those bookings is currently checked in with a partial payment.

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

### Deploying to production

`docker-compose.prod.yml` turns the dev stack into a production one:
- **TLS:** Caddy terminates TLS with automatic Let's Encrypt certificates, and HTTP is redirected to HTTPS.
- **Ports:** the API and database aren't reachable from outside.
- **Secrets:** there are no development defaults, and every secret must be set.
- **Mailpit:** not started.

```bash
cp .env.example .env        # then fill in DOMAIN, POSTGRES_PASSWORD, JWT_KEY, ENCRYPTION_KEY, BLIND_INDEX_KEY, SMTP_*
# generate secrets:  openssl rand -base64 48   (JWT_KEY)   ·   openssl rand -base64 32   (each encryption key)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

Point your domain's DNS at the server, with ports 80 and 443 open. If a secret is missing, Compose stops with an error. If a secret is one of the public development values, the API refuses to start. **Back up the encryption keys:** encrypted guest data can't be read without them.

To try it locally, set `DOMAIN=localhost`. Caddy then uses a self-signed certificate, so use `curl -k https://localhost/health`.

---

## Using the API

### Swagger UI

1. Open http://localhost:8080/swagger (or http://localhost:5180/swagger when running from the IDE).
2. Run `POST /api/auth/login` with `{ "email": "admin@demo.local", "password": "Passw0rd!" }`.
3. Copy `accessToken` from the response, click **Authorize** and paste it. Leave out the `Bearer ` prefix. The token lasts 15 minutes; get a new one with `POST /api/auth/refresh`, or by logging in again.
4. Every endpoint can now be called. Endpoints marked with a lock need a token.

Prefer files over a browser? `Hotel management/Hotel management.http` has ready-made requests for VS and VS Code.

### Endpoints

There are two groups of endpoints:
- **Staff API** (`/api/...`): needs `Authorization: Bearer <token>`, except `register`, `login`, `refresh` and `logout`. Those four are rate-limited per IP.
- **Public API** (`/api/public/{companySlug}/...`): no login. It's for guests and booking websites, and is rate-limited per IP.

**Staff API**

| Area | Endpoint | Who |
|------|----------|-----|
| **Auth** | `POST /api/auth/register` (new company and its first Admin; optional `companySlug`) | anyone |
| | `POST /api/auth/login`: returns `accessToken` (15 min) and `refreshToken` (14 days, single use) | anyone |
| | `POST /api/auth/refresh` `{ "refreshToken": "…" }`: new pair; the old refresh token stops working | anyone |
| | `POST /api/auth/logout` `{ "refreshToken": "…" }`: ends the session | anyone |
| | `GET /api/auth/me` | signed in |
| **Company** | `GET /api/company` (name and public slug) | all roles |
| | `PUT /api/company` (rename, change slug) | Admin |
| **Users** | `GET /api/users`, `GET /api/users/{id}`, `POST /api/users`, `PUT /api/users/{id}` | Admin |
| **Hotels** | `GET /api/hotels`, `GET /api/hotels/{id}` | all roles |
| | `POST /api/hotels`, `PUT /api/hotels/{id}` | Admin, Manager |
| | `DELETE /api/hotels/{id}` | Admin |
| **Rooms** | `GET /api/rooms`, `GET /api/rooms/{id}` | all roles |
| | `GET /api/rooms/available?hotelId=&checkIn=&checkOut=&guests=` | all roles |
| | `POST /api/rooms`, `PUT /api/rooms/{id}`, `DELETE /api/rooms/{id}` | Admin, Manager |
| **Guests** | `GET /api/guests`, `GET /api/guests/{id}`, `POST /api/guests`, `PUT /api/guests/{id}` | all roles |
| | `DELETE /api/guests/{id}` | Admin, Manager |
| | `GET /api/guests/{id}/export`: GDPR export of everything held about the guest | Admin, Manager |
| | `POST /api/guests/{id}/anonymize`: GDPR erasure (keeps bookings for accounting) | Admin, Manager |
| **Bookings** | `GET /api/bookings`, `GET /api/bookings/{id}`, `POST /api/bookings` | all roles |
| | `POST /api/bookings/{id}/check-in`, `/check-out`, `/cancel` | all roles |
| | `GET /api/bookings/{id}/payments`, `POST /api/bookings/{id}/payments` | all roles |
| | `GET /api/bookings/{id}/invoice` | all roles |
| **Health** | `GET /health` | anyone |

**Public API**: no login. `{companySlug}` is e.g. `demo-hotels-group`.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/public/{companySlug}/hotels` | List the company's hotels |
| `GET /api/public/{companySlug}/hotels/{hotelId}/offers?checkIn=&checkOut=&guests=` | Available **room types** for a stay, with "from" price and total |
| `POST /api/public/{companySlug}/bookings` | Book a room type with guest details. Returns the booking **reference** and secret **access code** (shown only once) and emails both to the guest. |
| `GET /api/public/{companySlug}/bookings/{reference}` + header `X-Booking-Code` | View the booking |
| `POST /api/public/{companySlug}/bookings/{reference}/cancel` + header `X-Booking-Code` | Cancel, until 24 h before the arrival day |

See [Online booking for guests](#online-booking-for-guests) for a walkthrough.

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
| 400 | Invalid input or a broken rule (past date, too many guests, overpayment, weak password…) |
| 401 | Missing, expired or revoked token; failed login; locked account; invalid refresh token |
| 403 | Your role isn't allowed to do this |
| 404 | Doesn't exist, **or belongs to another company** (also: wrong guest access code) |
| 409 | Conflicts with current state (double booking, wrong status, unpaid balance, record in use…) |
| 429 | Too many requests from your IP to a rate-limited endpoint; wait a minute |

---

## Solution structure

```
Hotel management/                          ← repository root
├── Hotel management.slnx                  Solution file (open this in Visual Studio)
├── docker-compose.yml                     Development stack: PostgreSQL + Mailpit + API
├── docker-compose.prod.yml                Production override: Caddy (TLS), no published DB/API ports, required secrets
├── deploy/Caddyfile                       Reverse proxy config (automatic HTTPS)
├── .env.example                           Template for production secrets
├── dotnet-tools.json                      Local tool manifest (dotnet-ef)
│
├── HotelManagement.Domain/                ── DOMAIN: business entities and rules, no dependencies
│   ├── Common/Common.cs                   Entity / TenantEntity base classes, DomainException, guards
│   ├── Companies/Company.cs               + CompanySlug (generate/validate URL slugs)
│   ├── Users/User.cs                      + UserRole; lockout, security stamp
│   ├── Users/RefreshToken.cs              Single-use refresh token (hash only, rotation family)
│   ├── Hotels/Hotel.cs
│   ├── Rooms/Room.cs                      + RoomType
│   ├── Guests/Guest.cs                    + Anonymize (GDPR erasure)
│   └── Bookings/Booking.cs, Payment.cs    Aggregate: lifecycle, guest cancellation, payments, balance
│
├── HotelManagement.Application/           ── APPLICATION: use cases and the interfaces they need
│   ├── Common/Abstractions.cs             ICurrentUser, ITenantContext/ITenantSelector, IUnitOfWork,
│   │                                      ITokenService, IPasswordHasher, IBookingCodeGenerator, IEmailSender
│   ├── Common/Paging.cs                   PageQuery, PagedResult<T>
│   ├── Contracts/Contracts.cs             Staff request/response DTOs (with validation attributes)
│   ├── Contracts/PublicContracts.cs       Guest-facing DTOs (no internal IDs or room numbers)
│   ├── Contracts/PrivacyContracts.cs      GDPR export DTOs
│   ├── Auth/AuthService.cs                Register (incl. slug), login (lockout, constant-time), "me"
│   ├── Auth/AuthSessionService.cs         Refresh-token issue/rotation/reuse detection, SessionValidator
│   ├── Auth/PasswordPolicy.cs             Length + common/sequential/personal-info checks
│   ├── Companies/CompanyService.cs        View/update own company and its slug
│   ├── Users/UserService.cs               + IUserRepository, IUserQueries
│   ├── Hotels/HotelService.cs             + IHotelRepository, IHotelQueries
│   ├── Rooms/RoomService.cs               + IRoomRepository, IRoomQueries
│   ├── Guests/GuestService.cs             + IGuestRepository, IGuestQueries
│   ├── Guests/GuestPrivacyService.cs      GDPR export and anonymization
│   ├── Bookings/                          BookingService, PaymentService, InvoiceService,
│   │                                      RoomReservation (shared lock → check → create) + abstractions
│   ├── PublicBooking/                     Guest-facing use cases:
│   │                                      PublicTenantResolver (slug → company), PublicCatalogService,
│   │                                      GuestBookingService, BookingEmailNotifier
│   └── DependencyInjection.cs             AddApplication()
│
├── HotelManagement.Infrastructure/        ── INFRASTRUCTURE: database, security, email
│   ├── Persistence/
│   │   ├── AppDbContext.cs                EF Core context: tenant filters/stamping, encryption, blind indexes
│   │   ├── EntityConfigurations.cs        Table mappings (one class per entity); `.IsEncrypted()` marks personal data
│   │   ├── PersonalDataConventions.cs     Encryption value converters + blind-index maintenance
│   │   ├── PersonalDataBackfill.cs        Encrypts legacy plaintext rows at startup
│   │   ├── Repositories.cs                Write side: load/save entities
│   │   ├── Queries.cs                     Read side: SQL projections straight to DTOs
│   │   ├── UnitOfWork.cs                  Transactions; translates DB errors to DomainException
│   │   ├── DatabaseInitializer.cs         Migrate on startup + demo data seeder
│   │   └── Migrations/                    EF Core migrations
│   ├── Security/Security.cs               JWT creation (with security stamp), PBKDF2 hashing, refresh-token generator
│   ├── Security/FieldEncryption.cs        AES-256-GCM field encryption with key rotation + HMAC blind index
│   ├── Security/ProductionSafety.cs       Startup validators that reject public dev secrets outside Development
│   ├── Security/BookingCodeGenerator.cs   Cryptographically random references and access codes
│   ├── Email/EmailSenders.cs              SMTP sender (MailKit) + log-only fallback
│   └── DependencyInjection.cs             AddInfrastructure()
│
├── Hotel management/                      ── API: HTTP layer and composition root
│   ├── Program.cs                         Wires everything together; middleware pipeline
│   ├── Controllers/                       Thin controllers: HTTP in, service call, HTTP out
│   ├── Auth/WebAuth.cs                    Role names, ICurrentUser from the JWT
│   ├── Auth/JwtAuthentication.cs          Bearer validation + per-request session (stamp) check
│   ├── Errors/DomainExceptionHandler.cs   DomainException → 400/401/404/409 problem response
│   ├── OpenApi/OpenApiSetup.cs            OpenAPI document + Swagger UI + Authorize button
│   ├── RateLimiting/RateLimitPolicies.cs  Per-IP limits for public and auth endpoints
│   ├── Security/TransportSecurity.cs      Forwarded headers, HTTPS redirect, HSTS, security headers
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
│ EF Core, JWT, crypto │   └────────────┬─────────────┘
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
| **S**ingle responsibility | Controllers only handle HTTP. Services orchestrate one use-case area each. Bookings are split into `BookingService` (lifecycle), `PaymentService` and `InvoiceService`. The guest side is split into catalog, booking and email notification. `RoomReservation` is the one place that reserves a room, for both staff and online bookings. Repositories handle writes, query classes handle reads. Each entity has its own mapping class. |
| **O**pen/closed | Tenant filters are applied automatically to every `ITenantEntity`, so a new tenant-owned entity is isolated without any change to `AppDbContext`. In the same way, a column marked `.IsEncrypted()` is encrypted, and a blind index declared with `HasBlindIndex(...)` is maintained, both without any new plumbing. New entity mappings are picked up automatically via `ApplyConfigurationsFromAssembly`. |
| **L**iskov substitution | All tenant-owned entities derive from `TenantEntity`, and the company-assignment rules hold for every one of them. |
| **I**nterface segregation | Small, role-specific interfaces: `IRoomRepository` (write) is separate from `IRoomQueries` (read), and `ITokenService` from `IPasswordHasher`. The tenant is *read* through `ITenantContext`, but only public flows can *switch* it, through `ITenantSelector`. |
| **D**ependency inversion | Services depend on abstractions only. Database errors are translated into `DomainException` inside Infrastructure, so no layer above it references Npgsql. Email goes through `IEmailSender`, so SMTP, the log fallback and the test capture are interchangeable. |

### Rich domain model

Business rules live **on the entities**, not in services. Entities have private setters and expose behaviour:

```csharp
var booking = Booking.Create(room, guest, checkIn, checkOut, guests, notes, now, reference); // validates dates, capacity…
booking.MarkCheckedIn(now);                                              // only if Confirmed and on/after arrival
booking.RecordPayment(100m, PaymentMethod.Card, "Deposit", now, userId);  // can't exceed balance
booking.MarkCheckedOut(now);                                             // only if fully paid

user.RegisterFailedLogin(now, maxAttempts: 5, lockoutDuration);          // locks after 5 in a row
guest.Anonymize(now);                                                    // GDPR erasure, irreversible
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
JwtBearer middleware ── validates signature/expiry, builds ClaimsPrincipal (sub, role, company_id, stamp)
  │                     then SessionValidator checks the stamp is current and the user active (else 401)
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

### Online booking for guests

Guests book **without an account**. Their company is identified by its slug in the URL, and the booking is protected by a secret code.

**Try it with the demo data** (Swagger groups these under *Public: catalog* and *Public: bookings*):

```bash
# 1. Browse: no token needed
GET  /api/public/demo-hotels-group/hotels
GET  /api/public/demo-hotels-group/hotels/{hotelId}/offers?checkIn=2026-12-01&checkOut=2026-12-04&guests=2

# 2. Book a room type
POST /api/public/demo-hotels-group/bookings
{ "hotelId": "...", "roomType": "Suite", "checkIn": "2026-12-01", "checkOut": "2026-12-04",
  "numberOfGuests": 2, "firstName": "Gina", "lastName": "Guest", "email": "gina@example.com" }
→ 201 { "accessCode": "T6S5-1PKR-6S04", "booking": { "reference": "BK-TAWD8AC1", "cancellableUntil": "...", ... } }

# 3. The confirmation email shows up in Mailpit: http://localhost:8025

# 4. View or cancel with the reference + code
GET  /api/public/demo-hotels-group/bookings/BK-TAWD8AC1          X-Booking-Code: T6S5-1PKR-6S04
POST /api/public/demo-hotels-group/bookings/BK-TAWD8AC1/cancel   X-Booking-Code: T6S5-1PKR-6S04
```

**How it works**

```
GET/POST /api/public/{companySlug}/...       (anonymous, rate-limited per IP)
  │
  ▼
PublicTenantResolver ── looks up the company by slug (404 if unknown)
  │                     and selects it in TenantContext for this request
  ▼                     → every query/save is now scoped to that company, like a staff request
GuestBookingService.CreateAsync
  ├─ Booking.ValidateStay(...)                 dates checked before touching the DB
  ├─ find guest by email, or create one        returning guests reuse their record
  ├─ rooms.FindFreeRoomIdsAsync(type, ...)     candidates, cheapest first
  ├─ for each candidate:
  │    RoomReservation.TryReserveAsync(...)    lock room → domain checks → overlap check
  │    → first free one wins                   (if another guest just took it, try the next)
  ├─ commit
  └─ BookingEmailNotifier                      confirmation email; a delivery failure is logged,
                                               the booking still succeeds
```

**Security**
- **Access code:** 60 random bits, generated with a cryptographic RNG. It's returned once and stored only as a **hash**, like a password.
- **No probing:** a wrong code and an unknown reference both return **404**, so references can't be guessed.
- **Staff bookings stay private:** they have no access code, so they can never be opened through the public API.
- **Rate limits:** by default 60 public requests and 10 bookings or cancellations per minute per IP.
- **Little data exposed:** guests see the room *type* but never room numbers or internal guest IDs.

**For staff**, online bookings appear in `GET /api/bookings` like any other booking, with `"source": "Online"` and a `reference`. Filter them with `?source=Online`, or find one with `?reference=BK-…`.

---

## Domain model

```
Company (tenant)          slug (public URL name)
  ├── User                email (globally unique login), role, password hash, active flag,
  │     │                 security stamp, failed-login count, lockout end
  │     └── RefreshToken  token hash, family (one per login session), expiry, revocation
  ├── Guest               🔒 name, 🔒 email (+ blind index, unique per company), 🔒 phone, anonymized-at
  └── Hotel               name, address, contact
        └── Room          number (unique per hotel), type, capacity, price/night, floor, active
              └── Booking reference (BK-…), source (Staff/Online), guest, dates [checkIn, checkOut),
                    │     party size, captured price, status, timestamps, 🔒 notes,
                    │     access-code hash (online only)
                    └── Payment   type (Payment/Refund), amount, method, reference

🔒 = encrypted at rest (AES-256-GCM)
```

| Entity | Key behaviour |
|--------|---------------|
| `Booking` | `Create`, `ValidateStay`, `MarkCheckedIn`, `MarkCheckedOut`, `Cancel`, `CancelByGuest` (deadline-checked), `RecordPayment`, `RecordRefund`. Exposes `Nights`, `AmountDue`, `TotalPaid`, `TotalRefunded`, `Balance` and `GuestCancellationDeadline`. |
| `Company` | `Rename`, `ChangeSlug`. `CompanySlug.FromName` turns "Hôtel Du Lac" into `hotel-du-lac`. |
| `Room` | `EnsureCanHost(guests)` (active and within capacity); belongs to a hotel of the same company |
| `User` | `Update(...)`: an Admin can't demote or deactivate themselves, and role or active changes rotate the security stamp. Also `RegisterFailedLogin` / `RegisterSuccessfulLogin` / `IsLockedOut` (lockout), `ChangePasswordHash` (rehash on login) and `RotateSecurityStamp`. |
| `RefreshToken` | `IsUsable`, `Revoke(reason, replacedBy)`: single use, rotation chain, revocation reason |
| `Guest` | `Update`; `Anonymize` irreversibly clears personal data, after which edits are refused |
| All tenant entities | `AssignCompany` sets the company once and never changes it |

**Booking statuses:**

```
            check-in                check-out (balance must be 0)
Confirmed ───────────► CheckedIn ───────────────────────────► CheckedOut
    │
    │ cancel
    ▼
Cancelled  (amount due becomes 0; anything paid can be refunded)
           staff: any time while Confirmed · guest (online): until 24 h before arrival day
```

---

## Authentication, roles and multi-tenancy

**Sign-up and login**
- `POST /api/auth/register` creates a **company** plus its first **Admin** user and starts a session.
- `POST /api/auth/login` returns two tokens:
  - an **access token**: a JWT valid for 15 minutes;
  - a **refresh token**: valid for 14 days and usable once.
- `POST /api/auth/refresh` swaps the refresh token for a new pair. The client should do this shortly before the access token expires.
- `POST /api/auth/logout` ends the session.
- Admins add staff with `POST /api/users`. Login emails are unique across the whole system.

**Sessions and revocation**

```
login ──► access token (15 min) + refresh token R1 ──┐
                                                       │  POST /refresh with R1
          access token + R2  ◄─────────────────────────┘  R1 is now used up (rotated)
              │
              │  someone presents R1 again? → it was stolen: R2 and the whole session are revoked
```

- **Instant revocation:** every request checks the token's `stamp` claim against the user's current *security stamp*. An admin deactivating a user or changing their role changes the stamp, so the user's existing tokens stop working **on their next request**. Their refresh tokens are revoked as well.
- **Stored as hashes:** refresh tokens are kept only as SHA-256 hashes, and each login is its own "family" of rotated tokens.

**Token contents**

| Claim | Holds |
|-------|-------|
| `sub` | User ID |
| `email` | Email address |
| `name` | Full name |
| `role` | `Admin`, `Manager` or `Receptionist` |
| `company_id` | The user's company |
| `stamp` | The user's security stamp at issue time (see above) |

**Authorization**
- Every endpoint requires a token by default (a fallback policy). Public endpoints opt out with `[AllowAnonymous]`.
- Role limits use `[Authorize(Roles = Roles.AdminOrManager)]` and similar.

**Tenant isolation.** This is the core security feature, and it's enforced in the data layer:
1. `TenantContext` works out the current company:
   - **Staff requests:** taken from the token's `company_id` claim (via `HttpCurrentUser`).
   - **Public requests:** set by `PublicTenantResolver` from the company slug in the URL.
2. `AppDbContext` adds a **global query filter**, `CompanyId == current company`, to every entity implementing `ITenantEntity`. This happens automatically.
3. So *every* query, including lookups by ID, only sees the caller's rows. Another company's ID returns **404**: its existence isn't even revealed.
4. On save, new rows are automatically stamped with the caller's company. Changing an existing row's company throws.
5. Entities also check this themselves: a booking's room and guest must belong to the same company.

---

## Security

An overview of how user data and access are protected, and where each part lives in the code.

### Credentials

| What | How | Where |
|------|-----|-------|
| **Staff passwords** | PBKDF2-HMAC-SHA512 with **100,000 iterations** and a **random 128-bit salt per password** (ASP.NET Core Identity v3 format). Hashes using older, weaker settings are upgraded automatically at the next login. | `IdentityPasswordHasher` |
| **Password policy** | At least 10 characters (max 128), following NIST SP 800-63B. Common passwords (`password123`, `Passw0rd!`…), repetitive ones, sequences and passwords containing the user's name or email are rejected. | `PasswordPolicy` |
| **Brute-force protection** | 5 failed logins in a row lock the account for 15 minutes. Login, sign-up and refresh are also limited to 10 requests per minute per IP. | `User.RegisterFailedLogin`, `RateLimitPolicies` |
| **No account discovery** | Every login failure (unknown email, wrong password, locked, deactivated) gets the same message. The password hash is checked even for unknown emails, so response timing reveals nothing. Sign-up doesn't say "already exists". | `AuthService` |
| **Guest access codes** | 60-bit random codes, hashed like passwords and checked in constant time. A wrong code returns 404, just like an unknown booking. | `BookingCodeGenerator`, `GuestBookingService` |
| **Tokens** | Access tokens are HMAC-SHA256 JWTs, with only that algorithm accepted, valid for 15 minutes and revocable through the security stamp. Refresh tokens are 256-bit random values, stored only as SHA-256 hashes, single-use, and detect reuse. | `JwtTokenService`, `AuthSessionService` |

### Personal data at rest

- **What's encrypted:** guest first and last names, email and phone, plus booking notes. The **database only holds ciphertext** (`enc:k1:…`); the API decrypts these values transparently.
- **Encryption method:** **AES-256-GCM** with a fresh random nonce per value. It's authenticated, so tampering is detected, and identical values don't produce identical ciphertext.
- **Email lookups:** emails are found through a **blind index**, a keyed HMAC-SHA256 of the email held in a separate column. This keeps "find a returning guest" and "one guest per email per company" working without storing the email in readable form.
- **Key rotation:** each ciphertext records its key id, so you can switch to a new key (`ENCRYPTION_KEY_ID` + `ENCRYPTION_KEY`) and keep old keys under `Encryption:RetiredKeys:{id}` for reading older data.
- **Upgrades:** data written before encryption existed is encrypted automatically at startup (`PersonalDataBackfill`).
- **Adding a new sensitive column:** one line in its mapping, `e.Property(x => x.Something).IsEncrypted();`.
- **Not encrypted:** staff names and emails. Staff emails are their login names, and staff data is the company's own business data, not guest personal data.

### GDPR

- **Right of access:** `GET /api/guests/{id}/export` returns everything held about a guest: personal data, bookings and payments.
- **Right to erasure:** `POST /api/guests/{id}/anonymize` irreversibly replaces the name with "Anonymized Guest", removes the email, phone and booking notes, and keeps the bookings and payments (which no longer identify the person) for accounting. It's refused while the guest has an upcoming or current stay.

### Transport and deployment

- **HTTPS:** in production, Caddy (`docker-compose.prod.yml`) terminates TLS with automatic certificates and redirects HTTP to HTTPS. The API sends **HSTS**, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY` and `Referrer-Policy: no-referrer`. Auth responses are `Cache-Control: no-store`.
- **Behind the proxy:** forwarded headers are trusted only in that setup (`Https:BehindProxy`), so rate limits use the real client IP.
- **No exposed internals:** the database and Mailpit listen on `127.0.0.1` only in development, and aren't published at all in production.
- **Startup checks:** outside Development the API **refuses to start** if the JWT or encryption keys are any of the public development values in this repo, if a key is malformed, or if demo seeding is on. The production Compose file also refuses to start if any secret is missing.
- **Logs:** never contain email bodies (and so no access codes). Recipient addresses are masked (`g***@example.com`).

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
| **Online booking** | The guest picks a room *type*, and the cheapest free room of that type that fits the party is assigned. If it's taken in the meantime, the next one is tried. If none is free, the result is 409. Payment happens at the hotel. |
| **Returning guests** | Online bookings are matched to an existing guest record by email (case-insensitive), so staff see one record per person. |
| **Guest cancellation** | Only `Confirmed` online bookings, until `PublicBooking:CancellationCutoffHours` (default 24) before the start of the arrival day (UTC). After that, the guest must contact the hotel. |
| **Booking references** | Every booking, staff or online, gets a unique reference like `BK-7K3P9QXM`. It uses Crockford Base32, which has no ambiguous 0/O or 1/I/L characters. |
| **Company slugs** | 3–60 characters of lowercase letters, digits and single hyphens, unique. Generated from the name at sign-up (`-2`, `-3`… if taken), or chosen explicitly. Admins can change it, which changes the public URLs. |

---

## Database

- **PostgreSQL 17** in Docker. Data is kept in the `pgdata` volume.
- **Migrations:** EF Core migrations live in `HotelManagement.Infrastructure/Persistence/Migrations` and are **applied automatically on startup** (`Database:MigrateOnStartup`).
- **Seeding:** demo data is added on startup only when `Database:SeedSampleData` is true *and* the database has no companies yet. Seeding is on in Development and off by default otherwise.
- **Constraints beyond EF:** the `InitialCreate` migration adds the anti-overlap exclusion constraint and a `CheckOut > CheckIn` check constraint with raw SQL.
- **Upgrading existing data:** databases created by earlier versions upgrade in place on startup.
  - `PublicBooking` adds company slugs and booking references, backfilled for existing rows: a slug from the company name, and a random reference per booking.
  - `SecurityHardening` adds lockout fields, security stamps, refresh tokens and the email blind index. Right after migrating, `PersonalDataBackfill` encrypts existing guest data and booking notes, which are readable throughout. Every user gets a new security stamp, so **everyone has to log in once more** after this upgrade.

**Changing the model**

```bash
dotnet tool restore     # installs dotnet-ef from dotnet-tools.json
dotnet ef migrations add <Name> --project HotelManagement.Infrastructure --startup-project "Hotel management" -o Persistence/Migrations
# the migration is applied next time the API starts
```

**Connecting with a DB client** (pgAdmin, DBeaver, DataGrip…): host `localhost`, port **5433**, database `hotel`, user `hotel`, password `hotel`. This works from your own machine only, since the port is bound to `127.0.0.1`. Guest names, emails, phones and booking notes show up as `enc:…` ciphertext there; that's expected.

---

## Configuration

Configuration comes from `appsettings.json`, then `appsettings.{Environment}.json`, then **environment variables**. Each source overrides the one before it. Environment variables use `__` for nesting (`Jwt__Key`).

| Setting | Default | Purpose |
|---------|---------|---------|
| `ConnectionStrings__Default` | `localhost:5433` (`db:5432` in Docker) | PostgreSQL connection string |
| `Jwt__Key` | dev key in Development only | HMAC signing key, ≥ 32 chars. **Required.** Outside Development, the public dev keys are refused. |
| `Jwt__Issuer` / `Jwt__Audience` | `HotelManagement` / `HotelManagement.Api` | Token issuer/audience |
| `Jwt__ExpiryMinutes` | `15` | Access-token lifetime |
| `RefreshTokens__RefreshTokenDays` | `14` | Refresh-token lifetime |
| `LoginSecurity__MaxFailedAttempts` / `LoginSecurity__LockoutMinutes` | `5` / `15` | Account lockout after failed logins |
| `Encryption__KeyId` / `Encryption__Key` | dev key in Development only | Active key (32 bytes, base64) for encrypting guest personal data. **Required.** |
| `Encryption__RetiredKeys__{id}` | none | Old keys, kept for reading data written before a key rotation |
| `Encryption__BlindIndexKey` | dev key in Development only | Key for searchable email hashes. **Required; must differ from `Encryption__Key`; don't rotate.** |
| `Https__BehindProxy` | `false` (`true` in the prod override) | Trust `X-Forwarded-*` from the reverse proxy |
| `Https__RequireHttps` / `Https__HstsMaxAgeDays` | `true` / `365` | HTTPS redirect + HSTS outside Development |
| `Database__MigrateOnStartup` | `true` | Apply migrations at startup |
| `Database__SeedSampleData` | `true` in Development | Seed demo data into an empty database |
| `ASPNETCORE_ENVIRONMENT` | `Development` in Docker Compose (`Production` in the prod override) | `Development` enables Swagger, the public dev keys and demo seeding, and turns off the HTTPS redirect. Never use it on a real server. |
| `Email__Host` / `Email__Port` | `mailpit:1025` in Docker, `localhost:1025` in Development, empty otherwise | SMTP server for guest emails. **Empty means emails are written to the log instead of sent.** |
| `Email__Security` | `Auto` (`None` for Mailpit) | `None`, `Auto`, `SslOnConnect`, `StartTls`, `StartTlsWhenAvailable` |
| `Email__Username` / `Email__Password` | empty | SMTP credentials, if your relay needs them |
| `Email__FromAddress` / `Email__FromName` | `no-reply@hotel-management.local` / `Hotel Management` | Sender of guest emails |
| `PublicBooking__CancellationCutoffHours` | `24` | How long before the arrival day guests can still cancel online |
| `RateLimiting__PublicPerMinute` | `60` | Public API requests per minute per IP |
| `RateLimiting__PublicWritePerMinute` | `10` | Public bookings and cancellations per minute per IP |
| `RateLimiting__AuthPerMinute` | `10` | Login, sign-up, refresh and logout requests per minute per IP |

**Production:** copy `.env.example` to `.env` and fill it in (see [Deploying to production](#deploying-to-production)):

```env
DOMAIN=hotels.example.com
POSTGRES_PASSWORD=...          # openssl rand -hex 24
JWT_KEY=...                    # openssl rand -base64 48
ENCRYPTION_KEY_ID=k1
ENCRYPTION_KEY=...             # openssl rand -base64 32   ← back this up!
BLIND_INDEX_KEY=...            # openssl rand -base64 32   ← back this up, never rotate
SMTP_HOST=smtp.example.com     # your real mail relay (SendGrid, Mailgun, Office 365, …)
SMTP_PORT=587
SMTP_SECURITY=StartTls
SMTP_USERNAME=...
SMTP_PASSWORD=...
```

In Production, Swagger is off. The API refuses to start with missing, malformed or public development secrets, or with demo seeding switched on.

---

## Testing

```bash
dotnet test                              # everything
dotnet test tests/HotelManagement.UnitTests   # fast tests only, no Docker needed
```

| Project | What it tests | How | Speed |
|---------|---------------|-----|-------|
| `HotelManagement.UnitTests` (129 tests) | Domain rules (booking lifecycle, guest cancellation deadline, payments, refunds, validation, slugs, tenant assignment, lockout) and application services (lock → load → save → commit ordering, falling back to the next free room, guest matching, wrong-code handling, email composition and failure handling). Also auth and security: constant-time login, lockout, rehashing, refresh rotation and reuse detection, the password policy, AES-GCM round trips, tampering and key rotation, the blind index, hash parameters and the production-safety validators. | Pure C#. Services get mocked interfaces (NSubstitute) and a fake clock (`FakeTimeProvider`). | ~0.1 s |
| `HotelManagement.Tests` (65 tests) | The whole API over HTTP: auth, roles (403s), **tenant isolation**, double booking, **concurrent booking/payment races**, payments → invoice → check-out, paging and filters. Also the **public booking flow**: browsing, booking, code checks, cancellation window, sold-out handling, concurrent online bookings, slugs and **rate limiting**. Also **security end to end**: lockout, auth rate limits, weak passwords, refresh and logout, **instant revocation** on deactivation and role change, **ciphertext actually stored in PostgreSQL**, GDPR export and anonymization, and the app refusing to start in Production with unsafe settings. | `WebApplicationFactory` boots the real app against a throwaway PostgreSQL started by **Testcontainers**. Emails are captured in memory so tests can read them. | ~5 s (+ container start) |

Integration tests need Docker running. Each test registers its own new company, so tests don't interfere with each other, and that doubles as a check of the tenant isolation.

---

## Adding a new feature

Example: adding **room service orders** to a booking.

1. **Domain:** create an `Order` entity deriving from `TenantEntity`, with its rules as methods. It automatically gets tenant isolation.
2. **Application:** in `Contracts/`, add request/response DTOs with validation attributes. In a new `Orders/` folder, define `IOrderRepository`/`IOrderQueries` and an `IOrderService` + `OrderService`. Register the service in `Application/DependencyInjection.cs`.
3. **Infrastructure:** add an `IEntityTypeConfiguration<Order>`, a `DbSet` in `AppDbContext`, and the repository and query implementations. Register them in `Infrastructure/DependencyInjection.cs`. If a column can hold personal data (e.g. free-text special requests), mark it `.IsEncrypted()`; it's encrypted with no further code. Then create a migration (see [Database](#database)).
4. **API:** add a thin `OrdersController` that calls `IOrderService`, with `[Authorize(Roles = …)]` where needed. Endpoints are private by default (fallback policy). If one must be anonymous, add `[AllowAnonymous]` *and* `[EnableRateLimiting(...)]`.
5. **Tests:** unit-test the entity rules and the service. Add an integration test for the endpoint, including a cross-company check.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `Failed to connect to 127.0.0.1:5432` / `Connection refused` | The database isn't running, or you're using an old port | `docker compose up -d db`. The hotel DB listens on **5433**. |
| `password authentication failed for user "hotel"` | Another PostgreSQL is answering, e.g. one installed in **WSL** or natively on Windows on port 5432 | Make sure your connection string uses port **5433** (the default here). Check with `netstat -ano \| findstr :5432`. |
| Running the **Container (Dockerfile)** profile can't reach the DB | In a container, `localhost` is the container itself | That profile already uses `host.docker.internal:5433`. Keep the `db` container running. |
| Integration tests fail to start | Docker isn't running | Start Docker Desktop. Unit tests run without it. |
| Swagger calls start returning `401` | The 15-minute access token expired | Log in again, or call `POST /api/auth/refresh` with the `refreshToken`, then click **Authorize** with the new token |
| Everyone got logged out after an upgrade | The `SecurityHardening` migration gave every user a new security stamp | Expected and one-time: log in again |
| Guest emails don't show up in Mailpit | Mailpit isn't running, or no SMTP host is configured | `docker compose up -d mailpit`, then open http://localhost:8025. With `Email:Host` empty, emails only appear in the API log. Failed sends are logged as warnings and never fail the booking. |
| `429 Too Many Requests` | A per-IP rate limit was hit, on the public API or on login/sign-up/refresh | Wait a minute, or raise `RateLimiting__PublicPerMinute` / `__PublicWritePerMinute` / `__AuthPerMinute` |
| Public URL returns 404 for a company | Wrong slug, or the slug was changed | `GET /api/company` (as staff) shows the current slug |
| Want a clean database | | `docker compose down -v && docker compose up -d --build` |
| API exits at startup with `OptionsValidationException` | Missing, malformed or unsafe configuration: no or short `Jwt:Key`, a public dev key outside Development, a bad encryption key, or demo seeding on | Read the message: it names the setting. Generate real secrets as described in [Deploying to production](#deploying-to-production). |
| `docker compose … prod.yml` says `required variable … is missing a value` | A secret isn't set in `.env` | Fill in every value from `.env.example` |
| `401` right after an admin changed a user | The user's role or active state changed, so their old tokens were revoked on purpose | Log in again |
| Login keeps failing with the right password | The account is locked for 15 minutes after 5 failed attempts | Wait for the lockout to end |
| `CryptographicException: No decryption key configured for key id` | Data was encrypted with a key that is no longer configured | Add the old key under `Encryption__RetiredKeys__{id}` |

---

## Known limitations

- **Guest search:** encrypted guest data can't be searched by the database, so guest search decrypts the company's guests in memory. That's fine for thousands of guests per company; beyond that, a dedicated search index would be needed.
- **Encryption keys:** the encryption keys come from environment variables. For high-security deployments, use a key management service or vault (Azure Key Vault, AWS KMS, HashiCorp Vault). Losing the keys means losing the encrypted data, so back them up.
- **Staff data:** staff names and emails aren't encrypted; staff emails are their login names.
- **Time zones:** "today" and the guest cancellation deadline are computed in **UTC**, not each hotel's local time zone. This affects check-in, blocking past-dated bookings and the cancellation window.
- **Payments:** payments and refunds are records only. No payment provider is integrated, so online bookings are paid at the hotel.
- **Cancellation fees:** cancelling a confirmed booking always makes the amount due 0.
- **Lost access codes:** a guest who loses their access code can't get it re-sent online; staff can still find the booking by reference or guest. There's no "resend confirmation" endpoint yet.
- **Guest names:** a returning guest's name and phone aren't updated from a new online booking. The existing staff-maintained record is kept as it is.
