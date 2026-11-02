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

The pilot's database is `db77252` (Premium, EU, SQL Server 2025, collation `SQL_Latin1_General_CP1_CI_AS`). It was created with **Databases → Add database**, and the plan includes two.

The database has two connection strings. Open **Databases → db77252 → Overview** and click **Show with password** on the tab you need:

| Tab | Used by | Notes |
|---|---|---|
| **Local access** | The website: `ConnectionStrings__HelloZen` in step 3 | Server `db77252.databaseasp.net`. Only reachable from websites hosted at MonsterASP |
| **Remote access** | `admin.ps1` on your PC | Shown only while **Remote access** is switched to **Enabled** on the same tab |

Create the tables from your PC, since MonsterASP has no shell:

1. Switch **Remote access** to **Enabled**.
2. From the repository root, run the command below. When it asks for the **Database password**, click the copy icon next to **Password** on the **Remote access** tab and paste. The script fills in the rest of the Remote access connection string itself:

   ```powershell
   .\deploy\monsterasp\admin.ps1 migrate
   ```

3. Switch **Remote access** back to **Disabled** when you're done (and after `provision` or `reset-password` in step 5). The website never needs it. Repeated failed sign-ins block your IP for a while (**Automated Security Protection**).

A "Login failed for user 'db77252'" error means the password was wrong, for example copied while it still showed `*****`.

## 3. Settings (once)

Open the website, then go to **Scripting → Advanced settings → Environment variables**. Add these:

| Name | Value |
|---|---|
| `ConnectionStrings__HelloZen` | The **Local access** connection string from step 2 |
| `Jwt__SigningKey` | A new key (see below) |
| `Qr__TokenKey` | A new key. It signs every QR code: never change it once codes are printed, and keep a copy somewhere safe |
| `Payments__Mastercard__MerchantId`, `__ApiPassword`, `__WebhookSecret` | From the acquirer (see [the gateway checklist](../README.md#4-mastercard-gateway-test-checklist)) |
| `Email__Smtp__Host`, `__Port`, `__Security`, `__Username`, `__Password` | From the mail service that sends from `receipts@hello-zen.com`. MonsterASP's own **E-mails** service works, and the plan includes one |
| `Payments__TermsUrl` | Your published terms page |

For each key, run this from the repository root. It puts a new random key on the clipboard without showing it, ready to paste:

```powershell
.\deploy\monsterasp\new-key.ps1
```

MonsterASP keeps these variables on the site's application pool, not in `web.config`, so publishing doesn't wipe them. New values apply when the app next starts (**Scripting → Restart application**).

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

   Or from a terminal, which needs Web Deploy (it comes with Visual Studio). It asks for the WebDeploy password without showing it:

   ```powershell
   .\deploy\monsterasp\publish.ps1
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

With **Remote access** enabled again (step 2), it asks three things, without showing what you type:

1. **The owner's portal password**, twice, at least 10 characters. Nothing is printed for the owner to lose, so tell them the password privately. (Running `provision` some other way prints a temporary password once instead.)
2. **The database password**, as in step 2.
3. **`Qr__TokenKey`**: paste exactly the value set in step 3, or the venue's QR codes won't open.

It should end with `Created …` and `The owner signs in with the password you chose.` The owner then signs in at `https://app.hello-zen.com/portal/`. The rest is the same as [Add the venue](../README.md#3-add-the-venue) in the Docker guide.

If someone can't sign in any more, for example an owner who lost their password, give them a new one the same way:

```powershell
.\deploy\monsterasp\admin.ps1 reset-password --email owner@example.jo
```

Owners can also reset their staff's passwords themselves, under **Settings → Team**.

## 6. Keep it running

Payment status checks, e-mail and WhatsApp sending, scheduled reports, reconciliation and POS sync run in the background. They only run while the site is awake, and IIS stops an idle site after about 20 minutes.

Premium lists an **AlwaysRunning** feature, but the website's Scripting and Process pages have no switch for it, so ask MonsterASP support to turn it on. Until then, keep the site awake with a planned task: open the website's **Tasks → Add planned task** and have it call `https://app.hello-zen.com/health/live` every 5 minutes.

## 7. Updates

Updates deploy themselves. Every push to `main` runs **CI** on GitHub (the .NET and Angular builds and tests). When CI passes, the **Deploy** workflow (`.github/workflows/deploy.yml`) publishes the API, the guest app and `/portal` to `https://app.hello-zen.com`, the same way step 4 does by hand. It ends by checking that `/health/ready` reports Healthy. Pull requests never deploy.

The site applies any new database migrations itself as it starts (`Database:MigrateOnStartup` in `appsettings.MonsterAsp.json`), so updates need no `migrate` step and Remote access can stay off. If a migration fails, the site doesn't start: look at **Logs**, fix it, and push again.

Setting it up once: in GitHub, open the repository's **Settings → Secrets and variables → Actions → New repository secret**. Name it `MONSTERASP_WEBDEPLOY_PASSWORD`, and set its value to the WebDeploy password (step 4). If that password is ever changed in MonsterASP, update the secret too.

To deploy without pushing, for example after changing a setting, open **Actions → Deploy → Run workflow**. Step 4 still works from your PC as a fallback.

MonsterASP backs up the website's files every day and keeps 14 days. Check the database's backup options under **Databases**. Keep a copy of the environment variables somewhere safe: without `Qr__TokenKey` the printed QR codes stop working.

For Mastercard, follow [the gateway checklist](../README.md#4-mastercard-gateway-test-checklist) with the webhook URL `https://app.hello-zen.com/v1/webhooks/payments/mastercard`.
