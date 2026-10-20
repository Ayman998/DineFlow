namespace DineFlow.Domain.Orders;

/// <summary>Subtotal, service charge, tax and total for an order, all in minor units.</summary>
public readonly record struct PriceBreakdown(
    long SubtotalMinor,
    long ServiceChargeMinor,
    long TaxMinor,
    bool TaxIncluded,
    long TotalMinor);

/// <summary>
/// The one place order totals are calculated. Service charge applies to the subtotal; tax
/// applies to subtotal + service charge. With tax-inclusive prices the tax is the share of
/// that amount that is tax (shown, not added). Each step rounds half away from zero.
/// </summary>
public static class OrderPricing
{
    public static PriceBreakdown Calculate(long subtotalMinor, int serviceChargeBasisPoints, int taxBasisPoints, bool taxIncluded)
    {
        if (subtotalMinor < 0) throw new ArgumentOutOfRangeException(nameof(subtotalMinor));

        var serviceCharge = ApplyRate(subtotalMinor, serviceChargeBasisPoints);
        var taxable = subtotalMinor + serviceCharge;

        if (taxIncluded)
        {
            var net = Round(taxable * 10_000m / (10_000 + taxBasisPoints));
            return new PriceBreakdown(subtotalMinor, serviceCharge, taxable - net, true, taxable);
        }

        var tax = ApplyRate(taxable, taxBasisPoints);
        return new PriceBreakdown(subtotalMinor, serviceCharge, tax, false, taxable + tax);
    }

    private static long ApplyRate(long amountMinor, int basisPoints) => Round(amountMinor * (decimal)basisPoints / 10_000m);

    private static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
