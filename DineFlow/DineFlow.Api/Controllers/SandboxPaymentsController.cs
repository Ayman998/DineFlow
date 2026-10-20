using System.Globalization;
using System.Net;
using System.Text;
using DineFlow.Api.Common;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Payments;
using DineFlow.Domain.Payments;
using DineFlow.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineFlow.Api.Controllers;

/// <summary>
/// The sandbox provider's hosted payment page: what a real PSP shows the guest. It takes test cards
/// only, then sends its signed webhook through the same verification as any provider event, and
/// returns the guest to DineFlow. No real money moves.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("sandbox/pay/{providerPaymentId}")]
public class SandboxPaymentsController(SandboxPaymentProvider sandbox, PaymentService payments) : ControllerBase
{
    [HttpGet]
    public IActionResult Show(string providerPaymentId, [FromQuery] string? t)
    {
        var checkout = sandbox.ReadCheckout(providerPaymentId, t ?? "");
        return checkout is null ? Expired() : Page(checkout, t!, error: null);
    }

    [HttpPost]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public async Task<IActionResult> Submit(string providerPaymentId, [FromForm] SandboxForm form, CancellationToken ct)
    {
        var checkout = sandbox.ReadCheckout(providerPaymentId, form.T ?? "");
        if (checkout is null) return Expired();

        if (form.Action == "cancel") return Redirect(checkout.CancelUrl);

        SandboxCards.Outcome outcome;
        if (checkout.Method == PaymentMethod.Card)
        {
            if (SandboxCards.For(form.Card ?? "") is not { } card)
                return Page(checkout, form.T!, "That card number isn't valid. Use one of the test cards below.");
            outcome = card;
        }
        else
        {
            // Wallets just approve or decline in the sandbox.
            outcome = form.Action == "decline"
                ? new SandboxCards.Outcome(PaymentStatus.Failed, WalletName(checkout.Method), "0000", "The payment was declined.")
                : new SandboxCards.Outcome(PaymentStatus.Succeeded, WalletName(checkout.Method), "4242", null);
        }

        var (body, signature) = sandbox.CreateWebhook(new ProviderWebhookEvent(
            $"evt_{Guid.NewGuid():N}", checkout.ProviderPaymentId, outcome.Status, outcome.Brand, outcome.Last4, outcome.FailureReason));
        await payments.HandleWebhookAsync(sandbox.Name, body,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [SandboxPaymentProvider.SignatureHeader] = signature }, ct);

        return Redirect(checkout.ReturnUrl);
    }

    public record SandboxForm(string? T, string? Action, string? Card);

    private ContentResult Expired() => Html(StatusCodes.Status410Gone, null, """
        <h1>This payment link has expired</h1>
        <p class="muted">Go back to the bill and tap Pay again.</p>
        """);

    private ContentResult Page(SandboxCheckout c, string token, string? error)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var digits = c.Currency is "KWD" or "BHD" or "OMR" or "JOD" ? 3 : 2;
        var amount = (c.AmountMinor / (decimal)Math.Pow(10, digits)).ToString($"N{digits}", CultureInfo.InvariantCulture);
        var action = $"/sandbox/pay/{Uri.EscapeDataString(c.ProviderPaymentId)}";

        var fields = new StringBuilder();
        if (c.Method == PaymentMethod.Card)
        {
            fields.Append($"""
                <label>Card number<input name="card" inputmode="numeric" autocomplete="off" value="4242 4242 4242 4242" required></label>
                <div class="row">
                  <label>Expiry<input value="12 / 34" autocomplete="off"></label>
                  <label>CVC<input value="123" autocomplete="off"></label>
                </div>
                <button class="primary" name="action" value="pay">Pay {E(c.Currency)} {amount}</button>
                <details><summary>Test cards</summary><ul>
                """);
            foreach (var (number, description) in SandboxCards.Examples)
                fields.Append($"<li><code>{E(number)}</code> {E(description)}</li>");
            fields.Append("</ul><p class=\"muted\">Any expiry date in the future and any CVC.</p></details>");
        }
        else
        {
            fields.Append($"""
                <button class="primary" name="action" value="pay">Approve with {E(WalletName(c.Method))}</button>
                <button name="action" value="decline">Decline</button>
                """);
        }

        return Html(StatusCodes.Status200OK, c.ReturnUrl, $"""
            <p class="badge">Sandbox · test payments only</p>
            <p class="muted">{E(c.MerchantName)}</p>
            <h1>{E(c.Currency)} {amount}</h1>
            <p class="muted">Reference {E(c.Reference)}</p>
            {(error is null ? "" : $"<p class=\"error\" role=\"alert\">{E(error)}</p>")}
            <form method="post" action="{E(action)}">
              <input type="hidden" name="t" value="{E(token)}">
              {fields}
              <button class="link" name="action" value="cancel" formnovalidate>Cancel and go back</button>
            </form>
            """);
    }

    private ContentResult Html(int status, string? returnUrl, string content)
    {
        // The form posts here, then redirects back to the guest app, so allow that origin too.
        var formTargets = "'self'";
        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var back)) formTargets += $" {back.GetLeftPart(UriPartial.Authority)}";
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src 'unsafe-inline'; form-action {formTargets}; frame-ancestors 'none'; base-uri 'none'";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";

        return new ContentResult
        {
            StatusCode = status,
            ContentType = "text/html; charset=utf-8",
            Content = $$"""
                <!doctype html>
                <html lang="en">
                <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <meta name="robots" content="noindex">
                <title>Sandbox payment</title>
                <style>
                  *, *::before, *::after { box-sizing: border-box; }
                  :root { color-scheme: light; font-family: "Segoe UI", system-ui, sans-serif; color: #101828; }
                  body { margin: 0; background: #F2F4F7; display: grid; place-items: center; min-height: 100vh; }
                  main { background: #fff; width: min(400px, calc(100% - 32px)); padding: 24px; border-radius: 16px; box-shadow: 0 8px 24px rgba(16,24,40,.08); }
                  h1 { margin: 4px 0; font-size: 32px; }
                  .muted { color: #475467; margin: 4px 0; }
                  .badge { display: inline-block; margin: 0 0 12px; padding: 4px 10px; border-radius: 999px; background: #FEF0C7; color: #93370D; font-size: 13px; font-weight: 600; }
                  .error { background: #FEE4E2; color: #B42318; padding: 10px 12px; border-radius: 8px; }
                  form { display: grid; gap: 12px; margin-top: 20px; }
                  label { display: grid; gap: 6px; font-size: 14px; font-weight: 600; }
                  input { font: inherit; width: 100%; min-width: 0; padding: 12px; border: 1px solid #D0D5DD; border-radius: 10px; }
                  .row { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
                  button { font: inherit; font-weight: 600; padding: 14px; border-radius: 12px; border: 1px solid #D0D5DD; background: #fff; cursor: pointer; }
                  button.primary { background: #0E9384; border-color: #0E9384; color: #fff; }
                  button.link { border: 0; background: none; color: #475467; padding: 8px; }
                  details { font-size: 14px; color: #475467; }
                  ul { padding-inline-start: 18px; } code { color: #101828; }
                </style>
                </head>
                <body><main>
                {{content}}
                </main></body>
                </html>
                """
        };
    }

    private static string WalletName(PaymentMethod method) => method switch
    {
        PaymentMethod.ApplePay => "Apple Pay",
        PaymentMethod.GooglePay => "Google Pay",
        PaymentMethod.StcPay => "STC Pay",
        _ => "Card"
    };
}
