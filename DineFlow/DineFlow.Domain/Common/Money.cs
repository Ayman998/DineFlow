namespace DineFlow.Domain.Common;

/// <summary>
/// Money as integer minor units (fils, halalas, cents) plus an ISO 4217 currency code.
/// Never use floating point for money.
/// </summary>
public readonly record struct Money
{
    public long Minor { get; }
    public string Currency { get; }

    public Money(long minor, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO 4217 code.", nameof(currency));

        Minor = minor;
        Currency = currency.ToUpperInvariant();
    }

    public static Money Zero(string currency) => new(0, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(checked(a.Minor + b.Minor), a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(checked(a.Minor - b.Minor), a.Currency);
    }

    /// <summary>
    /// Splits into <paramref name="parts"/> shares. Earlier shares get the floor amount
    /// and the last share absorbs the rounding remainder, so the shares always sum to the total.
    /// </summary>
    public IReadOnlyList<Money> Split(int parts)
    {
        if (parts <= 0) throw new ArgumentOutOfRangeException(nameof(parts));

        var share = Minor / parts;
        var result = new Money[parts];
        for (var i = 0; i < parts - 1; i++) result[i] = new Money(share, Currency);
        result[parts - 1] = new Money(Minor - share * (parts - 1), Currency);
        return result;
    }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
            throw new InvalidOperationException($"Currency mismatch: {a.Currency} vs {b.Currency}.");
    }

    public override string ToString() => $"{Minor} {Currency}";
}
