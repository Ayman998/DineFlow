using HelloZen.Application.Abstractions;
using HelloZen.Application.Audit;
using HelloZen.Application.Bills;
using HelloZen.Application.Common;
using HelloZen.Application.Dashboard;
using HelloZen.Application.Menus;
using HelloZen.Application.Orders;
using HelloZen.Application.Payments;
using HelloZen.Application.Pos;
using HelloZen.Application.Tables;
using HelloZen.Application.Venues;
using HelloZen.Domain.Orders;
using HelloZen.Domain.Payments;
using HelloZen.Domain.Staff;
using HelloZen.Domain.Venues;
using HelloZen.Infrastructure.Payments;

namespace HelloZen.Test.Application;

/// <summary>Staff moving a table's bill, or some of its items, to another table.</summary>
public class TableTransferTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private sealed record Table(LocationDto Location, string QrToken)
    {
        public Guid Id => Location.Id;
    }

    private sealed record Fixture(Guid TenantId, VenueDto Venue, StaffUser Waiter, ItemDto Burger, ItemDto Coke, ItemDto Soup,
        Table Seven, Table Eight, Table Nine);

    /// <summary>Tables 7, 8 and 9. With charges, the venue adds 10% service (VAT included in prices).</summary>
    private async Task<Fixture> CreateAsync(bool charges = false)
    {
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        await _host.Venues.UpdateSettingsAsync(venue.Id, new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38",
            true, true, ServiceChargeBasisPoints: charges ? 1000 : 0, TaxBasisPoints: charges ? 500 : 0), default);

        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(venue.Id, null, "Mains", null), default);
        ItemDto Item(string name, long price) => _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, name, null, null, null, price, null, true, [], [], []), default).Result;
        var burger = Item("Burger", 5000);
        var coke = Item("Coke", 1250);
        var soup = Item("Soup", 2000);
        await _host.Menus.SetMenuItemsAsync(menu.Id, new MenuItemsRequest([burger.Id, coke.Id, soup.Id]), default);

        async Task<Table> TableAsync(string label)
        {
            var location = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, label), default);
            return new Table(location, TestHost.TokenFromUrl(location.Qr!.Url));
        }

        var waiter = new StaffUser(tenant.Id, "sami@demo.test", "Sami", StaffRole.Manager);
        _host.Db.StaffUsers.Add(waiter);
        await _host.Db.SaveChangesAsync();

        return new Fixture(tenant.Id, venue, waiter, burger, coke, soup, await TableAsync("7"), await TableAsync("8"), await TableAsync("9"));
    }

    private async Task<Guid> GuestAtAsync(Fixture f, Table table)
    {
        _host.CurrentTenant = null;
        var session = await _host.Guests.ResolveQrAsync(table.QrToken, null, default);
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

    private Task<TransferResultDto> MoveAsync(Fixture f, Table from, Table to, params (TableBillLineDto Line, int Quantity)[] lines) =>
        _host.Transfers.TransferAsync(from.Id,
            new TransferRequest(to.Id, lines.Length == 0 ? null : lines.Select(l => new TransferLineRequest(l.Line.OrderLineId, l.Quantity)).ToList()),
            f.Waiter.Id, default);

    private async Task<TableBillLineDto> LineAsync(Table table, string name) =>
        (await _host.Transfers.GetBillAsync(table.Id, default)).Lines.Single(l => l.Name == name);

    private async Task<TableStatusDto> StatusAsync(Fixture f, Table table) =>
        (await _host.TableStatus.ListAsync(f.Venue.Id, default)).Single(t => t.LocationId == table.Id);

    private static CreatePaymentRequest Pay(Guid sessionId, PaymentMode mode, long expected, long? amount = null) =>
        new(sessionId, Guid.NewGuid(), PaymentMethod.Card, mode, null, amount, 0, null, expected, true);

    private async Task<PaymentDto> PayAsync(CreatePaymentRequest request)
    {
        var payment = await _host.Payments.CreateAsync(request, default);
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent(
            $"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", PaymentStatus.Succeeded, "Visa", "4242", null));
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
        return await _host.Payments.GetAsync(request.SessionId, payment.Id, default);
    }

    // ---- Whole bill ----

    [Fact]
    public async Task Whole_bill_moves_to_a_free_table_with_what_has_been_paid()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        var order = await OrderAsync(atSeven, (f.Burger, 1), (f.Coke, 2));
        await PayAsync(Pay(atSeven, PaymentMode.Custom, 2_000, amount: 2_000));
        var checkId = (await BillAsync(atSeven)).CheckId;

        var result = await MoveAsync(f, f.Seven, f.Eight);

        Assert.Equal((true, 3), (result.WholeBill, result.Items));
        Assert.Equal([order.Number], result.OrderNumbers);
        var atEight = await BillAsync(await GuestAtAsync(f, f.Eight));
        Assert.Equal((checkId, "8"), (atEight.CheckId, atEight.LocationLabel));
        Assert.Equal((7_500L, 2_000L, 5_500L), (atEight.TotalMinor, atEight.PaidMinor, atEight.RemainingMinor));
        Assert.Equal(BillStatus.NoOrders, (await BillAsync(atSeven)).Status);

        // The kitchen serves it to table 8, and the live view shows the bill there.
        var moved = await _host.MerchantOrders.GetAsync(order.Id, default);
        Assert.Equal((f.Eight.Id, "8"), (moved.ServiceLocationId, moved.LocationLabel));
        Assert.Contains(_host.Notifier.Events, e => e.Event == "changed" && e.Order.Id == order.Id && e.Order.LocationLabel == "8");
        Assert.Equal(TableState.PartiallyPaid, (await StatusAsync(f, f.Eight)).State);
        Assert.Null((await StatusAsync(f, f.Seven)).CheckId);
        Assert.Contains((f.Seven.Id, "transfer"), _host.PaymentNotifier.TableChanges);
        Assert.Contains((f.Eight.Id, "transfer"), _host.PaymentNotifier.TableChanges);

        var audit = Assert.Single((await _host.AuditLog.ListAsync(new AuditQuery(Area: "table"), default)).Items);
        Assert.Equal(("table.bill_moved", "Sami"), (audit.Action, audit.StaffName));
    }

    [Fact]
    public async Task Whole_bill_joins_the_bill_of_a_table_that_already_ordered()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        var atEight = await GuestAtAsync(f, f.Eight);
        await OrderAsync(atSeven, (f.Burger, 1));
        await OrderAsync(atEight, (f.Coke, 1));

        var result = await MoveAsync(f, f.Seven, f.Eight);

        Assert.True(result.WholeBill);
        var eight = await BillAsync(atEight);
        Assert.Equal((2, 6_250L), (eight.Orders.Count, eight.TotalMinor));
        Assert.Equal(BillStatus.NoOrders, (await BillAsync(atSeven)).Status);
        Assert.Null((await StatusAsync(f, f.Seven)).CheckId);

        // Table 7's next order starts a fresh bill there.
        await OrderAsync(atSeven, (f.Soup, 1));
        Assert.Equal(2_000, (await BillAsync(atSeven)).TotalMinor);
    }

    // ---- Some items ----

    [Fact]
    public async Task Part_of_an_order_is_split_off_with_the_same_status_and_both_are_repriced()
    {
        var f = await CreateAsync(charges: true);
        var atSeven = await GuestAtAsync(f, f.Seven);
        var order = await OrderAsync(atSeven, (f.Burger, 1), (f.Coke, 2), (f.Soup, 1));
        await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted, null), default);
        Assert.Equal(10_450, order.TotalMinor); // 95.00 + 10% service

        var result = await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Coke"), 1), (await LineAsync(f.Seven, "Soup"), 1));

        Assert.Equal((false, 2), (result.WholeBill, result.Items));
        var split = Assert.Single(result.OrderNumbers);
        Assert.NotEqual(order.Number, split);

        var kept = await _host.MerchantOrders.GetAsync(order.Id, default);
        Assert.Equal([("Burger", 1), ("Coke", 1)], kept.Lines.Select(l => (l.Name, l.Quantity)));
        Assert.Equal(("7", 6_875L), (kept.LocationLabel, kept.TotalMinor));

        var atEight = await BillAsync(await GuestAtAsync(f, f.Eight));
        var moved = Assert.Single(atEight.Orders);
        Assert.Equal((split, OrderStatus.Accepted, 3_575L), (moved.Number, moved.Status, moved.TotalMinor));
        Assert.Equal([("Coke", 1), ("Soup", 1)], moved.Lines.Select(l => (l.Name, l.Quantity)));
        Assert.Equal(order.TotalMinor, kept.TotalMinor + moved.TotalMinor);
        Assert.Equal(6_875, (await BillAsync(atSeven)).TotalMinor);

        // The guest who ordered still follows both orders.
        Assert.Equal(2, (await _host.GuestOrders.ListAsync(atSeven, default)).Count);
        var audit = Assert.Single((await _host.AuditLog.ListAsync(new AuditQuery(Area: "table"), default)).Items);
        Assert.Equal("table.items_moved", audit.Action);
    }

    [Fact]
    public async Task Moving_every_item_of_an_order_moves_the_order_itself()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        var first = await OrderAsync(atSeven, (f.Burger, 1));
        var second = await OrderAsync(atSeven, (f.Coke, 2));

        var result = await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Coke"), 2));

        Assert.False(result.WholeBill);
        Assert.Equal([second.Number], result.OrderNumbers);
        Assert.Equal("8", (await _host.MerchantOrders.GetAsync(second.Id, default)).LocationLabel);
        Assert.Equal("7", (await _host.MerchantOrders.GetAsync(first.Id, default)).LocationLabel);
        Assert.Equal(2_500, (await BillAsync(await GuestAtAsync(f, f.Eight))).TotalMinor);
    }

    // ---- Money already paid ----

    [Fact]
    public async Task What_a_table_has_paid_stays_covered_by_what_is_left_on_its_bill()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        await OrderAsync(atSeven, (f.Burger, 1), (f.Coke, 2));
        await OrderAsync(await GuestAtAsync(f, f.Eight), (f.Soup, 1));
        await PayAsync(Pay(atSeven, PaymentMode.Custom, 6_000, amount: 6_000));

        var burger = await Assert.ThrowsAsync<AppException>(async () => await MoveAsync(f, f.Seven, f.Nine, (await LineAsync(f.Seven, "Burger"), 1)));
        var all = await Assert.ThrowsAsync<AppException>(() => MoveAsync(f, f.Seven, f.Eight)); // 8 has its own bill
        Assert.Equal(("paid_more_than_left", "paid_more_than_left"), (burger.Code, all.Code));

        await MoveAsync(f, f.Seven, f.Nine, (await LineAsync(f.Seven, "Coke"), 1));
        var seven = await BillAsync(atSeven);
        Assert.Equal((6_250L, 6_000L, 250L), (seven.TotalMinor, seven.PaidMinor, seven.RemainingMinor));
    }

    [Fact]
    public async Task Moving_the_unpaid_rest_away_settles_the_bill_that_stays()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        await OrderAsync(atSeven, (f.Burger, 1), (f.Coke, 2));
        await PayAsync(Pay(atSeven, PaymentMode.Custom, 6_250, amount: 6_250));

        await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Coke"), 1));

        Assert.Equal(BillStatus.Paid, (await BillAsync(atSeven)).Status);
        Assert.Equal(TableState.Paid, (await StatusAsync(f, f.Seven)).State);
        Assert.Equal(TableState.Unpaid, (await StatusAsync(f, f.Eight)).State);
    }

    [Fact]
    public async Task Items_paid_by_item_stay_but_the_whole_bill_can_still_move_to_a_free_table()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        await OrderAsync(atSeven, (f.Burger, 2), (f.Soup, 1), (f.Coke, 1));
        var bill = await BillAsync(atSeven);
        await _host.Bills.ReserveAsync(atSeven, [new UnitRef(bill.Units.First(u => u.Name == "Burger").OrderLineId, 0)], default);
        await PayAsync(Pay(atSeven, PaymentMode.Items, 5_000));

        Assert.Equal(0, (await LineAsync(f.Seven, "Burger")).MovableQuantity);
        Assert.Equal(1, (await LineAsync(f.Seven, "Coke")).MovableQuantity);
        var paid = await Assert.ThrowsAsync<AppException>(async () => await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Burger"), 1)));
        Assert.Equal("items_paid", paid.Code);

        await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Coke"), 1));
        await MoveAsync(f, f.Seven, f.Nine);

        var nine = await BillAsync(await GuestAtAsync(f, f.Nine));
        Assert.Equal((12_000L, 5_000L), (nine.TotalMinor, nine.PaidMinor));
        Assert.Single(nine.Units, u => u.Status == BillUnitStatus.Paid);
    }

    [Fact]
    public async Task Nothing_moves_while_a_guest_is_paying()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        await OrderAsync(atSeven, (f.Burger, 1));
        await _host.Payments.CreateAsync(Pay(atSeven, PaymentMode.Full, 5_000), default);

        var busy = await Assert.ThrowsAsync<AppException>(() => MoveAsync(f, f.Seven, f.Eight));

        Assert.Equal("payment_in_progress", busy.Code);
        Assert.Equal(5_000, (await BillAsync(atSeven)).TotalMinor);
    }

    [Fact]
    public async Task Items_guests_were_choosing_on_a_changed_order_are_let_go()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        await OrderAsync(atSeven, (f.Burger, 1), (f.Coke, 2));
        var bill = await BillAsync(atSeven);
        await _host.Bills.ReserveAsync(atSeven, [new UnitRef(bill.Units.First(u => u.Name == "Burger").OrderLineId, 0)], default);

        await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Coke"), 1));

        var after = await BillAsync(atSeven);
        Assert.All(after.Units, u => Assert.Equal(BillUnitStatus.Open, u.Status));
        Assert.Equal(0, after.Split.SelectedMinor);
    }

    // ---- Refusals ----

    [Fact]
    public async Task Refuses_the_same_table_more_than_was_ordered_and_venues_with_a_POS()
    {
        var f = await CreateAsync();
        var atSeven = await GuestAtAsync(f, f.Seven);
        await OrderAsync(atSeven, (f.Coke, 2));

        var same = await Assert.ThrowsAsync<AppException>(() => MoveAsync(f, f.Seven, f.Seven));
        var tooMany = await Assert.ThrowsAsync<AppException>(async () => await MoveAsync(f, f.Seven, f.Eight, (await LineAsync(f.Seven, "Coke"), 3)));
        var empty = await Assert.ThrowsAsync<AppException>(() => MoveAsync(f, f.Nine, f.Eight));
        await _host.Pos.SaveConnectionAsync(f.Venue.Id, new PosConnectionRequest("simulator", null, null, true, true), f.Waiter.Id, default);
        var pos = await Assert.ThrowsAsync<AppException>(() => MoveAsync(f, f.Seven, f.Eight));

        Assert.Equal(["same_table", "items_changed", "nothing_to_move", "pos_bills"], [same.Code, tooMany.Code, empty.Code, pos.Code]);
        Assert.Equal(2_500, (await BillAsync(atSeven)).TotalMinor);
    }
}
