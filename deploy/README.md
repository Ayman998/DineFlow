# Running the Amman pilot

DineFlow runs on one Linux server with Docker: SQL Server, the API, and Caddy, which serves both apps over HTTPS and forwards API calls. The same images run on Azure, AWS or any container host later; only this folder's compose file is specific to a single server.

| Container | What it is | Data |
|---|---|---|
| `db` | SQL Server 2022 (Express edition by default) | `db-data` volume |
| `api` | The .NET API, port 8080 inside the network only | `api-data` volume: photos, Data Protection keys |
| `web` | Caddy: the guest app on `GUEST_DOMAIN`, the merchant portal on `MERCHANT_DOMAIN`, with automatic HTTPS | `caddy-data` volume: certificates |

## 1. Server

- A Linux VPS with 2 vCPU, 4 GB RAM and 40 GB disk is enough for a pilot. SQL Server needs at least 2 GB of RAM. A data centre close to Jordan keeps the guest app fast, for example a Jordanian host or a Gulf region.
- Install Docker Engine with the Compose plugin.
- Open ports 80 and 443 only. Keep SSH on a key, not a password.
- Create two DNS A records pointing at the server, for example `dine.example.jo` for guests (printed on the QR codes) and `portal.example.jo` for staff. Caddy can only get certificates once DNS resolves.

## 2. First start

On the server, copy this `deploy` folder (for example to `/opt/dineflow/deploy`), then:

```bash
cp .env.example .env
nano .env        # domains, sa password, keys (openssl rand -base64 48), Mastercard, SMTP
```

The images are private on GitHub Container Registry. Sign in once with a GitHub token that has `read:packages`:

```bash
echo "$GITHUB_TOKEN" | docker login ghcr.io -u <github-user> --password-stdin
docker compose pull
docker compose up -d db
docker compose run --rm api migrate
docker compose up -d
```

To build on the server instead of pulling, clone the whole repository and run `docker compose build` in `deploy`.

Check that it's up:

```bash
docker compose ps
curl -s https://portal.example.jo/health/ready
docker compose logs api --tail 50
```

The API refuses to start if a setting would be unsafe with real guests. Examples are the sandbox payment provider, e-mail written to files, a QR address that isn't https, or demo seeding left on. The log says which setting to fix. Settings that are allowed but worth a second look are logged as `Configuration:` warnings at startup, such as still being on Mastercard's test gateway.

## 3. Add the venue

There's no public sign-up during the pilot. Create each restaurant from the server:

```bash
docker compose run --rm api provision \
  --business "Hashem Kitchen" --venue "Abdoun, Amman" --venue-ar "عبدون، عمّان" \
  --owner-email owner@example.jo --owner-name "Lina Haddad" --tables 20
```

The defaults suit Amman: JOD, Asia/Amman, Arabic first, 16% sales tax, 10% service charge and prices that include tax. Use `--tax`, `--service` and `--prices-exclude-tax` to change them. The owner can also change them later under Settings. The command prints a **temporary password once**. Hand it over privately.

The owner then:

1. Signs in at `https://portal.example.jo` and replaces the password under **Settings → Team → Your password**.
2. Adds managers and waiters under **Settings → Team**. Each person gets a temporary password shown once.
3. Builds the menu, prints the QR codes from **Tables**, and works through **Go-live** in the portal.

**Go-live** works out each check from what has actually happened, so nobody ticks anything by hand. It shows what's left before guests pay with real cards.

## 4. Mastercard Gateway test checklist

The acquirer (for example MEPS, Network International or your bank) gives you test credentials first: a `TEST…` merchant id on `https://test-gateway.mastercard.com` or the bank's own test host. The steps below double as the acquirer's certification script. Do them on the real server, with its real domains.

