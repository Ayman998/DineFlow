using DineFlow.Application.Abstractions;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Promotions;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Promotions;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

/// <summary>Splitting the bill (blueprint E7): equal shares, by item, custom amounts, and guests acting at once.</summary>
public class SplitBillTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private sealed record Fixture(Guid TenantId, VenueDto Venue, string QrToken, ItemDto Burger, ItemDto Coke, ItemDto Soup)
    {
        public List<Guid> Guests { get; } = [];
    }

    /// <summary>
    /// Table 7 with three guests. With charges, the venue adds 10% service (VAT included in prices);
    /// without, totals are plain. Leaves the host acting as the guests' tenant.
    /// </summary>
    private async Task<Fixture> CreateAsync(bool charges = false)
    {
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        await _host.Venues.UpdateSettingsAsync(venue.Id, new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38",
            true, true, ServiceChargeBasisPoints: charges ? 1000 : 0, TaxBasisPoints: charges ? 500 : 0), default);

        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", null), default);
        ItemDto Item(string name, long price) => _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, name, null, null, null, price, null, true, [], [], []), default).Result;
        var burger = Item("Burger", 5000);
        var coke = Item("Coke", 1250);
        var soup = Item("Soup", 2000);
        await _host.Promotions.CreateAsync(new PromoCodeRequest("WELCOME10", "10% off", DiscountType.Percent, 1000, null, null, null, null, null, true, false), default);

        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);
        var f = new Fixture(tenant.Id, venue, TestHost.TokenFromUrl(table.Qr!.Url), burger, coke, soup);
        for (var i = 0; i < 3; i++) f.Guests.Add(await NewGuestAsync(f));
        return f;
    }

    private async Task<Guid> NewGuestAsync(Fixture f)
    {
        _host.CurrentTenant = null;
        var session = await _host.Guests.ResolveQrAsync(f.QrToken, null, default);
        _host.CurrentTenant = f.TenantId;
        return session.Context.SessionId;
    }

    private async Task<OrderDto> OrderAsync(Guid sessionId, params (ItemDto Item, int Quantity)[] lines)
    {
        var request = lines.Select(l => new CartLineRequest(l.Item.Id, l.Quantity, [], null)).ToList();
        var quote = await _host.CartQuotes.QuoteAsync(sessionId, new CartRequest(request), default);
        return await _host.GuestOrders.PlaceAsync(sessionId, new PlaceOrderRequest(Guid.NewGuid(), request, null, quote.TotalMinor), default);
    }

    private Task<BillDto> BillAsync(Guid sessionId) => _host.Bills.GetForSessionAsync(sessionId, default);

    private static CreatePaymentRequest Pay(Guid sessionId, PaymentMode mode, long expected, int? shares = null, long? amount = null,
        long tip = 0, string? promo = null) =>
        new(sessionId, Guid.NewGuid(), PaymentMethod.Card, mode, shares, amount, tip, promo, expected, true);

    private Task<PaymentDto> StartAsync(CreatePaymentRequest request) => _host.Payments.CreateAsync(request, default);

    private async Task<PaymentDto> PayAsync(CreatePaymentRequest request, PaymentStatus outcome = PaymentStatus.Succeeded)
    {
        var payment = await StartAsync(request);
        await WebhookAsync(payment, outcome);
        return await _host.Payments.GetAsync(request.SessionId, payment.Id, default);
    }

    private async Task WebhookAsync(PaymentDto payment, PaymentStatus status)
    {
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent(
            $"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", status, "Visa", "4242", status == PaymentStatus.Failed ? "Declined" : null));
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null; // webhooks carry no tenant
        try
        {
            await _host.Payments.HandleWebhookAsync("sandbox", body,
                new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
        }
        finally
        {
            _host.CurrentTenant = tenant;
        }
    }

    private Task<ReserveResultDto> HoldAsync(Guid sessionId, params BillUnitDto[] units) =>
        _host.Bills.ReserveAsync(sessionId, units.Select(u => new UnitRef(u.OrderLineId, u.UnitIndex)).ToList(), default);

    private static BillUnitDto Unit(BillDto bill, string name, int index = 0) =>
        bill.Units.Where(u => u.Name == name).ElementAt(index);

    /// <summary>The ledger never takes more than the bill, and paid + left always equals the total.</summary>
    private static void AssertBalanced(BillDto bill)
    {
        Assert.Equal(bill.TotalMinor, bill.PaidMinor + bill.RemainingMinor);
        Assert.InRange(bill.PaidMinor, 0, bill.TotalMinor);
        Assert.InRange(bill.ReservedMinor + bill.AvailableMinor, 0, bill.RemainingMinor);
    }

    // ---- Bill units ----

    [Fact]
    public async Task Each_item_carries_its_share_of_charges_and_the_items_add_up_to_the_bill()
    {
        var f = await CreateAsync(charges: true);
        await OrderAsync(f.Guests[0], (f.Burger, 2), (f.Coke, 2), (f.Soup, 1));

        var bill = await BillAsync(f.Guests[0]);

        Assert.Equal(15_950, bill.TotalMinor); // 145.00 + 10% service, VAT included
        Assert.Equal(5, bill.Units.Count);
        Assert.Equal(bill.TotalMinor, bill.Units.Sum(u => u.AmountMinor));
        Assert.Equal([5_500, 5_500, 1_375, 1_375, 2_200], bill.Units.Select(u => u.AmountMinor));
        Assert.All(bill.Units, u => Assert.Equal(BillUnitStatus.Open, u.Status));
    }

    // ---- Equal split ----

    [Fact]
    public async Task Equal_shares_round_down_and_the_last_share_absorbs_the_rounding()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2)); // 100.00
        var bill = await _host.Bills.SetSplitAsync(f.Guests[0], 3, default);
        Assert.Equal((3, 0, 3), (bill.Split.People, bill.Split.SharesTaken, bill.Split.SharesLeft));

        var first = await PayAsync(Pay(f.Guests[0], PaymentMode.Equal, 3_333, shares: 1));
        var second = await PayAsync(Pay(f.Guests[1], PaymentMode.Equal, 3_333, shares: 1));
        var mid = await BillAsync(f.Guests[2]);
        var last = await PayAsync(Pay(f.Guests[2], PaymentMode.Equal, 3_334, shares: 1));

        Assert.Equal((1, 2, 1, 3_334), (mid.Split.SharesLeft, mid.Split.SharesTaken, mid.Split.SharesLeft, mid.AvailableMinor));
        Assert.Equal([3_333L, 3_333L, 3_334L], new[] { first.BillAmountMinor, second.BillAmountMinor, last.BillAmountMinor });
        Assert.True(last.FullyPaid);
        Assert.Equal(CheckStatus.Closed, (await _host.Db.Checks.SingleAsync()).Status);
        AssertBalanced(await BillAsync(f.Guests[0]));
    }

    [Fact]
    public async Task A_guest_can_pay_for_several_people_and_tip_on_their_share()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2), (f.Soup, 1)); // 120.00
        await _host.Bills.SetSplitAsync(f.Guests[0], 4, default);

        var payment = await PayAsync(Pay(f.Guests[0], PaymentMode.Equal, 6_000, shares: 2, tip: 600, promo: "WELCOME10"));

        Assert.Equal((6_000, 600, 600, 6_000), (payment.BillAmountMinor, payment.DiscountMinor, payment.TipMinor, payment.AmountMinor));
        Assert.Equal((2, 4), (payment.Shares, payment.SplitPeople));
        var bill = await BillAsync(f.Guests[1]);
        Assert.Equal((6_000, 2, 6_000), (bill.RemainingMinor, bill.Split.SharesLeft, bill.AvailableMinor));
        AssertBalanced(bill);
    }

    [Fact]
    public async Task The_split_can_change_but_never_below_the_shares_already_taken()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2));
        await _host.Bills.SetSplitAsync(f.Guests[0], 4, default);
        await StartAsync(Pay(f.Guests[0], PaymentMode.Equal, 5_000, shares: 2)); // in progress

        var tooFew = await Assert.ThrowsAsync<AppException>(() => _host.Bills.SetSplitAsync(f.Guests[1], 2, default));
        var removed = await Assert.ThrowsAsync<AppException>(() => _host.Bills.SetSplitAsync(f.Guests[1], null, default));
        var five = await _host.Bills.SetSplitAsync(f.Guests[1], 5, default);

        Assert.Equal("invalid_split", tooFew.Code);
        Assert.Equal("invalid_split", removed.Code);
        Assert.Equal((5, 2, 3), (five.Split.People, five.Split.SharesTaken, five.Split.SharesLeft));
        Assert.Equal(5_000, five.ReservedMinor);
        Assert.Equal(1_666, BillAllocation.EqualShare(five.AvailableMinor, 3, 1));
    }

    [Fact]
    public async Task Removing_the_split_returns_to_the_full_bill()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2));
        await _host.Bills.SetSplitAsync(f.Guests[0], 4, default);

        var bill = await _host.Bills.SetSplitAsync(f.Guests[0], null, default);
        var full = await PayAsync(Pay(f.Guests[0], PaymentMode.Full, 10_000));

        Assert.Null(bill.Split.People);
        Assert.Equal(PaymentMode.Full, full.Mode);
        Assert.True(full.FullyPaid);
    }

    // ---- By item ----

    [Fact]
    public async Task Selected_items_are_held_for_one_guest_and_shown_as_taken_to_others()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2), (f.Coke, 1));
        var bill = await BillAsync(f.Guests[0]);

        var mine = await HoldAsync(f.Guests[0], Unit(bill, "Burger"), Unit(bill, "Coke"));
        var theirs = await HoldAsync(f.Guests[1], Unit(bill, "Burger", 0), Unit(bill, "Burger", 1));

        Assert.Empty(mine.Taken);
        Assert.Equal(6_250, mine.Bill.Split.SelectedMinor);
        var taken = Assert.Single(theirs.Taken);
        Assert.Equal(Unit(bill, "Burger").OrderLineId, taken.OrderLineId);
        Assert.Equal(0, taken.UnitIndex);

        var seenByOther = theirs.Bill;
        Assert.Equal((BillUnitStatus.Selected, false), (Unit(seenByOther, "Burger", 0).Status, Unit(seenByOther, "Burger", 0).Mine));
        Assert.Equal((BillUnitStatus.Selected, true), (Unit(seenByOther, "Burger", 1).Status, Unit(seenByOther, "Burger", 1).Mine));
        Assert.Equal(6_250, seenByOther.ReservedMinor);              // guest 0's items
        Assert.Equal(11_250 - 6_250, seenByOther.AvailableMinor);
        Assert.False(seenByOther.Split.EqualAvailable);               // the table is paying by item
        Assert.Contains(_host.PaymentNotifier.TableChanges, c => c.Reason == "items");
    }

    [Fact]
    public async Task Leaving_an_item_out_lets_it_go_and_holds_lapse_after_a_while()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 1), (f.Coke, 1));
        var bill = await BillAsync(f.Guests[0]);
        await HoldAsync(f.Guests[0], Unit(bill, "Burger"), Unit(bill, "Coke"));

        var dropped = await HoldAsync(f.Guests[0], Unit(bill, "Burger"));
        Assert.Equal(BillUnitStatus.Open, Unit(dropped.Bill, "Coke").Status);

        _host.Clock.Advance(ItemClaim.HoldLifetime + TimeSpan.FromSeconds(1));
        var later = await HoldAsync(f.Guests[1], Unit(bill, "Burger"));
        Assert.Empty(later.Taken);
        Assert.True(Unit(later.Bill, "Burger").Mine);
    }

    [Fact]
    public async Task Paying_by_item_marks_those_items_paid_and_leaves_the_rest()
    {
        var f = await CreateAsync(charges: true);
        await OrderAsync(f.Guests[0], (f.Burger, 2), (f.Coke, 2));
        var bill = await BillAsync(f.Guests[0]);
        await HoldAsync(f.Guests[0], Unit(bill, "Burger"), Unit(bill, "Coke"));

        var payment = await PayAsync(Pay(f.Guests[0], PaymentMode.Items, 6_875));

        Assert.Equal((PaymentStatus.Succeeded, 2, 6_875L), (payment.Status, payment.ItemCount, payment.BillAmountMinor));
        Assert.Equal((false, 6_875L), (payment.FullyPaid, payment.RemainingMinor));
        var after = await BillAsync(f.Guests[1]);
        Assert.Equal((BillUnitStatus.Paid, false), (Unit(after, "Burger", 0).Status, Unit(after, "Burger", 0).Mine));
        Assert.Equal(BillUnitStatus.Open, Unit(after, "Burger", 1).Status);
        AssertBalanced(after);

        // The second guest pays for the rest by item too, which settles the bill.
        await HoldAsync(f.Guests[1], Unit(after, "Burger", 1), Unit(after, "Coke", 1));
        var rest = await PayAsync(Pay(f.Guests[1], PaymentMode.Items, 6_875));
        Assert.True(rest.FullyPaid);
    }

    [Fact]
    public async Task A_failed_item_payment_gives_the_guest_their_items_back()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 1), (f.Coke, 1));
        var bill = await BillAsync(f.Guests[0]);
        await HoldAsync(f.Guests[0], Unit(bill, "Burger"));

        var paying = await StartAsync(Pay(f.Guests[0], PaymentMode.Items, 5_000));
        var duringPayment = await BillAsync(f.Guests[1]);
        await WebhookAsync(paying, PaymentStatus.Failed);
        var afterFailure = await BillAsync(f.Guests[0]);

        Assert.Equal((BillUnitStatus.Paying, false), (Unit(duringPayment, "Burger").Status, Unit(duringPayment, "Burger").Mine));
        Assert.Equal((BillUnitStatus.Selected, true), (Unit(afterFailure, "Burger").Status, Unit(afterFailure, "Burger").Mine));
        Assert.Equal(5_000, afterFailure.Split.SelectedMinor);
        Assert.Equal(PaymentStatus.Succeeded, (await PayAsync(Pay(f.Guests[0], PaymentMode.Items, 5_000))).Status);
    }

    [Fact]
    public async Task Paying_the_rest_leaves_other_guests_selected_items_for_them()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2), (f.Soup, 1)); // 120.00
        var bill = await BillAsync(f.Guests[0]);
        await HoldAsync(f.Guests[0], Unit(bill, "Soup"));

        var rest = await PayAsync(Pay(f.Guests[1], PaymentMode.Full, 10_000));
        var mine = await PayAsync(Pay(f.Guests[0], PaymentMode.Items, 2_000));

        Assert.Equal((false, true), (rest.FullyPaid, mine.FullyPaid));
        AssertBalanced(await BillAsync(f.Guests[2]));
    }

    [Fact]
    public async Task Splitting_by_item_and_by_amount_do_not_mix()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2));
        var bill = await BillAsync(f.Guests[0]);

        await HoldAsync(f.Guests[0], Unit(bill, "Burger"));
        var equal = await Assert.ThrowsAsync<AppException>(() => _host.Bills.SetSplitAsync(f.Guests[1], 2, default));
        await HoldAsync(f.Guests[0]); // let go

        await PayAsync(Pay(f.Guests[1], PaymentMode.Custom, 3_000, amount: 3_000));
        var items = await Assert.ThrowsAsync<AppException>(() => HoldAsync(f.Guests[0], Unit(bill, "Burger")));
        var afterCustom = await BillAsync(f.Guests[0]);

        Assert.Equal((409, "equal_unavailable"), (equal.StatusCode, equal.Code));
        Assert.Equal((409, "items_unavailable"), (items.StatusCode, items.Code));
        Assert.Equal((false, true), (afterCustom.Split.ItemsAvailable, afterCustom.Split.EqualAvailable));
    }

    // ---- Custom amount ----

    [Fact]
    public async Task A_custom_amount_can_be_anything_up_to_what_is_left()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2));

        var tooMuch = await Assert.ThrowsAsync<AppException>(() => StartAsync(Pay(f.Guests[0], PaymentMode.Custom, 10_001, amount: 10_001)));
        var part = await PayAsync(Pay(f.Guests[0], PaymentMode.Custom, 2_550, amount: 2_550));

        Assert.Equal("amount_too_high", tooMuch.Code);
        Assert.Equal(7_450, part.RemainingMinor);
        AssertBalanced(await BillAsync(f.Guests[1]));
    }

    // ---- Several guests at once ----

    [Fact]
    public async Task Two_guests_paying_the_rest_at_the_same_moment_cannot_both_pay_it()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2));
        using var other = _host.NewScope();

        // Guest 1's request runs in full between guest 0 reading the bill and saving its payment.
        PaymentDto? otherPayment = null;
        _host.SaveHook.BeforeNextSave = async () =>
            otherPayment = await other.Payments.CreateAsync(Pay(f.Guests[1], PaymentMode.Full, 10_000), default);

        var ex = await Assert.ThrowsAsync<AppException>(() => StartAsync(Pay(f.Guests[0], PaymentMode.Full, 10_000)));

        Assert.NotNull(otherPayment);
        Assert.Equal((409, "payment_in_progress"), (ex.StatusCode, ex.Code));
        Assert.Single(await _host.Db.Payments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Two_guests_taking_the_last_share_at_the_same_moment_cannot_both_take_it()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 2));
        await _host.Bills.SetSplitAsync(f.Guests[0], 2, default);
        await PayAsync(Pay(f.Guests[0], PaymentMode.Equal, 5_000, shares: 1));
        using var other = _host.NewScope();

        _host.SaveHook.BeforeNextSave = () => other.Payments.CreateAsync(Pay(f.Guests[2], PaymentMode.Equal, 5_000, shares: 1), default);
        var ex = await Assert.ThrowsAsync<AppException>(() => StartAsync(Pay(f.Guests[1], PaymentMode.Equal, 5_000, shares: 1)));

        Assert.Equal("bill_changed", ex.Code); // no shares left for guest 1
        Assert.Equal(2, await _host.Db.Payments.IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Two_guests_selecting_the_same_item_at_the_same_moment_cannot_both_hold_it()
    {
        var f = await CreateAsync();
        await OrderAsync(f.Guests[0], (f.Burger, 1), (f.Coke, 1));
        var bill = await BillAsync(f.Guests[0]);
        using var other = _host.NewScope();

        _host.SaveHook.BeforeNextSave = () =>
            other.Bills.ReserveAsync(f.Guests[1], [new UnitRef(Unit(bill, "Burger").OrderLineId, 0)], default);
        var result = await HoldAsync(f.Guests[0], Unit(bill, "Burger"), Unit(bill, "Coke"));

        Assert.Equal(Unit(bill, "Burger").OrderLineId, Assert.Single(result.Taken).OrderLineId);
        Assert.Equal((BillUnitStatus.Selected, false), (Unit(result.Bill, "Burger").Status, Unit(result.Bill, "Burger").Mine));
        Assert.True(Unit(result.Bill, "Coke").Mine);
        Assert.Equal(2, await _host.Db.ItemClaims.CountAsync());
    }

    // ---- Staff changes while guests split ----

    [Fact]
    public async Task Staff_cannot_reject_an_order_guests_have_paid_for_but_selected_items_are_let_go()
    {
        var f = await CreateAsync();
        var paidOrder = await OrderAsync(f.Guests[0], (f.Burger, 1));
        var otherOrder = await OrderAsync(f.Guests[1], (f.Coke, 1));
        var bill = await BillAsync(f.Guests[0]);
        await HoldAsync(f.Guests[0], Unit(bill, "Burger"));
        await PayAsync(Pay(f.Guests[0], PaymentMode.Items, 5_000));
        await HoldAsync(f.Guests[1], Unit(bill, "Coke"));

        var blocked = await Assert.ThrowsAsync<AppException>(() =>
            _host.MerchantOrders.ChangeStatusAsync(paidOrder.Id, new ChangeOrderStatusRequest(OrderStatus.Rejected, "Out of buns"), default));
        await _host.MerchantOrders.ChangeStatusAsync(otherOrder.Id, new ChangeOrderStatusRequest(OrderStatus.Rejected, "Out of cola"), default);

        Assert.Equal((409, "order_paid"), (blocked.StatusCode, blocked.Code));
        var after = await BillAsync(f.Guests[1]);
        Assert.DoesNotContain(after.Units, u => u.Name == "Coke");
        Assert.Equal(0, after.Split.SelectedMinor);
        Assert.Equal(BillStatus.Paid, after.Status);
    }

    // ---- Any mix of payments ----

    /// <summary>
    /// Blueprint 13: random split combinations must never let paid exceed the total, and paid +
    /// left must always equal the total. Guests pay in every mode, in any order, and some fail.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task Any_mix_of_split_payments_never_overpays(int seed)
    {
        var random = new Random(seed);
        var f = await CreateAsync(charges: seed % 2 == 0);
        await OrderAsync(f.Guests[0], (f.Burger, random.Next(1, 4)), (f.Coke, random.Next(1, 5)), (f.Soup, random.Next(0, 3) + 1));

        for (var step = 0; step < 12; step++)
        {
            var guest = f.Guests[random.Next(f.Guests.Count)];
            var bill = await BillAsync(guest);
            AssertBalanced(bill);
            if (bill.Status != BillStatus.Open) break;

            var outcome = random.Next(5) == 0 ? PaymentStatus.Failed : PaymentStatus.Succeeded;
            try
            {
                switch (random.Next(4))
                {
                    case 0 when bill.AvailableMinor > 0:
                        var amount = Math.Min(bill.AvailableMinor, 1 + random.Next(4_000));
                        await PayAsync(Pay(guest, PaymentMode.Custom, amount, amount: amount), outcome);
                        break;
                    case 1 when bill.Split.EqualAvailable:
                        if (bill.Split.People is null) bill = await _host.Bills.SetSplitAsync(guest, random.Next(2, 5), default);
                        if (bill.Split.SharesLeft is int left && left > 0)
                        {
                            var shares = random.Next(1, left + 1);
                            await PayAsync(Pay(guest, PaymentMode.Equal, BillAllocation.EqualShare(bill.AvailableMinor, left, shares), shares: shares), outcome);
                        }
                        break;
                    case 2 when bill.Split.ItemsAvailable:
                        var open = bill.Units.Where(u => u.Status == BillUnitStatus.Open).OrderBy(_ => random.Next()).Take(random.Next(1, 3)).ToArray();
                        if (open.Length == 0) break;
                        var held = await HoldAsync(guest, open);
                        if (held.Bill.Split.SelectedMinor > 0)
                            await PayAsync(Pay(guest, PaymentMode.Items, held.Bill.Split.SelectedMinor), outcome);
                        break;
                    default:
                        if (bill.AvailableMinor > 0) await PayAsync(Pay(guest, PaymentMode.Full, bill.AvailableMinor), outcome);
                        break;
                }
            }
            catch (AppException ex) when (ex.StatusCode == 409)
            {
                // A rule said no (e.g. modes don't mix): fine, as long as the books still balance.
            }
        }

        var final = await BillAsync(f.Guests[0]);
        AssertBalanced(final);
        var ledger = await _host.Db.LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.Type == LedgerEntryType.BillPayment || e.Type == LedgerEntryType.Discount).SumAsync(e => e.AmountMinor);
        Assert.Equal(final.PaidMinor, ledger);
        Assert.True(ledger <= final.TotalMinor);
    }
}
