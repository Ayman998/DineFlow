using DineFlow.Api.Common;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Application.Payments;
using DineFlow.Application.Promotions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineFlow.Api.Controllers;

/// <summary>Guest bill and payments (blueprint 9.1). Every call is limited to the caller's own session.</summary>
[ApiController]
[Authorize(Policy = Policies.Guest)]
public class PaymentsController(BillService bills, PromotionService promotions, PaymentService payments) : ControllerBase
{
    /// <summary>The table's bill: orders, totals, what's been paid and what's left, tip options and payment methods.</summary>
    [HttpGet("v1/public/sessions/{sessionId:guid}/bill")]
    public Task<BillDto> Bill(Guid sessionId, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return bills.GetForSessionAsync(sessionId, ct);
    }

    /// <summary>Sets how many people the table splits the bill between equally, or removes the split (people: null).</summary>
    [HttpPut("v1/public/sessions/{sessionId:guid}/split")]
    public Task<BillDto> SetSplit(Guid sessionId, SplitRequest request, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return bills.SetSplitAsync(sessionId, request.People, ct);
    }

    /// <summary>
    /// Holds the items this guest is paying for while they choose (blueprint allocations/reserve).
    /// Send the full selection each time; an empty list lets everything go.
    /// </summary>
    [HttpPost("v1/public/sessions/{sessionId:guid}/allocations/reserve")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<ReserveResultDto> Reserve(Guid sessionId, ReserveRequest request, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return bills.ReserveAsync(sessionId, request.Units, ct);
    }

    [HttpPost("v1/public/sessions/{sessionId:guid}/promotions/validate")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<PromoQuoteDto> ValidatePromotion(Guid sessionId, PromoValidateRequest request, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return promotions.ValidateAsync(sessionId, request.Code, request.AmountMinor, ct);
    }

    /// <summary>
    /// Starts paying the rest of the bill, equal shares, the guest's items or an amount. Safe to retry
    /// with the same clientRequestId. Returns the provider page to send the guest to (checkoutUrl),
    /// or 409 with the current bill in "details" when it changed.
    /// </summary>
    [HttpPost("v1/public/payments")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<PaymentDto> Create(CreatePaymentRequest request, CancellationToken ct)
    {
        EnsureOwnSession(request.SessionId);
        return payments.CreateAsync(request, ct);
    }

    /// <summary>Payment status for the confirmation screen; the app polls this until it's final.</summary>
    [HttpGet("v1/public/payments/{paymentId:guid}")]
    public Task<PaymentDto> Get(Guid paymentId, CancellationToken ct) =>
        payments.GetAsync(SessionId(), paymentId, ct);

    [HttpPost("v1/public/payments/{paymentId:guid}/cancel")]
    public Task<PaymentDto> Cancel(Guid paymentId, CancellationToken ct) =>
        payments.CancelAsync(SessionId(), paymentId, ct);

    [HttpPost("v1/public/payments/{paymentId:guid}/receipt")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public async Task<IActionResult> Receipt(Guid paymentId, ReceiptRequest request, CancellationToken ct)
    {
        await payments.SendReceiptAsync(SessionId(), paymentId, request.Email, ct);
        return NoContent();
    }

    private Guid SessionId() =>
        User.GuestSessionId() ?? throw AppException.Unauthorized("session_expired", "Scan the QR code again.");

    private void EnsureOwnSession(Guid sessionId)
    {
        if (User.GuestSessionId() != sessionId)
            throw AppException.NotFound("session_not_found", "Session not found.");
    }
}

/// <summary>
/// Provider webhooks (blueprint 9.3 PaymentProviderSucceeded/Failed). Anonymous, but every event
/// must carry a valid signature; anything else gets a 400 and changes nothing.
/// </summary>
[ApiController]
[AllowAnonymous]
public class PaymentWebhooksController(PaymentService payments) : ControllerBase
{
    [HttpPost("v1/webhooks/payments/{provider}")]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        await payments.HandleWebhookAsync(provider, body, headers, ct);
        return Ok(new { received = true });
    }
}

/// <summary>Promotion and voucher codes (blueprint 9.2 /v1/merchant/promotions/*).</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant/promotions")]
public class MerchantPromotionsController(PromotionService promotions) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PromoCodeDto>> List(CancellationToken ct) => promotions.ListAsync(ct);

    [HttpPost]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<PromoCodeDto> Create(PromoCodeRequest request, CancellationToken ct) => promotions.CreateAsync(request, ct);

    [HttpPut("{promoId:guid}")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<PromoCodeDto> Update(Guid promoId, PromoCodeRequest request, CancellationToken ct) =>
        promotions.UpdateAsync(promoId, request, ct);
}
