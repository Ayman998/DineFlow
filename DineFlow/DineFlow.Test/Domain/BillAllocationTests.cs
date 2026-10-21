using DineFlow.Domain.Bills;

namespace DineFlow.Test.Domain;

public class BillAllocationTests
{
    [Fact]
    public void Proportional_amounts_always_add_up_to_the_total()
    {
        var random = new Random(42);
        for (var run = 0; run < 2_000; run++)
        {
            var weights = Enumerable.Range(0, random.Next(1, 12)).Select(_ => (long)random.Next(0, 20_000)).ToList();
            var total = (long)random.Next(0, 500_000);

            var amounts = BillAllocation.Proportional(total, weights);

            Assert.Equal(total, amounts.Sum());
            Assert.All(amounts, a => Assert.True(a >= 0));
            var weightSum = weights.Sum();
            if (weightSum == 0) continue;
            for (var i = 0; i < weights.Count; i++)
            {
                var exact = (decimal)total * weights[i] / weightSum;
                Assert.True(Math.Abs(amounts[i] - exact) < 1, $"unit {i}: {amounts[i]} vs exact {exact}");
            }
        }
    }

    [Fact]
    public void Proportional_splits_rounding_to_the_largest_remainders_then_earliest()
    {
        Assert.Equal([3_334, 3_333, 3_333], BillAllocation.Proportional(10_000, [1, 1, 1]));
        Assert.Equal([5_500, 1_375, 2_200], BillAllocation.Proportional(9_075, [5_000, 1_250, 2_000]));
        Assert.Equal([5, 5], BillAllocation.Proportional(10, [0, 0])); // free items share evenly
    }

    [Theory]
    [InlineData(10_000, 3)]
    [InlineData(12_345, 4)]
    [InlineData(1, 5)]
    [InlineData(99_999, 7)]
    public void Paying_every_equal_share_in_turn_pays_exactly_the_bill(long bill, int people)
    {
        var left = bill;
        var shares = new List<long>();
        for (var sharesLeft = people; sharesLeft > 0; sharesLeft--)
        {
            var share = BillAllocation.EqualShare(left, sharesLeft, 1);
            shares.Add(share);
            left -= share;
        }

        Assert.Equal(0, left);
        Assert.Equal(bill, shares.Sum());
        Assert.True(shares.Max() - shares.Min() <= people, "shares stay within rounding of each other");
    }

    [Fact]
    public void Paying_several_shares_at_once_costs_the_same_as_paying_them_together_at_the_end()
    {
        Assert.Equal(6_000, BillAllocation.EqualShare(12_000, 4, 2));
        Assert.Equal(12_000, BillAllocation.EqualShare(12_000, 4, 4));
        Assert.Equal(3_334, BillAllocation.EqualShare(3_334, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BillAllocation.EqualShare(1_000, 2, 3));
    }
}
