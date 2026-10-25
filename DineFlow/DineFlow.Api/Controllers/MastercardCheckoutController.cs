using System.Net;
using System.Security.Cryptography;
using DineFlow.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DineFlow.Api.Controllers;

/// <summary>
/// Mastercard Gateway Hosted Checkout needs the gateway's checkout.js to open its payment page, so
/// the guest is sent here first: this page loads the script from the gateway and shows the payment
/// page for the session. Card details are only ever entered on the gateway's page.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route(MastercardGatewayProvider.LauncherPath)]
public class MastercardCheckoutController(IOptions<MastercardGatewayOptions> options) : ControllerBase
{
    [HttpGet]
    public IActionResult Launch([FromQuery] string? session)
    {
        if (!MastercardGatewayProvider.IsSessionId(session))
            return Content("<p>This payment link isn't valid. Go back and tap Pay again.</p>", "text/html; charset=utf-8");

        var gateway = new Uri(options.Value.GatewayUrl).GetLeftPart(UriPartial.Authority);
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        // Only this page's script and the gateway's own resources; replaces the API's default "nothing at all" policy.
        Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; script-src 'nonce-{nonce}' {gateway}; connect-src {gateway}; frame-src {gateway}; img-src {gateway} data:; " +
            $"style-src 'unsafe-inline' {gateway}; font-src {gateway}; form-action {gateway}; base-uri 'none'; frame-ancestors 'none'";
        Response.Headers.CacheControl = "no-store";

        var html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Secure payment</title>
              <style>body{font-family:system-ui,sans-serif;display:flex;min-height:100vh;align-items:center;justify-content:center;color:#475467;margin:0}</style>
              <script nonce="{{nonce}}" src="{{gateway}}/static/checkout/checkout.min.js"
                      data-error="dineflowCheckoutError" data-cancel="dineflowCheckoutCancel"></script>
            </head>
            <body>
              <p id="status">Opening the secure payment page…</p>
              <script nonce="{{nonce}}">
                function dineflowCheckoutError() { document.getElementById('status').textContent = "The payment page couldn't open. Go back and try again."; }
                function dineflowCheckoutCancel() { history.back(); }
                Checkout.configure({ session: { id: '{{WebUtility.HtmlEncode(session)}}' } });
                Checkout.showPaymentPage();
              </script>
            </body>
            </html>
            """;
        return Content(html, "text/html; charset=utf-8");
    }
}