**In Merchant Administration (the gateway's portal):**

1. **Admin → Integration Settings:** generate an API password. Put the merchant id and password in `.env` as `Payments__Mastercard__MerchantId` and `__ApiPassword`.
2. **Admin → Webhook Notifications:** set the URL to `https://<GUEST_DOMAIN>/v1/webhooks/payments/mastercard`. Copy the notification secret into `Payments__Mastercard__WebhookSecret`, and the same URL into `Payments__Mastercard__NotificationUrl`.
3. Ask the acquirer to enable the payment methods you'll offer (cards, Apple Pay) and 3-D Secure on the test profile.
4. Run `docker compose up -d api` to restart with the new settings.

**In DineFlow (each step ticks a Go-live check):**

| # | Do this | Go-live check |
|---|---|---|
| 1 | Scan a printed QR code with a phone | A printed QR code was scanned |
| 2 | Order, then pay in full with a test card that passes 3-D Secure | Test payment succeeded |
| 3 | Check the payment shows as paid without refreshing. That's the webhook, not the status poll | Gateway's notification received |
| 4 | Pay a second bill with a test card that's declined, and check the guest can try again | (manual) |
| 5 | Split a bill and pay one share; pay a payment link | (manual) |
| 6 | Ask for the e-mail receipt on the payment page | A receipt e-mail was delivered |
| 7 | Refund part of a payment, then the rest, from **Transactions** | A refund went through |
| 8 | The next day, download the settlement report from Merchant Administration and import it under **Reports → Settlements** | Settlement report imported |
| 9 | Check **Reports → Reconciliation** shows the day balanced, with no differences | (manual) |

Use the test card numbers and 3-D Secure scenarios from the gateway's own testing documentation. They differ between gateway hosts.

**Going live.** When the acquirer signs off, put the live gateway URL, live merchant id, API password and webhook secret in `.env`. Set the live webhook in the live Merchant Administration too. Then run `docker compose up -d api`. The *Switched to the live gateway* check turns green, and the startup warning about the test gateway goes away. Make one small real payment and refund it before opening to guests.

## 5. Updates

Releases are built by the **Release images** workflow when a tag such as `v1.0.0` is pushed. On the server:

```bash
docker compose pull
docker compose run --rm api migrate
docker compose up -d
```

Set `TAG=1.0.0` in `.env` to pin a release. Set it back to the previous tag and run `up -d` to roll back. Run a backup first, because migrations don't roll back by themselves.

## 6. Backups

`./backup.sh` writes a SQL Server backup and an archive of the `api-data` volume into `./backups`, and keeps 14 days. Schedule it nightly:

```bash
chmod +x backup.sh
crontab -e   # 0 3 * * * cd /opt/dineflow/deploy && ./backup.sh >> backups/backup.log 2>&1
```

Copy `./backups` off the server as well, for example with rclone or restic to object storage. Keep `.env` somewhere safe too: without `Qr__TokenKey` the printed QR codes stop working, and without the Data Protection keys in `api-data`, stored POS tokens can't be read.

To restore a database backup:

```bash
docker compose stop api
docker compose cp backups/DineFlow-<stamp>.bak db:/var/opt/mssql/restore.bak
docker compose exec db sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -Q "RESTORE DATABASE [DineFlow] FROM DISK = N'"'"'/var/opt/mssql/restore.bak'"'"' WITH REPLACE"'
docker compose start api
```

## 7. When something's wrong

| Symptom | Look at |
|---|---|
| The site doesn't load, or shows a certificate error | `docker compose logs web`: DNS not pointing here yet, or ports 80/443 closed |
| The API keeps restarting | `docker compose logs api`: the first lines name the setting to fix |
| Payments stay "processing" | The webhook URL or secret in Merchant Administration. The API also asks the gateway every minute, so they complete anyway, just later |
| No receipt e-mails | `/health/ready` shows the notification outbox; `docker compose logs api` shows the SMTP error |
| A staff member left | **Settings → Team → Switch off**. They're signed out within a minute |

Every error the API returns carries a `correlationId`. Search the logs for it with `docker compose logs api | grep <id>`.
