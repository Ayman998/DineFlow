using DineFlow.Domain.Common;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;

namespace DineFlow.Test.Domain;

public class TipTests
{
    [Theory]
    [InlineData(10_000, 2_000)]
    [InlineData(1_003, 201)] // 200.6 rounds up, as the guest apps round 20%
    [InlineData(1_002, 200)]
    public void The_largest_tip_is_20_percent_of_the_amount(long amount, long max) =>
        Assert.Equal(max, Venue.MaxTipMinor(amount));

    [Fact]
    public void Tip_suggestions_above_20_percent_are_refused()
    {
        var venue = new Venue(Guid.NewGuid(), "Marina", "JOD", "Asia/Amman", "en");

        Assert.Throws<DomainException>(() => venue.SetTips(true, [1000, 2500], 1000));
        venue.SetTips(true, [1000, 2000], 2000);

        Assert.Equal([1000, 2000], venue.TipPresetsBasisPoints);
    }

    [Fact]
    public void A_payment_cannot_tip_more_than_20_percent()
    {
        Payment Pay(long tip) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "sandbox", PaymentMethod.Card, PaymentMode.Full, null, null, "JOD", 10_000, 0, tip, null, null, "1", DateTimeOffset.UtcNow);

        Assert.Throws<DomainException>(() => Pay(2_001));
        Assert.Equal(12_000, Pay(2_000).AmountMinor);
    }
}
