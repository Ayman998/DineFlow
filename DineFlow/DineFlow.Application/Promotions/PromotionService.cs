using System.ComponentModel.DataAnnotations;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using DineFlow.Domain.Promotions;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Promotions;

public record PromoCodeDto(
    Guid Id, string Code, string Headline, DiscountType Type, long Value, long? MaxDiscountMinor, long? MinBillMinor,
    DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, int? MaxRedemptions, int RedemptionCount, bool IsActive, bool ShowAfterPayment);

/// <param name="Value">Percent in basis points (1000 = 10%) or an amount in minor units.</param>
public record PromoCodeRequest(
    [Required, StringLength(20, MinimumLength = 3)] string Code,
    [Required, StringLength(120, MinimumLength = 1)] string Headline,
    [Required] DiscountType Type,
    [Range(1, 100_000_000)] long Value,
    [Range(1, 100_000_000)] long? MaxDiscountMinor,
    [Range(0, 100_000_000)] long? MinBillMinor,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    [Range(1, 1_000_000)] int? MaxRedemptions,
    bool IsActive,
    bool ShowAfterPayment);

/// <param name="AmountMinor">The guest's share of the bill they're about to pay; everything they can pay when omitted.</param>
public record PromoValidateRequest([Required, StringLength(20)] string Code, [Range(1, 100_000_000)] long? AmountMinor = null);

/// <summary>A code that applies to this bill, and how much it takes off what's left to pay.</summary>
public record PromoQuoteDto(string Code, string Headline, long DiscountMinor);

/// <summary>Offered on the payment confirmation for the guest's next visit (P1-REV-01).</summary>
public record NextVisitOfferDto(string Code, string Headline);

/// <summary>Promotion and voucher codes (blueprint E30): merchant management and guest validation.</summary>
public class PromotionService(IAppDbContext db, ITenantContext tenant, BillService bills, TimeProvider clock)
{
    // ---- Guests ----

    /// <summary>
    /// Checks a code against what the guest is about to pay, server side only (P1-PAY-08):
    /// <paramref name="amountMinor"/> is their share of the bill, or everything they can pay when null.
    /// </summary>
    public async Task<PromoQuoteDto> ValidateAsync(Guid sessionId, string code, long? amountMinor, CancellationToken ct)
    {
        var bill = await bills.GetForSessionAsync(sessionId, ct);
        if (bill.Status != BillStatus.Open || bill.AvailableMinor <= 0)
            throw AppException.Conflict("nothing_to_pay", "There's nothing left to pay on this bill.");

        var share = Math.Min(amountMinor ?? bill.AvailableMinor, bill.AvailableMinor);
        var (promo, evaluation) = await EvaluateAsync(code, share, ct);
        return new PromoQuoteDto(promo.Code, promo.Headline, evaluation.DiscountMinor);
    }

    /// <summary>The code, if usable, and its discount on <paramref name="billMinor"/>; throws a 400 explaining why not.</summary>
    internal async Task<(PromoCode Promo, PromoEvaluation Evaluation)> EvaluateAsync(string code, long billMinor, CancellationToken ct)
    {
        var normalised = PromoCode.Normalise(code);
        var promo = await db.PromoCodes.SingleOrDefaultAsync(p => p.Code == normalised, ct)
            ?? throw AppException.BadRequest("promo_invalid", "That code isn't valid here.");

        var evaluation = promo.Evaluate(billMinor, clock.GetUtcNow());
        if (!evaluation.IsValid) throw AppException.BadRequest(evaluation.Code!, evaluation.Message!);
        return (promo, evaluation);
    }

    public async Task<NextVisitOfferDto?> NextVisitOfferAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var offers = await db.PromoCodes.Where(p => p.ShowAfterPayment && p.IsActive).OrderBy(p => p.Code).ToListAsync(ct);
        var offer = offers.FirstOrDefault(p => p.IsAvailableAt(now));
        return offer is null ? null : new NextVisitOfferDto(offer.Code, offer.Headline);
    }

    // ---- Merchants ----

    public async Task<IReadOnlyList<PromoCodeDto>> ListAsync(CancellationToken ct) =>
        (await db.PromoCodes.OrderBy(p => p.Code).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<PromoCodeDto> CreateAsync(PromoCodeRequest request, CancellationToken ct)
    {
        var code = PromoCode.Normalise(request.Code);
        if (await db.PromoCodes.AnyAsync(p => p.Code == code, ct))
            throw AppException.Conflict("promo_exists", $"There's already a code {code}.");

        var promo = Apply(() =>
        {
            var p = new PromoCode(tenant.RequireTenantId(), code);
            Update(p, request);
            return p;
        });

        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync(ct);
        return ToDto(promo);
    }

    /// <summary>Updates everything except the code itself, which guests may already have.</summary>
    public async Task<PromoCodeDto> UpdateAsync(Guid id, PromoCodeRequest request, CancellationToken ct)
    {
        var promo = await db.PromoCodes.SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw AppException.NotFound("promo_not_found", "Promotion not found.");

        Apply(() =>
        {
            Update(promo, request);
            return promo;
        });
        await db.SaveChangesAsync(ct);
        return ToDto(promo);
    }

    private static void Update(PromoCode p, PromoCodeRequest r) =>
        p.Update(r.Headline, r.Type, r.Value, r.MaxDiscountMinor, r.MinBillMinor, r.StartsAt, r.EndsAt,
            r.MaxRedemptions, r.IsActive, r.ShowAfterPayment);

    private static T Apply<T>(Func<T> change)
    {
        try { return change(); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_promo", ex.Message); }
    }

    private static PromoCodeDto ToDto(PromoCode p) => new(
        p.Id, p.Code, p.Headline, p.Type, p.Value, p.MaxDiscountMinor, p.MinBillMinor,
        p.StartsAt, p.EndsAt, p.MaxRedemptions, p.RedemptionCount, p.IsActive, p.ShowAfterPayment);
}
