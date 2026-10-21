# DineFlow

Scan, order and pay at the table. Guests scan a table QR, browse a branded e-menu, order, split the bill, tip, pay, and rate the visit. Restaurants see live orders, payments and reviews.

The spec is the Df3Pay Developer Blueprint v1.1, and the screen designs are the Df3Pay mockups.

## Stack

| Part | Tech |
|---|---|
| API | ASP.NET Core 8 Web API, modular monolith, Clean Architecture, JWT auth, SignalR |
| Data | EF Core 8 + SQL Server 2022 |
| Web | Angular 21 workspace: `guest` (mobile PWA, EN/AR + RTL) and `merchant` (dashboard), Tailwind CSS 4 |

## Layout

```
DineFlow/
  DineFlow.Api/             controllers, auth policies, rate limits, error responses
  DineFlow.Application/     use cases (guests, venues, tables, menus, orders, bills, payments, promotions)
  DineFlow.Domain/          entities: tenant, venue, location, QR, session, menu, order, check, payment, ledger, promo
  DineFlow.Infrastructure/  EF Core + SQL Server, migrations, QR/JWT/password services, sandbox PSP, e-mail, demo seed
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

In Development, the API creates the `DineFlow` database, applies migrations and seeds a demo tenant on first start. The demo is **Demo Bistro, Dubai Marina**: tables 1–8 in Main hall, 9–12 on the Terrace, a counter, an owner account, 10% service charge with 5% VAT included in prices, and two bilingual menus. "All day" includes subcategories, modifiers, allergens and one sold-out item. "Breakfast" is served 07:00–11:30 only. The owner's sign-in details are under `Seed:Owner` in `DineFlow/DineFlow.Api/appsettings.Development.json`.

The API listens on http://localhost:5266. Swagger is at `/swagger`, and readiness (SQL Server) is at `/health/ready`.

```bash
npm --prefix web ci
```

```bash
npm --prefix web start
```

That serves both apps. Sign in to the merchant portal at http://localhost:4300, open **Tables**, and click **Open** on any table to see its guest landing page. From there, **View Menu** opens the e-menu. Add items, review the order and place it. The order appears instantly under **Orders** in the merchant portal, with a chime, a toast and a badge. Moving it along (Accept → Preparing → Ready → Served) updates the guest's tracker live. Manage menus, categories, items, modifiers and photos under **Menu**, and service charge, VAT and tips under **Settings**. The Angular dev servers proxy `/v1`, `/health`, `/hubs`, `/media` and `/sandbox` to the API, so both apps call relative URLs.

### Paying the bill

On the guest side, **Pay Bill** opens checkout for everything the table has ordered. The guest picks a tip (the venue's "Most common" one starts selected), can enter a promo code and choose a payment method, then pays on the provider's page. The demo seeds two codes: **WELCOME10** (10% off, up to AED 50) and **NEXT15**, which is offered on the payment confirmation for the next visit. Manage codes under **Promotions** in the merchant portal. When the payment goes through, the guest sees the confirmation and can e-mail a receipt. Staff get a "Payment received" alert with the tip and what's still owed. Once nothing is left, the table's bill closes, and the next order opens a new one.

### Splitting the bill

**Split the bill** on the checkout screen lets each guest pay part of the bill. All amounts include service charge and VAT:

- **Equally:** set how many people are at the table and how many you're paying for. The head count is saved on the bill, so everyone at the table sees the same split. Shares round down, and whoever pays the last share pays exactly what's left.
- **By item:** pick the items you had. Each portion of an order line is its own item (two Cokes are two items), priced with its share of service charge and VAT. Tapping an item holds it for you for 10 minutes. Other guests see "Being selected by another guest" and can't pay for it.
- **Custom:** any amount up to what's left.
- **Full:** everything that's left, apart from anything other guests have selected or are paying for.

**Remove split** goes back to paying the full remaining bill. Equal and item splits don't mix on one bill. While anyone holds or pays items, the bill can't be split equally. Once anyone pays an amount, nobody can pick new items, though items already held can still be paid. Staff can't reject an order whose items have been paid for or are being paid for. They get a "refund it instead" error, because refunds arrive with Transactions.

Guests can act at the same time without paying the same money twice. Each bill has a revision number that the database checks on every save. Starting a payment, holding items and changing the split all bump it. When two guests act at once, one save fails and that request is worked out again with the other guest's change in view. A unique index also allows only one claim per item. This needs no Redis. The tests reproduce both races, and randomized split sequences check that paid + left always equals the total.

Until a real PSP is connected, payments go through the built-in **sandbox provider**. It's a hosted test page at `/sandbox/pay/…` that takes these test cards (any future expiry, any CVC):

| Card | Result |
|---|---|
| `4242 4242 4242 4242` | Visa, succeeds |
| `5555 5555 5555 4444` | Mastercard, succeeds |
| `4000 0000 0000 0002` | Declined |
| `4000 0000 0000 9995` | Insufficient funds |

Apple Pay, Google Pay and STC Pay show Approve and Decline buttons. The sandbox reports each result through a signed webhook, verified the same way a real provider's would be. No real money moves. Outside Development the API refuses to start with the sandbox, unless `Payments:Sandbox:AllowOutsideDevelopment` is set, e.g. for a demo server.

Receipts aren't sent in Development. Each one is written as an `.eml` file to `DineFlow/DineFlow.Api/App_Data/outbox` (git-ignored), and any mail app can open it.

To apply migrations without starting the API:

```bash
dotnet ef database update -p DineFlow/DineFlow.Infrastructure -s DineFlow/DineFlow.Api
```

### From Visual Studio

`DineFlow.slnx` includes the Angular workspace as `DineFlow.Web` (`web/DineFlow.Web.esproj`). Pick the **API + Web** launch profile in the toolbar's startup dropdown and press F5. That starts the API and runs `npm start`, which serves both Angular apps.

### Keys and secrets

`appsettings.Development.json` holds **development-only** values: the JWT signing key, the QR token key, the sandbox payment key and the demo owner's password. Outside Development, `Jwt:SigningKey`, `Qr:TokenKey` and (with the sandbox) `Payments:Sandbox:SecretKey` are required (base64, at least 32 bytes), and the API refuses to start without them. Set them with environment variables, user-secrets or a key vault. Changing `Qr:TokenKey` invalidates every printed QR code.

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
| `POST /v1/public/sessions/{id}/cart/validate` | guest token | Server prices for a cart, with service charge, VAT and any problems per line |
| `POST /v1/public/sessions/{id}/orders`, `GET …/orders` | guest token | Place an order (idempotent per `clientRequestId`); the table's orders |
| `GET /v1/merchant/venues/{id}/orders?filter=Active\|New\|Completed\|Rejected`, `GET …/orders/counts` | merchant | The order queue and badge counts |
| `POST /v1/merchant/orders/{id}/status` | merchant | Accept, reject (with reason), preparing, ready, served |
| `GET /v1/public/sessions/{id}/bill` | guest token | The table's bill: orders, items with who holds or paid them, paid, left, what others are paying, what this guest can pay, tip options, payment methods |
| `PUT /v1/public/sessions/{id}/split` | guest token | Set how many people split equally (`people`), or remove the split (`null`) |
| `POST /v1/public/sessions/{id}/allocations/reserve` | guest token | Hold exactly these items for this guest; reports any another guest already has |
| `POST /v1/public/sessions/{id}/promotions/validate` | guest token | Check a promo code against the bill |
| `POST /v1/public/payments`, `GET …/payments/{id}` | guest token | Pay the rest (`Full`), equal shares, held items or a custom amount (idempotent per `clientRequestId`); payment status |
| `POST /v1/public/payments/{id}/cancel`, `POST …/{id}/receipt` | guest token | The guest came back without paying; e-mail the receipt |
| `POST /v1/webhooks/payments/{provider}` | provider (signed) | Payment results. Only a verified webhook marks a payment paid. Repeats are ignored |
| `GET/POST /v1/merchant/promotions`, `PUT …/promotions/{id}` | merchant / manager | Promotion and voucher codes |

### Real-time

SignalR hubs push order and payment events. Staff screens connect to `/hubs/merchant` (merchant JWT) and receive `orderPlaced`, `orderChanged` and `paymentReceived` for their tenant. Guest phones connect to `/hubs/guest` (guest session token) and receive `orderChanged` for their own session, plus `billChanged` whenever anything at their table changes what it owes: an order, a payment, a held item or the split. Browsers send the token as `?access_token=` on these paths. Delivery is best effort, and both apps reload their lists after reconnecting.

Errors come back as ProblemDetails with a stable `code`, e.g. `qr_invalid`, `venue_unavailable`, `invalid_credentials`, `location_exists`, `bill_changed`, `payment_in_progress` or `promo_expired`.

## Ground rules (from the blueprint)

- Money is `long` minor units plus an ISO currency code. Never use floating point. See `Money`.
- Every tenant-owned entity implements `ITenantOwned` and gets an automatic `TenantId` query filter.
- QR tokens are opaque, rotatable and never stored. Only their SHA-256 hash is saved.
- The server prices every cart and order. Guests confirm the server's total, and a changed total is never ordered silently.
- Only the verified PSP webhook confirms a payment. Every write carries an idempotency key; orders are unique per session + `clientRequestId`.
- If the payment succeeds but posting to the POS fails, the state becomes `POSTING_PENDING` and the post is retried. The guest is never charged twice.
- Never store card numbers or CVV. Use the PSP's hosted fields.

## Roadmap

| Sprint | Scope | Epics |
|---|---|---|
| 0 | Scaffold, DB, CI ✅ | — |
| 1 | Tenant / venue / table / QR token, guest session, merchant sign-in ✅ | E1, E2 |
| 2 | Menu admin + guest e-menu, modifiers, schedules, photos ✅ | E4, E31 |
| 3 | Cart, server-validated order, standalone queue, staff alerts (SignalR) ✅ | E6, E10 |
| 4 | Bill, pay in full, sandbox PSP + signed webhook, ledger, tips, promo codes, e-mail receipt ✅ | E5, E8, E30 |
| 5 | Split the bill: equal, by item and custom, with item holds and concurrency protection ✅ | E7 |
| 6 | Reviews and feedback | E27 |
| 7 | Merchant overview, tables, transactions, refunds | E11, E12 |
| 8 | Payment links | E16 |
| 9 | Hardening, Foodics connector, WhatsApp, email reports | E9, E15, E28 |
