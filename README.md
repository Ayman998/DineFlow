# DineFlow

Scan, order and pay at the table. Guests scan a table QR, browse a branded e-menu, order, split the bill, tip, pay, and rate the visit. Restaurants see live orders, payments and reviews.

The spec is the Df3Pay Developer Blueprint v1.1, and the screen designs are the Df3Pay mockups.

## Stack

| Part | Tech |
|---|---|
| API | ASP.NET Core 8 Web API, modular monolith, Clean Architecture |
| Data | EF Core 8 + PostgreSQL 16, Redis 7 |
| Web | Angular 21 workspace: `guest` (mobile PWA, EN/AR + RTL) and `merchant` (dashboard), Tailwind CSS 4 |

## Layout

```
src/
  DineFlow.Domain/          entities, Money (integer minor units), tenant marker
  DineFlow.Application/     use cases and abstractions (ITenantContext)
  DineFlow.Infrastructure/  EF Core DbContext + migrations, health checks
  DineFlow.Api/             controllers, Serilog, CORS, health endpoints
tests/DineFlow.Tests/       xUnit
web/
  projects/guest/           guest PWA       → http://localhost:4200
  projects/merchant/        merchant portal → http://localhost:4300
docker-compose.yml          postgres + redis
```

## Prerequisites

- .NET SDK 10 (pinned in `global.json`). It builds the `net8.0` projects, and the .NET 8 runtime runs them.
- Node 20.19+ and npm
- Docker Desktop

## Run locally

```bash
docker compose up -d
```

```bash
dotnet tool restore
```

```bash
dotnet ef database update -p src/DineFlow.Infrastructure -s src/DineFlow.Api
```

```bash
dotnet run --project src/DineFlow.Api --launch-profile http
```

The API listens on http://localhost:5266. Swagger is at `/swagger`, and readiness (Postgres + Redis) is at `/health/ready`.

```bash
npm --prefix web ci
```

```bash
npm --prefix web run start:guest
```

```bash
npm --prefix web run start:merchant
```

The Angular dev servers proxy `/v1`, `/health` and `/hubs` to the API, so both apps call relative URLs.

## Test

```bash
dotnet test DineFlow.slnx
```

```bash
npm --prefix web test
```

## Ground rules (from the blueprint)

- Money is `long` minor units plus an ISO currency code. Never use floating point. See `Money`.
- Every tenant-owned entity implements `ITenantOwned` and gets an automatic `TenantId` query filter.
- Only the verified PSP webhook confirms a payment. Every write carries an idempotency key.
- If the payment succeeds but posting to the POS fails, the state becomes `POSTING_PENDING` and the post is retried. The guest is never charged twice.
- Never store card numbers or CVV. Use the PSP's hosted fields.

## Roadmap

| Sprint | Scope | Epics |
|---|---|---|
| 0 | Scaffold, DB, docker-compose, CI ✅ | — |
| 1 | Tenant / venue / table / QR token, guest session | E1, E2 |
| 2 | Menu admin + guest e-menu, modifiers | E4, E31 |
| 3 | Cart, server-validated order, staff alerts (SignalR) | E6, E10 |
| 4 | Bill, payment intent, PSP webhook, tip, promo | E5, E8, E30 |
| 5 | Split ledger: equal and by item, with concurrency locks | E7 |
| 6 | Reviews and feedback | E27 |
| 7 | Merchant overview, tables, transactions, refunds | E11, E12 |
| 8 | Payment links | E16 |
| 9 | Hardening, Foodics connector, WhatsApp, email reports | E9, E15, E28 |
