# Running helloZen on MonsterASP

On MonsterASP, helloZen is a single ASP.NET Core website. The API serves both apps itself: guests use `https://app.hello-zen.com` and staff use `https://app.hello-zen.com/portal/`. The database is a MonsterASP MSSQL database. The Docker setup one folder up isn't used here.

| What | Where |
|---|---|
| Website | `site103877`, under **Websites → app.hello-zen.com** (EU datacenter, Premium subdomain slot) |
| Settings | `HelloZen/HelloZen.Api/appsettings.MonsterAsp.json` (in git, nothing secret), plus environment variables in the control panel (the secrets) |
| Publishing | `HelloZen/HelloZen.Api/Properties/PublishProfiles/MonsterASP.pubxml` (Web Deploy from Visual Studio) |
| Kept on the server | `App_Data`: uploaded photos, Data Protection keys, logs. Publishing never touches it |

## 1. Point app.hello-zen.com at MonsterASP (once)

1. In Spaceship, open **Advanced DNS → hello-zen.com → Add record**. Add an **A** record with host `app` and value `136.243.185.115`.
2. When the website's **DNS status** card in MonsterASP turns green, open **HTTPS** and add the free Let's Encrypt certificate for `app.hello-zen.com`.

## 2. Database (once)

1. **Databases → Create**, and pick **MSSQL**. The plan includes two databases.
2. Copy its .NET connection string. It looks like `Server=…;Database=…;User Id=…;Password=…;TrustServerCertificate=True`. Add `;MultipleActiveResultSets=true` if it isn't there.
3. Create the tables from your PC (MonsterASP has no shell). From the repository root, paste the connection string when asked:

   ```powershell
   .\deploy\monsterasp\admin.ps1 migrate
   ```

## 3. Settings (once)

Open the website, then go to **Scripting → Advanced settings → Environment variables**. Add these:

| Name | Value |
|---|---|
| `ConnectionStrings__HelloZen` | The connection string from step 2 |
| `Jwt__SigningKey` | A new key (see below) |
| `Qr__TokenKey` | A new key. It signs every QR code: never change it once codes are printed, and keep a copy somewhere safe |
| `Payments__Mastercard__MerchantId`, `__ApiPassword`, `__WebhookSecret` | From the acquirer (see [the gateway checklist](../README.md#4-mastercard-gateway-test-checklist)) |
| `Email__Smtp__Host`, `__Port`, `__Security`, `__Username`, `__Password` | From the mail service that sends from `receipts@hello-zen.com`. MonsterASP's own **E-mails** service works, and the plan includes one |
| `Payments__TermsUrl` | Your published terms page |

Make each key in PowerShell:

```powershell
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b); [Convert]::ToBase64String($b)
```

**Until the Mastercard credentials arrive.** Without a real payment provider and SMTP, the API refuses to start. For a demo, add these as well:

| Name | Value |
|---|---|
| `Payments__Provider` | `sandbox` |
| `Payments__Sandbox__AllowOutsideDevelopment` | `true` |
| `Payments__Sandbox__SecretKey` | A new key |
| `Email__Provider` | `file` (only if SMTP isn't set up yet) |
| `Email__AllowFileOutsideDevelopment` | `true` (only with `file`) |

The sandbox marks bills as paid without moving money, so don't let real guests pay. Delete these variables when you go live.

## 4. Publish

1. In MonsterASP, open the website's **Overview → Visual Studio access** and switch WebDeploy to **Enabled**. Check that its server is `site103877.siteasp.net`. If it isn't, change `MSDeployServiceURL` in `MonsterASP.pubxml`.
2. In Visual Studio, right-click **HelloZen.Api → Publish**, choose **MonsterASP** and click **Publish**. The first time, it asks for the WebDeploy password from the same panel. Publishing builds both Angular apps, so Node.js must be installed.

   From a terminal instead (Web Deploy comes with Visual Studio):

   ```powershell
   dotnet publish HelloZen/HelloZen.Api -p:PublishProfile=MonsterASP -p:Password=<WebDeploy password>
   ```

3. Check that it's up:
   - `https://app.hello-zen.com/health/ready` reports `Healthy`.
   - `https://app.hello-zen.com` shows the guest home page.
   - `https://app.hello-zen.com/portal/` shows the staff sign-in.

   If the site shows an error, open the website's **Logs** in MonsterASP, or `App_Data/logs` in **Files**. The first lines name the setting to fix.

## 5. Add the venue

There's no public sign-up during the pilot. Create each restaurant from your PC:

```powershell
.\deploy\monsterasp\admin.ps1 provision --business "Hashem Kitchen" --venue "Abdoun, Amman" --venue-ar "عبدون، عمّان" --owner-email owner@example.jo --owner-name "Lina Haddad" --tables 20
```

It asks for the connection string and for `Qr__TokenKey`. Paste exactly the value set in step 3, or the venue's QR codes won't open. The owner then signs in at `https://app.hello-zen.com/portal/`. The rest is the same as [Add the venue](../README.md#3-add-the-venue) in the Docker guide.

## 6. Keep it running

Payment status checks, e-mail and WhatsApp sending, scheduled reports, reconciliation and POS sync run in the background. They only run while the site is awake, and IIS stops an idle site after about 20 minutes. Turn on **AlwaysRunning** for the website. It's included in Premium; ask MonsterASP support if you can't find it.

## 7. Updates

If the release adds database migrations, run `.\deploy\monsterasp\admin.ps1 migrate` first. Then publish again (step 4).

MonsterASP backs up the website's files every day and keeps 14 days. Check the database's backup options under **Databases**. Keep a copy of the environment variables somewhere safe: without `Qr__TokenKey` the printed QR codes stop working.

For Mastercard, follow [the gateway checklist](../README.md#4-mastercard-gateway-test-checklist) with the webhook URL `https://app.hello-zen.com/v1/webhooks/payments/mastercard`.
