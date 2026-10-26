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

In Development, the API creates the `DineFlow` database, applies migrations and seeds a demo tenant on first start. The demo is **Demo Bistro, Abdoun, Amman**, in JOD (3 decimals) and Arabic by default: tables 1–8 in Main hall, 9–12 on the Terrace, a counter, an owner account, a 10% service charge with Jordan's 16% sales tax included in prices, and two bilingual menus. "All day" includes subcategories, modifiers, allergens and one sold-out item. "Breakfast" is served 07:00–11:30 only. The owner's sign-in details are under `Seed:Owner` in `DineFlow/DineFlow.Api/appsettings.Development.json`.

The API listens on http://localhost:5266. Swagger is at `/swagger`, and readiness (SQL Server) is at `/health/ready`.

```bash
npm --prefix web ci
```

```bash
npm --prefix web start
```

That serves both apps. Sign in to the merchant portal at http://localhost:4300, open **Tables**, and click **Open** on any table to see its guest landing page. From there, **View Menu** opens the e-menu. Add items, review the order and place it. The order appears instantly under **Orders** in the merchant portal, with a chime, a toast and a badge. Moving it along (Accept → Preparing → Ready → Served) updates the guest's tracker live. Manage menus, categories, items, modifiers and photos under **Menu**, and service charge, VAT and tips under **Settings**. The Angular dev servers proxy `/v1`, `/health`, `/hubs`, `/media` and `/sandbox` to the API, so both apps call relative URLs.

### Paying the bill

