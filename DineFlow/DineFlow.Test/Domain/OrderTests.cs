using DineFlow.Domain.Common;
using DineFlow.Domain.Orders;

namespace DineFlow.Test.Domain;

public class OrderPricingTests
{
    [Fact]
    public void Tax_on_top_applies_to_subtotal_plus_service_charge()
    {
        // 100.00 + 10% service = 110.00; + 5% tax = 5.50
        var price = OrderPricing.Calculate(10_000, serviceChargeBasisPoints: 1000, taxBasisPoints: 500, taxIncluded: false);

        Assert.Equal(1_000, price.ServiceChargeMinor);
        Assert.Equal(550, price.TaxMinor);
        Assert.Equal(11_550, price.TotalMinor);
        Assert.False(price.TaxIncluded);
    }

    [Fact]
    public void Tax_inclusive_prices_show_the_tax_share_without_adding_it()
    {
        // 105.00 including 5% VAT: 5.00 of it is tax.
        var price = OrderPricing.Calculate(10_500, serviceChargeBasisPoints: 0, taxBasisPoints: 500, taxIncluded: true);

        Assert.Equal(500, price.TaxMinor);
        Assert.Equal(10_500, price.TotalMinor);
        Assert.True(price.TaxIncluded);
    }

    [Theory]
    [InlineData(1_005, 1000, 101)] // 100.5 rounds half away from zero → 101
    [InlineData(1_004, 1000, 100)]
    [InlineData(0, 1000, 0)]
    public void Service_charge_rounds_half_away_from_zero(long subtotal, int basisPoints, long expected)
    {
        Assert.Equal(expected, OrderPricing.Calculate(subtotal, basisPoints, 0, false).ServiceChargeMinor);
    }

    [Fact]
    public void No_charges_means_total_equals_subtotal()
    {
        var price = OrderPricing.Calculate(4_200, 0, 0, true);

        Assert.Equal(new PriceBreakdown(4_200, 0, 0, true, 4_200), price);
    }
}

public class OrderStatusTests
{
    private static Order Create()
    {
        var line = new OrderLine(Guid.NewGuid(), Guid.NewGuid(), "Burger", null, 5_500, 1, [], null);
        return new Order(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Order.FirstNumber,
            "Table", "12", "Main hall", "AED", 0, 0, true, [line], null, DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Happy_path_runs_from_submitted_to_completed()
    {
        var order = Create();

        foreach (var next in new[] { OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Completed })
            order.TransitionTo(next, null, DateTimeOffset.UtcNow);

        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Preparing)]
    [InlineData(OrderStatus.Ready)]
    [InlineData(OrderStatus.Completed)]
    public void A_new_order_must_be_accepted_or_rejected_first(OrderStatus next)
    {
        Assert.Throws<DomainException>(() => Create().TransitionTo(next, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_is_final()
    {
        var order = Create();

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.Rejected, " ", DateTimeOffset.UtcNow));
        order.TransitionTo(OrderStatus.Rejected, "Kitchen closed", DateTimeOffset.UtcNow);

        Assert.Equal("Kitchen closed", order.RejectReason);
        Assert.False(order.CanTransitionTo(OrderStatus.Accepted));
    }

    [Fact]
    public void Line_price_includes_options_times_quantity()
    {
        var line = new OrderLine(Guid.NewGuid(), Guid.NewGuid(), "Burger", null, 5_500, 2,
            [new OrderLineModifier(Guid.NewGuid(), "Extras", null, "Cheese", null, 400)], null);

        Assert.Equal(5_900, line.UnitPriceMinor);
        Assert.Equal(11_800, line.LineTotalMinor);
    }
}
