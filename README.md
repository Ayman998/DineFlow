# DineFlow

Scan, order and pay at the table. Guests scan a table QR, browse a branded e-menu, order, split the bill, tip, pay, and rate the visit. Restaurants see live orders, payments and reviews.

The spec is the Df3Pay Developer Blueprint v1.1, and the screen designs are the Df3Pay mockups.

## Stack

| Part | Tech |
|---|---|
| API | ASP.NET Core 8 Web API, modular monolith, Clean Architecture, JWT auth |
| Data | EF Core 8 + SQL Server 2022 |
| Web | Angular 21 workspace: `guest` (mobile PWA, EN/AR + RTL) and `merchant` (dashboard), Tailwind CSS 4 |

## Layout

```
DineFlow/
  DineFlow.Api/             controllers, auth policies, rate limits, error responses
  DineFlow.Application/     use cases (guests, venues, tables, auth) and abstractions
  DineFlow.Domain/          entities: tenant, venue, area, location, QR token, guest session, staff
  DineFlow.Infrastructure/  EF Core + SQL Server, migrations, QR/JWT/password services, demo seed
  DineFlow.Test/            xUnit (services run against in-memory SQLite)
web/
  projects/guest/           guest PWA       → http://localhost:4200
  projects/merchant/        merchant portal → http://localhost:4300
```

## Prerequisites

- .NET SDK 10 (pinned in `global.json`). It builds the `net8.0` projects, and the .NET 8 runtime runs them.
- SQL Server 2022 on `localhost`, with Windows sign-in. Change `ConnectionStrings:DineFlow` in `appsettings.json` to use another server.
- Node 20.19+ and npm

## Run locally

```bash
dotnet run --project DineFlow/DineFlow.Api --launch-profile http
```

In Development, the API creates the `DineFlow` database, applies migrations and seeds a demo tenant on first start. The demo is **Demo Bistro, Dubai Marina**: tables 1–8 in Main hall, 9–12 on the Terrace, a counter, an owner account, and two bilingual menus. "All day" includes subcategories, modifiers, allergens and one sold-out item. "Breakfast" is served 07:00–11:30 only. The owner's sign-in details are under `Seed:Owner` in `DineFlow/DineFlow.Api/appsettings.Development.json`.

The API listens on http://localhost:5266. Swagger is at `/swagger`, and readiness (SQL Server) is at `/health/ready`.

```bash
npm --prefix web ci
```

```bash
npm --prefix web start
```

That serves both apps. Sign in to the merchant portal at http://localhost:4300, open **Tables**, and click **Open** on any table to see its guest landing page. From there, **View Menu** opens the e-menu. Manage menus, categories, items, modifiers and photos under **Menu**. The Angular dev servers proxy `/v1`, `/health` and `/hubs` to the API, so both apps call relative URLs.

To apply migrations without starting the API:

```bash
dotnet ef database update -p DineFlow/DineFlow.Infrastructure -s DineFlow/DineFlow.Api
```

### From Visual Studio

`DineFlow.slnx` includes the Angular workspace as `DineFlow.Web` (`web/DineFlow.Web.esproj`). Pick the **API + Web** launch profile in the toolbar's startup dropdown and press F5. That starts the API and runs `npm start`, which serves both Angular apps.

### Keys and secrets

`appsettings.Development.json` holds **development-only** values: the JWT signing key, the QR token key and the demo owner's password. Outside Development, `Jwt:SigningKey` and `Qr:TokenKey` are required (base64, at least 32 bytes), and the API refuses to start without them. Set them with environment variables, user-secrets or a key vault. Changing `Qr:TokenKey` invalidates every printed QR code.

### Uploaded images

Menu photos (JPEG, PNG or WebP, up to 5 MB, checked by file content) are saved under `Storage:LocalRoot`, which defaults to `DineFlow/DineFlow.Api/App_Data/media`. The API serves them at `/media/...` with year-long caching, and that folder is git-ignored. Production should swap `LocalFileStorage` for S3-compatible storage behind the same `IFileStorage` interface.

## Test

```bash
dotnet test DineFlow.slnx
```

```bash
npm --prefix web test
```

## API

| Endpoint | Who | Purpose |
|---|---|---|
| `GET /v1/public/qr/{token}` | anyone (rate limited) | Resolve a scanned QR and start a guest session |
| `GET /v1/public/sessions/{id}` | guest token | Reload the guest's own session |
| `POST /v1/merchant/auth/login`, `GET /v1/merchant/auth/me` | — / merchant | Sign in, current user |
| `GET/POST /v1/merchant/venues`, `PUT /v1/merchant/venues/{id}/settings` | merchant / manager | Venues and settings |
| `GET /v1/merchant/venues/{id}/tables`, `POST /v1/merchant/venues/{id}/areas` | merchant / manager | Floor plan, areas |
| `POST /v1/merchant/tables`, `POST /v1/merchant/tables/bulk` | manager | Add locations (each gets a QR) |
| `POST /v1/merchant/tables/{id}/qr/rotate`, `/qr/disable`, `/qr/enable` | manager | Manage a location's QR |
| `GET /v1/public/sessions/{id}/menu` | guest token | Published menus, with what can be ordered right now |
| `GET /v1/merchant/venues/{id}/menu` | merchant | Every menu, including drafts |
| `POST /v1/merchant/venues/{id}/menus`, `PUT`/`DELETE /v1/merchant/menu/menus/{id}` | manager | Menus and their schedules |
| `POST /v1/merchant/menu/categories`, `PUT`/`DELETE …/categories/{id}` | manager | Categories and subcategories |
| `POST /v1/merchant/menu/items`, `PUT`/`DELETE …/items/{id}` | manager | Items with modifiers, allergens and tags |
| `PUT /v1/merchant/menu/items/{id}/availability` | merchant | Sold out / back in stock (any staff role) |
| `POST /v1/merchant/menu/reorder`, `POST /v1/merchant/menu/images` | manager | Reordering and photo upload |

Errors come back as ProblemDetails with a stable `code`, e.g. `qr_invalid`, `venue_unavailable`, `invalid_credentials` or `location_exists`.

## Ground rules (from the blueprint)

- Money is `long` minor units plus an ISO currency code. Never use floating point. See `Money`.
- Every tenant-owned entity implements `ITenantOwned` and gets an automatic `TenantId` query filter.
- QR tokens are opaque, rotatable and never stored. Only their SHA-256 hash is saved.
- Only the verified PSP webhook confirms a payment. Every write carries an idempotency key.
- If the payment succeeds but posting to the POS fails, the state becomes `POSTING_PENDING` and the post is retried. The guest is never charged twice.
- Never store card numbers or CVV. Use the PSP's hosted fields.

## Roadmap

| Sprint | Scope | Epics |
|---|---|---|
| 0 | Scaffold, DB, CI ✅ | — |
| 1 | Tenant / venue / table / QR token, guest session, merchant sign-in ✅ | E1, E2 |
| 2 | Menu admin + guest e-menu, modifiers, schedules, photos ✅ | E4, E31 |
| 3 | Cart, server-validated order, staff alerts (SignalR) | E6, E10 |
| 4 | Bill, payment intent, PSP webhook, tip, promo | E5, E8, E30 |
| 5 | Split ledger: equal and by item, with concurrency locks (adds Redis) | E7 |
| 6 | Reviews and feedback | E27 |
| 7 | Merchant overview, tables, transactions, refunds | E11, E12 |
| 8 | Payment links | E16 |
| 9 | Hardening, Foodics connector, WhatsApp, email reports | E9, E15, E28 |
