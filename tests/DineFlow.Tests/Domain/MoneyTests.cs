using DineFlow.Domain.Common;

namespace DineFlow.Tests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData(10000, 3)]
    [InlineData(1, 4)]
    [InlineData(9999, 7)]
    public void Split_shares_always_sum_to_total(long total, int parts)
    {
        var shares = new Money(total, "AED").Split(parts);

        Assert.Equal(parts, shares.Count);
        Assert.Equal(total, shares.Sum(s => s.Minor));
    }

    [Fact]
    public void Split_last_share_absorbs_rounding()
    {
        var shares = new Money(10000, "AED").Split(3);

        Assert.Equal(new long[] { 3333, 3333, 3334 }, shares.Select(s => s.Minor));
    }

    [Fact]
    public void Adding_different_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new Money(100, "AED") + new Money(100, "SAR"));
    }
}
