using DineFlow.Domain.Orders;

namespace DineFlow.Domain.Bills;

/// <summary>One portion of one order line, e.g. the second of two Cokes, priced with its share of service charge and tax.</summary>
public readonly record struct BillUnit(Guid OrderLineId, int UnitIndex, long AmountMinor);

/// <summary>
/// Money math for splitting a bill (blueprint 3.2, E7). Everything is integer minor units, and
/// every split adds up exactly to what it divides, so paid + remaining always equals the total.
/// </summary>
public static class BillAllocation
{
    /// <summary>
    /// Each unit of each line on the order, carrying its share of the order total (inclusive of
    /// service charge and tax) in proportion to its price. The units add up to the order total.
    /// </summary>
    public static IReadOnlyList<BillUnit> Units(Order order)
    {
        // A fixed order (as placed) so every request sees the same units and the same rounding.
        var units = order.Lines
            .OrderBy(l => l.Position).ThenBy(l => l.Id)
            .SelectMany(l => Enumerable.Range(0, l.Quantity).Select(i => (Line: l, Index: i)))
            .ToList();
        var amounts = Proportional(order.TotalMinor, units.Select(u => u.Line.UnitPriceMinor).ToList());
        return units.Select((u, i) => new BillUnit(u.Line.Id, u.Index, amounts[i])).ToList();
    }

    /// <summary>
    /// Divides <paramref name="totalMinor"/> in proportion to <paramref name="weights"/> using the
    /// largest-remainder method: amounts never differ from the exact share by a minor unit or
    /// more, and always add up to the total. Equal weights split evenly, earliest first.
    /// </summary>
    public static long[] Proportional(long totalMinor, IReadOnlyList<long> weights)
    {
        if (totalMinor < 0) throw new ArgumentOutOfRangeException(nameof(totalMinor));
        var result = new long[weights.Count];
        if (weights.Count == 0) return result;

        Int128 weightSum = 0;
        foreach (var w in weights) weightSum += Math.Max(0, w);
        if (weightSum == 0) // nothing to weigh by (free items): split evenly
            return Proportional(totalMinor, weights.Select(_ => 1L).ToList());

        var remainders = new (Int128 Remainder, int Index)[weights.Count];
        long allocated = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            var exact = (Int128)totalMinor * Math.Max(0, weights[i]);
            result[i] = (long)(exact / weightSum);
            remainders[i] = (exact % weightSum, i);
            allocated += result[i];
        }

        // Hand out what's left, one minor unit each, to the largest remainders (earliest wins a tie).
        foreach (var (_, index) in remainders.OrderByDescending(r => r.Remainder).ThenBy(r => r.Index).Take((int)(totalMinor - allocated)))
            result[index]++;
        return result;
    }

    /// <summary>
    /// What paying <paramref name="shares"/> of the <paramref name="sharesLeft"/> remaining equal
    /// shares costs, out of <paramref name="availableMinor"/>. Shares round down; whoever pays the
    /// last share pays exactly what's left, absorbing the rounding (blueprint 3.2).
    /// </summary>
    public static long EqualShare(long availableMinor, int sharesLeft, int shares)
    {
        if (sharesLeft < 1 || shares < 1 || shares > sharesLeft) throw new ArgumentOutOfRangeException(nameof(shares));
        if (availableMinor <= 0) return 0;
        return shares == sharesLeft ? availableMinor : (long)((Int128)availableMinor * shares / sharesLeft);
    }
}
