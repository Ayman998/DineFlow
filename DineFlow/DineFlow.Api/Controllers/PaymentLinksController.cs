using System.Text;
using DineFlow.Api.Common;
using DineFlow.Application.Common;
using DineFlow.Application.PaymentLinks;
using DineFlow.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineFlow.Api.Controllers;

/// <summary>Payment links (blueprint 9.2 /v1/merchant/payment-links/*, E16). Any staff member can create and deactivate them.</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[RequiresFeature(nameof(FeatureOptions.PaymentLinks))]
[Route("v1/merchant/payment-links")]
public class MerchantPaymentLinksController(PaymentLinkService links) : ControllerBase
{
    /// <summary>A venue's links, newest first, with counts per status for the filters.</summary>
    [HttpGet]
    public Task<PaymentLinkPageDto> List([FromQuery] PaymentLinkQuery query, CancellationToken ct) => links.ListAsync(query, ct);

    /// <summary>Creates a link (safe to retry with the same clientRequestId); e-mails the customer when an address is given.</summary>
    [HttpPost]
    public Task<PaymentLinkDetailDto> Create(CreatePaymentLinkRequest request, CancellationToken ct) =>
        links.CreateAsync(request, User.UserId(), ct);

    /// <summary>A link with its payments and timeline.</summary>
    [HttpGet("{linkId:guid}")]
    public Task<PaymentLinkDetailDto> Get(Guid linkId, CancellationToken ct) => links.GetAsync(linkId, ct);

    /// <summary>Deactivates the link: nobody can start a new payment on it.</summary>
    [HttpPost("{linkId:guid}/cancel")]
    public Task<PaymentLinkDetailDto> Cancel(Guid linkId, CancellationToken ct) => links.CancelAsync(linkId, User.UserId(), ct);

    /// <summary>E-mails the link to the customer.</summary>
    [HttpPost("{linkId:guid}/notify")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<PaymentLinkDetailDto> Notify(Guid linkId, PaymentLinkNotifyRequest request, CancellationToken ct) =>
        links.NotifyAsync(linkId, request.Email, ct);

    /// <summary>The filtered links as CSV.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] PaymentLinkQuery query, CancellationToken ct)
    {
        var csv = await links.ExportCsvAsync(query, ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8",
            $"payment-links-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}

/// <summary>
/// The customer's payment page behind a link (blueprint 9.1 GET /v1/public/payment-links/{token}).
/// Anonymous: having the link is what lets someone pay it, and every payment is scoped to it.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(Policies.PublicRateLimit)]
[RequiresFeature(nameof(FeatureOptions.PaymentLinks))]
[Route("v1/public/payment-links/{token}")]
public class PublicPaymentLinksController(PaymentLinkCheckoutService checkout) : ControllerBase
{
    [HttpGet]
    public Task<PublicPaymentLinkDto> Get(string token, CancellationToken ct) => checkout.GetAsync(token, ct);

    /// <summary>
    /// Starts paying what's left, equal shares or an amount. Safe to retry with the same clientRequestId.
    /// Returns the provider page to send the customer to (checkoutUrl), or 409 with the current link in "details".
    /// </summary>
    [HttpPost("payments")]
    public Task<LinkPaymentDto> Pay(string token, CreateLinkPaymentRequest request, CancellationToken ct) =>
        checkout.CreatePaymentAsync(token, request, ct);

    [HttpGet("payments/{paymentId:guid}")]
    public Task<LinkPaymentDto> Payment(string token, Guid paymentId, CancellationToken ct) => checkout.GetPaymentAsync(token, paymentId, ct);

    [HttpPost("payments/{paymentId:guid}/cancel")]
    public Task<LinkPaymentDto> Cancel(string token, Guid paymentId, CancellationToken ct) => checkout.CancelPaymentAsync(token, paymentId, ct);

    [HttpPost("payments/{paymentId:guid}/receipt")]
    public async Task<IActionResult> Receipt(string token, Guid paymentId, ReceiptRequest request, CancellationToken ct)
    {
        await checkout.SendReceiptAsync(token, paymentId, request.Email, ct);
        return NoContent();
    }
}