On the guest side, **Pay Bill** opens checkout for everything the table has ordered. The guest picks a tip (the venue's "Most common" one starts selected), can enter a promo code and choose a payment method, then pays on the provider's page. The demo seeds two codes: **WELCOME10** (10% off, up to JOD 5.000) and **NEXT15**, which is offered on the payment confirmation for the next visit. Manage codes under **Promotions** in the merchant portal. When the payment goes through, the guest sees the confirmation and can e-mail a receipt. Staff get a "Payment received" alert with the tip and what's still owed. Once nothing is left, the table's bill closes, and the next order opens a new one.

Until a real PSP is connected, payments go through the built-in **sandbox provider**. It's a hosted test page at `/sandbox/pay/…` that takes these test cards (any future expiry, any CVC):

| Card | Result |
|---|---|
| `4242 4242 4242 4242` | Visa, succeeds |
| `5555 5555 5555 4444` | Mastercard, succeeds |
| `4000 0000 0000 0002` | Declined |
| `4000 0000 0000 9995` | Insufficient funds |

Apple Pay, Google Pay and STC Pay show Approve and Decline buttons. The sandbox reports each result through a signed webhook, verified the same way a real provider's would be. No real money moves. Outside Development the API refuses to start with the sandbox, unless `Payments:Sandbox:AllowOutsideDevelopment` is set, e.g. for a demo server.

#### Real payments: Mastercard Gateway

DineFlow's venues are in Jordan. Jordanian acquirers (MEPS, Network International Jordan and the banks) give merchants a **Mastercard Gateway (MPGS)** account, so that's the real provider. Set `Payments:Provider` to `mastercard`.

How a payment works:

1. **INITIATE_CHECKOUT** opens a Hosted Checkout session. It carries the amount in the currency's decimals (JOD `12.500`), DineFlow's return and cancel URLs, and the webhook URL.
2. The guest goes to DineFlow's small launcher page, `/v1/public/checkout/mastercard`. It loads the gateway's `checkout.js` and shows the gateway's own payment page. Card details never touch DineFlow.
3. The result arrives as a webhook to `/v1/webhooks/payments/mastercard`. It's accepted only with the merchant's secret in `X-Notification-Secret`, compared in constant time.
4. Mastercard says its webhooks aren't guaranteed to arrive, so DineFlow also asks the gateway (**Retrieve Order**) every 15 seconds about any payment or refund still waiting. A result that arrives both ways is counted once.
5. Refunds are **REFUND** transactions on the gateway order. Their result comes back the same two ways.

Settings, all from your acquirer or Merchant Administration:

| Setting | What |
|---|---|
| `Payments:Mastercard:GatewayUrl` | The gateway host your acquirer gives you (default: Mastercard's test gateway) |
| `Payments:Mastercard:MerchantId` | Gateway merchant id |
| `Payments:Mastercard:ApiPassword` | **Secret.** Admin → Integration Settings |
| `Payments:Mastercard:WebhookSecret` | **Secret.** Admin → Webhook Notifications (also turn webhooks on there) |
| `Payments:Mastercard:NotificationUrl` | DineFlow's public **https** webhook URL |
| `Payments:Mastercard:ApiVersion` | REST API version of your merchant profile (default 100) |
| `Payments:Mastercard:Methods` | What guests can pick before the gateway page (default Card, ApplePay; Apple Pay must be enabled by your acquirer) |

Set the two secrets with user-secrets or a key vault, never in `appsettings.json`. The provider is built from Mastercard's public API documentation and tested against a fake gateway. Confirm it with your acquirer's test credentials before going live: the API version, Apple Pay and 3-D Secure settings vary by merchant profile.

Receipts aren't sent in Development. Each one is written as an `.eml` file to `DineFlow/DineFlow.Api/App_Data/outbox` (git-ignored), and any mail app can open it.

### Splitting the bill

**Split the bill** on the checkout screen lets each guest pay part of the bill. All amounts include service charge and VAT:

- **Equally:** set how many people are at the table and how many you're paying for. The head count is saved on the bill, so everyone at the table sees the same split. Shares round down, and whoever pays the last share pays exactly what's left.
- **By item:** pick the items you had. Each portion of an order line is its own item (two Cokes are two items), priced with its share of service charge and VAT. Tapping an item holds it for you for 10 minutes. Other guests see "Being selected by another guest" and can't pay for it.
- **Custom:** any amount up to what's left.
- **Full:** everything that's left, apart from anything other guests have selected or are paying for.

**Remove split** goes back to paying the full remaining bill. Equal and item splits don't mix on one bill. While anyone holds or pays items, the bill can't be split equally. Once anyone pays an amount, nobody can pick new items, though items already held can still be paid. Staff can't reject an order whose items have been paid for or are being paid for. They get a "refund it instead" error and can refund the payment under **Transactions**.

Guests can act at the same time without paying the same money twice. Each bill has a revision number that the database checks on every save. Starting a payment, holding items and changing the split all bump it. When two guests act at once, one save fails and that request is worked out again with the other guest's change in view. A unique index also allows only one claim per item. This needs no Redis. The tests reproduce both races, and randomized split sequences check that paid + left always equals the total.

### Reviews

After a successful payment, the confirmation asks the guest to rate the visit from 1 to 5 stars, with an optional comment. They can skip it, and the payment and receipt are unaffected.

- **4–5 stars:** the guest is invited to post a public review on the venue's page (Google, Tripadvisor or Foursquare). DineFlow records whether they opened it.
- **1–3 stars:** the feedback stays private. The guest sees a form, with a heading the venue can change, where they can say what went wrong and leave a name, e-mail and phone number. Contact details are kept only if the guest ticks the consent box.

Set this up under **Settings → Reviews**: turn ratings on or off, choose the platform, add your review page link (https only) and set the feedback heading. The demo venue starts with ratings on and no public link.

**Reviews** in the merchant portal shows feedback for a chosen period:

- Totals, the average, ratings per star, the share of payments that left a rating and the share guided to public review, each compared with the previous period.
- A list you can split into positive and constructive, filter by stars or "only with comment", sort and page through, then export as CSV.
- Staff ratings, credited to whoever marked the table's order served, or accepted it if nobody marked it served.

Each review opens its visit: the payment and the items ordered. Owners and managers can reveal the guest's contact details, and every view is written to the audit log. The CSV export never includes contact details. **Mark as followed up** records who got back to the guest and when. Staff see a "New review" alert as ratings come in.

### Overview, tables and transactions

Signing in opens **Overview**, which updates live as orders, payments, refunds and reviews come in:

- Revenue, tips, orders and refunds for today, the last 7 days, this month or this quarter, in the venue's time zone. Revenue is what guests paid towards their bills, before refunds and without tips. Refunds are shown on their own.
- The last 7 days against the 7 days before, for revenue and orders.
- A revenue or tips chart by hour, day, week or month. Hover or use the arrow keys for each value, or switch to the table view.
- **Needs attention:** failed payments today, orders waiting more than 5 minutes, tables with an open balance, unhappy reviews from the last 7 days that nobody has followed up, and refunds still waiting for the provider.

**Tables → Live status** shows every table's state, what's been paid against its bill, and its orders when you open it:

| State | When |
|---|---|
| Available | No open bill |
| Ordering | A guest scanned the QR in the last hour, with nothing ordered yet |
| Unpaid / Partially paid | An open bill with nothing paid / part paid |
| Fully paid | The bill closed in the last 2 hours |
| Needs attention | A payment failed in the last 30 minutes and money is still owed |

**Tables → Tables & QR codes** is the floor plan and QR management from Sprint 1.

**Transactions** lists every payment for a period, with totals for paid, tips, refunded and net. Filter by status, or search by reference (`DF-…`), table number or amount. Each payment opens its breakdown, items, refunds and a complete timeline: scan, orders, payment, the provider's confirmation, ledger entries, refunds and the review.

Owners and managers can refund all or part of a payment, with a reason that goes in the audit log. Refunds can never add up to more than was charged, tip included. Retrying a request returns the same refund, and two managers refunding at once can't both succeed. A refund stays pending until the provider's signed webhook confirms it, and then it's written to the ledger as its own `Refund` entry. The bill itself doesn't change. The sandbox confirms refunds about 2 seconds later, and staff see a "Refund confirmed" alert.

Not built yet: QR groups, posting refunds to a POS, and the diner fee.

### Payment links

**Payment Links** lets staff ask a customer to pay ahead, e.g. a booking deposit or an order to collect. Any staff member can create one with:

- an amount, including taxes and charges, and an optional reference such as "Booking 42";
- how long it stays active, from 1 hour to 90 days, with the exact expiry shown as you type;
- whether several people can split it, and whether to ask for a tip, using the venue's tip choices;
- an optional customer e-mail address, which sends the link straight away.

Each link gets an identifier like `PL-1A2B3C4D` and a URL ending in a signed token that can't be guessed. Like QR tokens, only the token's hash is stored. Share it by QR code (shown and downloadable), WhatsApp, e-mail, copy or the device's share sheet.

The customer opens `/l/{token}` in the guest app. No table or account is needed. The page shows:

- the venue's branding, the link identifier and reference;
- the amount, with anything already paid and what's left;
- a tip and split options if the link has them;
- the payment methods and the Terms notice.

A split link can be paid in full, by a custom amount, or in equal shares of the link's amount. Shares round up and the last one pays exactly what's left. Payments go through the same provider and signed webhook as payments at the table. While someone is on the provider's page, their amount is held for 15 minutes, so two people can't pay the same money. Once the amount is fully paid the link closes by itself. An expired, paid or deactivated link shows a clear message instead of the checkout.

The list has counts per status (active, paid, expired, deactivated), search by identifier, reference, e-mail or amount, and CSV export. Each link opens its payments and a timeline. Deactivating a link stops new payments, and the provider page also refuses a payment that's already under way. Link payments appear in **Transactions** as "Link PL-…", searchable by the identifier. They can be refunded like any other payment, and they count in the Overview's revenue and tips. A refund doesn't reopen the link. Staff see a "Payment link paid" alert as money comes in. Promotion codes and reviews aren't offered on links.

### WhatsApp alerts, e-mail reports and the audit log

**Settings → Notifications** turns on WhatsApp payment alerts and lists the numbers that get them: up to 10, in international format (`+962 79 123 4567` is stored as `+962791234567`). Every successful payment, at a table or through a payment link, sends each number a short message. It's short enough to read on a smartwatch:

```
Demo Bistro · Table 7
AED 55.00 (tip AED 5.00) paid
Bill fully paid
Orders #1012, #1013 · DF-1A2B3C4D · 12:04
```

A part payment shows what's left instead, e.g. "AED 60.00 left to pay". **Send a test message** checks the numbers and the channel.

**Reports** schedules e-mailed reports:

- **Daily** reports go out at the chosen hour (venue time) and cover the day before.
- **Weekly** reports go out on Mondays and cover Monday to Sunday.
- **Monthly** reports go out on the 1st and cover the month before.

Each report has revenue, tips, refunds, net, orders, successful payments, the average payment, payment failures, money paid through payment links, ratings and the average rating. Every figure is compared with the period before, and the five best-selling items are listed. The page previews the latest report and has **Send now**.

Messages aren't sent while the request or webhook is being handled. They're written to an outbox table, and a background worker sends them every few seconds:

- Each message is sent once per payment or report period, however often the event is handled.
- A failure is retried after 30 seconds, 2 minutes, 10 minutes and 30 minutes. After 5 attempts the message stays **Failed**, with the provider's reason.
- Both pages show a delivery log. Managers can resend a failed message.
- If two API instances run, a version column on each message and schedule stops them sending the same one twice.

WhatsApp goes through the **WhatsApp Cloud API** (Meta). Development needs no account: `WhatsApp:Provider` is `outbox`, and each message is written as JSON to `DineFlow/DineFlow.Api/App_Data/whatsapp` (git-ignored). Report e-mails go to the e-mail outbox like receipts. To send for real:

1. Create a template in WhatsApp Manager with four body parameters, e.g. `{{1}}: {{2}} paid. {{3}}. {{4}}`. Name it `payment_alert`, or set `WhatsApp:MetaApi:TemplateName`.
2. Set `WhatsApp:Provider` to `meta`.
3. Set `WhatsApp:MetaApi:PhoneNumberId` and `WhatsApp:MetaApi:AccessToken` with user-secrets or a key vault.

**Settings → Activity** (owners and managers) is the audit log. It shows refunds, viewing a guest's contact details, payment links, alert numbers, reports and resent messages: who did each, when, and to what. Entries can't be changed or deleted.

**Operations:**

- Every request gets a correlation id. The API uses the caller's `X-Correlation-Id` if it's sensible, otherwise a new one. It's echoed in the response, included as `correlationId` in every error body, and attached to every log line for the request.
- API responses carry `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, a locked-down `Permissions-Policy` and a CSP of `default-src 'none'`. HSTS is on outside Development.
- `/health/ready` checks the database and the outbox. It's Degraded if the worker stops or messages are more than ten minutes overdue.
- `Features:PaymentLinks`, `Features:WhatsAppAlerts` and `Features:EmailReports` (default `true`) switch features off without a release. Their endpoints answer `404 feature_disabled`, the merchant app hides them, and their background work stops.

### POS connection

**Settings → POS** connects the venue's POS:

1. Pick the POS, add its branch id and access token, then **Check connection**.
2. Map each table to its table id in the POS.
3. If the POS only takes its own items (Foodics does), map menu items to its product ids.

Every connector implements the blueprint's adapter contract (section 6): health check, open checks, check details, create order, post payment and close check. Each connector declares what its POS can do (`PaymentPosting`, `AutoClose`, `TipPosting`, `OrderCreate`, …), and DineFlow only asks for those.

While the connection is active, the order of events is:

1. When staff **accept** an order, it's sent to the POS. The bill's first order opens a POS check on the mapped table; later orders are added to it.
2. When the provider confirms a **payment** at a table, it's posted to that check as a tender, with the tip. Payment links aren't on a POS check, so they aren't posted.
3. Once the bill is paid in full, the POS check is **closed**, if the venue wants that and the POS supports it.

Nothing here holds up the guest. Each step is queued and a background worker sends it, oldest first for each bill, so a payment never reaches the POS before its order.

When things go wrong:

- **The POS is down or slow:** the step waits (`POSTING_PENDING`) and is retried after 30 seconds, then 1, 2, 5 and 10 minutes, then every 15 minutes until the POS is back. The guest isn't charged again.
- **An answer is lost after the POS may have applied the step:** the next attempt asks the POS whether the tender is already on the check before posting again, so a payment is posted exactly once. Orders carry DineFlow's order id for the same reason.
- **The POS refuses a step** (e.g. a table that isn't mapped): it's marked **Refused** with the reason, and later steps for that bill wait. Fix the cause and press **Retry**.

Anything waiting more than 15 minutes, or refused, shows in the Overview's **Needs attention** and on the POS tab. Transactions show each payment's POS state ("In the POS", "Waiting for the POS", "POS refused it") and a timeline entry.

Access tokens are encrypted with ASP.NET Core Data Protection before they're stored, and are never returned. The keys live in `DineFlow/DineFlow.Api/App_Data/keys` (git-ignored); production should keep them in shared, protected storage.

Two connectors are built in:

- **Simulated POS** keeps checks in memory and needs no account. The POS tab shows its checks and can switch it to **Offline**, **Time out after applying** (once) or **Refuse everything**, to try the failure cases from blueprint 13.1. These controls exist in Development only.
- **Foodics** uses Foodics API v5. Set `Pos:Foodics:BaseUrl` (sandbox by default), and set `Pos:Foodics:PaymentMethodId` to a payment method created for DineFlow in Foodics. It's built from Foodics' public documentation and tested against a fake Foodics. The calls that add products and payments to an existing order still need confirming with a Foodics sandbox account.

To apply migrations without starting the API:

```bash
dotnet ef database update -p DineFlow/DineFlow.Infrastructure -s DineFlow/DineFlow.Api
```

### Settlements and reconciliation

**Reports → Reconciliation** answers whether every payment reached the bank. For each business day (in the venue's time zone) DineFlow compares three things:

1. **The ledger:** succeeded payments and refunds in DineFlow.
2. **The acquirer's settlements:** the payout batches it reports, line by line.
3. **The POS:** whether each table payment was posted to its check.

Payments are matched to settlement lines by the gateway's order id or by DineFlow's payment reference. Whatever doesn't add up lands in a queue of **differences**:

| Difference | Meaning |
|---|---|
| Not settled | A payment has no settlement line 3 days after the day it was taken. Before that it counts as **awaiting settlement**, because acquirers pay a day or two later. |
| Unknown to DineFlow | A settlement line matches no payment. |
| Amount differs | The settled amount isn't what the guest paid. |
| Refund differs | A refund is missing from the settlement, or settled for a different amount. |
| Settled twice | Two lines settle the same payment. |
| Not in POS | The POS refused the payment, or still hasn't taken it an hour later. |

A day is reconciled again every hour for the last week, whenever a settlement is imported, and on **Reconcile yesterday now**. A difference that goes away (the settlement arrived, the POS caught up) is cleared by itself. Others are **resolved** by a manager with a note, e.g. the claim raised with the acquirer. Resolving is audit-logged, and a resolved difference stays resolved.

**Reports → Settlements** imports the acquirer's settlement report as CSV (up to 5 MB). It needs one row per transaction with a header row. Commas or semicolons both work, and column names are matched loosely:

- batch or payout id, and payout date
- transaction date and type (sale, refund or fee)
- the gateway's order id, or DineFlow's payment reference
- amount in major units (e.g. `12.500` for JOD), and optionally fee, refund id and currency

A batch that's already been imported is refused, so importing the same file twice changes nothing. In Development, **Simulate a payout** settles every unsettled sandbox payment as an acquirer would, with a 2.25% + 0.100 fee, so you can see a clean day.

**Ledger export** downloads every money movement for a date range as CSV, for the accountant. Each row has the payment, the table or link, and the settlement batch, payout date and fee once settled.

### From Visual Studio

`DineFlow.slnx` includes the Angular workspace as `DineFlow.Web` (`web/DineFlow.Web.esproj`). Pick the **API + Web** launch profile in the toolbar's startup dropdown and press F5. That starts the API and runs `npm start`, which serves both Angular apps.

### Keys and secrets

`appsettings.Development.json` holds **development-only** values: the JWT signing key, the QR token key, the sandbox payment key and the demo owner's password. Outside Development, `Jwt:SigningKey`, `Qr:TokenKey` and (with the sandbox) `Payments:Sandbox:SecretKey` are required (base64, at least 32 bytes), and the API refuses to start without them. Set them with environment variables, user-secrets or a key vault. Changing `Qr:TokenKey` invalidates every printed QR code. With `Payments:Provider` set to `mastercard`, `Payments:Mastercard:ApiPassword` and `WebhookSecret` are required secrets. With `WhatsApp:Provider` set to `meta`, `WhatsApp:MetaApi:AccessToken` is a secret too, and the API refuses to start without it and `PhoneNumberId`.

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
| `POST /v1/public/sessions/{id}/reviews` | guest token | Rate a paid visit (1–5 stars, one per payment); returns the public review link for 4–5 stars |
| `PUT …/reviews/{reviewId}/feedback`, `POST …/reviews/{reviewId}/public-opened` | guest token | Private feedback with consented contact details; record that the public page was opened |
| `GET /v1/merchant/reviews?venueId=&from=&to=&rating=&kind=&withComment=&sort=&page=&pageSize=` | merchant | Summary (with previous period) and the filtered page |
| `GET /v1/merchant/reviews/{id}`, `POST …/{id}/follow-up` | merchant | A review with its payment and items; mark it followed up |
| `GET /v1/merchant/reviews/{id}/contact` | manager | The guest's contact details (audit-logged) |
| `GET /v1/merchant/reviews/staff-summary`, `GET /v1/merchant/reviews/export` | merchant | Ratings per staff member; CSV export |
| `GET /v1/merchant/venues/{id}/overview?period=Today\|Week\|Month\|Quarter&granularity=` | merchant | Overview KPIs, week comparison, trend and what needs attention |
| `GET /v1/merchant/venues/{id}/table-status` | merchant | Every table's live state, bill and orders |
| `GET /v1/merchant/transactions?venueId=&from=&to=&status=&search=&page=&pageSize=` | merchant | Payments for a period, with totals |
| `GET /v1/merchant/transactions/{paymentId}` | merchant | A payment with its items, refunds and timeline |
| `POST /v1/merchant/transactions/{paymentId}/refunds` | manager | Refund all or part of a payment (idempotent per `clientRequestId`, reason required) |
| `GET /v1/merchant/payment-links?venueId=&status=&search=&page=&pageSize=`, `GET …/export` | merchant | Links with counts per status; CSV export |
| `POST /v1/merchant/payment-links` | merchant | Create a link (idempotent per `clientRequestId`); e-mails the customer when an address is given |
| `GET /v1/merchant/payment-links/{id}`, `POST …/{id}/cancel`, `POST …/{id}/notify` | merchant | A link with its payments and timeline; deactivate it; e-mail it |
| `GET /v1/public/payment-links/{token}` | anyone with the link (rate limited) | The link as the customer sees it |
| `POST /v1/public/payment-links/{token}/payments`, `GET …/payments/{id}`, `POST …/payments/{id}/cancel`, `POST …/payments/{id}/receipt` | anyone with the link | Pay what's left, equal shares or an amount (idempotent per `clientRequestId`); status; cancel; e-mail the receipt |
| `GET`/`PUT /v1/merchant/venues/{id}/alerts`, `POST …/alerts/contacts`, `DELETE /v1/merchant/alerts/contacts/{id}`, `POST …/alerts/test` | merchant / manager | WhatsApp payment alerts: on or off, numbers, test message |
| `GET /v1/merchant/notifications?venueId=&channel=&status=`, `POST …/{id}/resend` | merchant / manager | Delivery log for WhatsApp and e-mail; resend a failed message |
| `GET/POST /v1/merchant/report-schedules`, `PUT`/`DELETE …/{id}`, `POST …/{id}/send`, `GET …/preview?venueId=&frequency=` | merchant / manager | Scheduled e-mail reports, send now, preview |
| `GET /v1/merchant/audit?area=&from=&to=&page=` | manager | The audit log |
| `GET /v1/merchant/features` | merchant | Which switchable features are on |
| `GET /health/live`, `GET /health/ready` | anyone | Liveness; readiness with database and outbox checks |
| `GET`/`PUT /v1/merchant/venues/{id}/pos`, `POST …/pos/check`, `PUT …/pos/mappings` | merchant / manager | POS connection (token write-only), health check, table and product mapping |
| `GET /v1/merchant/pos/commands?venueId=&status=`, `POST …/{id}/retry` | merchant / manager | What's been sent to the POS, what's waiting or refused; retry |
| `GET /v1/merchant/settlements?venueId=`, `GET …/settlements/{id}` | manager | Settlement batches with totals; a batch with its lines and the payments they matched |
| `POST /v1/merchant/settlements/import?venueId=` | manager | Import an acquirer settlement CSV (multipart `file`); reconciles the days it covers |
| `POST /v1/merchant/settlements/sandbox-payout?venueId=` | manager (Development only) | Settle every unsettled sandbox payment |
| `GET /v1/merchant/reconciliation?venueId=`, `POST …/reconciliation/run` | manager | Reconciled days; reconcile a day now (yesterday by default) |
| `GET /v1/merchant/reconciliation/discrepancies?venueId=&status=&page=`, `POST …/discrepancies/{id}/resolve` | manager | The queue of differences; resolve one with a note (audit-logged) |
| `GET /v1/merchant/ledger/export?venueId=&from=&to=` | manager | Ledger CSV with settlement batch and fee |

### Real-time

SignalR hubs push order and payment events. Staff screens connect to `/hubs/merchant` (merchant JWT) and receive `orderPlaced`, `orderChanged`, `paymentReceived`, `reviewSubmitted`, `refundUpdated` and `paymentLinkPaid` for their tenant. Guest phones connect to `/hubs/guest` (guest session token) and receive `orderChanged` for their own session, plus `billChanged` whenever anything at their table changes what it owes: an order, a payment, a held item or the split. Browsers send the token as `?access_token=` on these paths. Delivery is best effort, and both apps reload their lists after reconnecting.

Errors come back as ProblemDetails with a stable `code`, e.g. `qr_invalid`, `venue_unavailable`, `invalid_credentials`, `location_exists`, `bill_changed`, `payment_in_progress`, `promo_expired`, `refund_not_allowed`, `link_not_found`, `link_expired`, `link_paid`, `invalid_contact`, `settlement_exists`, `invalid_settlement_file`, `nothing_to_settle` or `feature_disabled`, plus the request's `correlationId`.

## Ground rules (from the blueprint)

- Money is `long` minor units plus an ISO currency code. Never use floating point. See `Money`.
- Every tenant-owned entity implements `ITenantOwned` and gets an automatic `TenantId` query filter.
- QR tokens are opaque, rotatable and never stored. Only their SHA-256 hash is saved.
- The server prices every cart and order. Guests confirm the server's total, and a changed total is never ordered silently.
- Only the verified PSP webhook confirms a payment. Every write carries an idempotency key; orders are unique per session + `clientRequestId`.
- If the payment succeeds but posting to the POS fails, the state becomes `POSTING_PENDING` and the post is retried. The guest is never charged twice. See [POS connection](#pos-connection).
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
| 6 | Reviews: post-payment rating, public-review routing, private feedback with consent, reviews dashboard, staff ratings ✅ | E27 |
| 7 | Merchant overview, live table status, transactions with timeline, refunds ✅ | E11, E12 |
| 8 | Payment links: create, share (QR, WhatsApp, e-mail), split and tip options, expiry, status tracking, export ✅ | E16 |
| 9a | WhatsApp payment alerts, scheduled e-mail reports, notification outbox with retries, audit log, correlation ids, security headers, health checks, feature flags ✅ | E15, E28 |
| 9b | POS connector framework and capability matrix, orders and payment posting with POSTING_PENDING retries and exactly-once tenders, auto-close, simulated POS, Foodics connector (to verify with a sandbox account) ✅ | E3, E9 |
| 10 | Real payments for Jordan: Mastercard Gateway (MPGS) Hosted Checkout, verified webhooks plus status checks, refunds, JOD defaults (to verify with acquirer test credentials) ✅ | E8 |
| 11 | Settlements and reconciliation: settlement CSV import, daily ledger vs settlement vs POS reconciliation, differences queue with resolve and audit, sandbox payout, ledger export ✅ | E12, E14 |
