using HelloZen.Domain.Common;

namespace HelloZen.Domain.Orders;

/// <summary>
/// A round of food and drink ordered from a table session (blueprint 3.4). Lines are snapshots
/// of names and prices at the time of ordering, so later menu edits never change an order.
/// A session can place several orders.
/// </summary>
public class Order : Entity, ITenantOwned
{
    public const int FirstNumber = 1001;
    public const int MaxLines = 50;

    private readonly List<OrderLine> _lines = [];

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public Guid GuestSessionId { get; private set; }
    /// <summary>The table's bill this order belongs to; null for orders placed before bills existed.</summary>
    public Guid? CheckId { get; private set; }
    /// <summary>The guest app's id for this submission; makes retries safe (blueprint 7.3).</summary>
    public Guid ClientRequestId { get; private set; }
    /// <summary>Short number staff and guests use, e.g. 1024. Unique per venue.</summary>
    public int Number { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Submitted;

    /// <summary>Location snapshot, e.g. Table / "12" / "Main hall".</summary>
    public string LocationType { get; private set; } = string.Empty;
    public string LocationLabel { get; private set; } = string.Empty;
    public string? AreaName { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public long SubtotalMinor { get; private set; }
    public long ServiceChargeMinor { get; private set; }
    public int ServiceChargeBasisPoints { get; private set; }
    public long TaxMinor { get; private set; }
    public int TaxBasisPoints { get; private set; }
    public bool TaxIncluded { get; private set; }
    public long TotalMinor { get; private set; }

    /// <summary>"Special request for the kitchen" for the whole order.</summary>
    public string? Note { get; private set; }
    public string? RejectReason { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    /// <summary>The staff member who accepted the order, and the one who marked it served (for staff ratings).</summary>
    public Guid? AcceptedByStaffId { get; private set; }
    public Guid? ServedByStaffId { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    private Order() { }

    public Order(
        Guid tenantId, Guid venueId, Guid serviceLocationId, Guid guestSessionId, Guid clientRequestId, int number,
        string locationType, string locationLabel, string? areaName,
        string currency, int serviceChargeBasisPoints, int taxBasisPoints, bool taxIncluded,
        IReadOnlyList<OrderLine> lines, string? note, DateTimeOffset now)
    {
        if (lines.Count == 0) throw new DomainException("An order needs at least one item.");
        if (lines.Count > MaxLines) throw new DomainException($"An order can have at most {MaxLines} lines.");

        TenantId = tenantId;
        VenueId = venueId;
        ServiceLocationId = serviceLocationId;
        GuestSessionId = guestSessionId;
        ClientRequestId = clientRequestId;
        Number = number;
        LocationType = locationType;
        LocationLabel = locationLabel;
        AreaName = areaName;
        Currency = currency;
        Note = Text.Optional(note, 200, "Special request");
        CreatedAt = now;
        UpdatedAt = now;

        for (var i = 0; i < lines.Count; i++) lines[i].AttachTo(this, i);
        _lines.AddRange(lines);

        ServiceChargeBasisPoints = serviceChargeBasisPoints;
        TaxBasisPoints = taxBasisPoints;
        TaxIncluded = taxIncluded;
        Reprice();
    }

    /// <summary>Adds the order to the table's open bill.</summary>
    public void AssignToCheck(Guid checkId) => CheckId = checkId;

    /// <summary>
    /// Moves the order to another table's bill, e.g. when the guests change tables. It keeps its
    /// number and kitchen status; the location snapshot follows it, so staff serve the right table.
    /// </summary>
    public void MoveTo(Guid serviceLocationId, string locationType, string locationLabel, string? areaName, Guid checkId, DateTimeOffset now)
    {
        ServiceLocationId = serviceLocationId;
        LocationType = locationType;
        LocationLabel = locationLabel;
        AreaName = areaName;
        CheckId = checkId;
        UpdatedAt = now;
    }

    /// <summary>
    /// Takes some of the items off this order onto a new one at the same table, ready to be moved,
    /// e.g. when one guest takes their drinks to the bar. <paramref name="quantities"/> says how
    /// many of each line (by line id) go. The new order keeps this one's guest, status, charges
    /// and time, and both orders are repriced. Moving every item is <see cref="MoveTo"/> instead.
    /// </summary>
    public Order SplitOff(IReadOnlyDictionary<Guid, int> quantities, int number, DateTimeOffset now)
    {
        var taking = _lines.Where(l => quantities.ContainsKey(l.Id)).OrderBy(l => l.Position).ToList();
        if (taking.Count == 0 || taking.Count != quantities.Count) throw new DomainException("Pick items from this order to move.");
        if (taking.Any(l => quantities[l.Id] < 1 || quantities[l.Id] > l.Quantity))
            throw new DomainException("You can't move more of an item than was ordered.");
        if (taking.Count == _lines.Count && taking.All(l => quantities[l.Id] == l.Quantity))
            throw new DomainException("Move the whole order instead of splitting it.");

        var moved = new List<OrderLine>();
        foreach (var line in taking)
        {
            var quantity = quantities[line.Id];
            moved.Add(line.CopyWith(quantity));
            if (quantity == line.Quantity) _lines.Remove(line);
            else line.Reduce(quantity);
        }
        Reprice();
        UpdatedAt = now;

        return new Order(TenantId, VenueId, ServiceLocationId, GuestSessionId, Guid.NewGuid(), number,
            LocationType, LocationLabel, AreaName, Currency, ServiceChargeBasisPoints, TaxBasisPoints, TaxIncluded, moved, Note, now)
        {
            CheckId = CheckId,
            Status = Status,
            RejectReason = RejectReason,
            AcceptedByStaffId = AcceptedByStaffId,
            ServedByStaffId = ServedByStaffId,
            CreatedAt = CreatedAt
        };
    }

    private void Reprice()
    {
        var price = OrderPricing.Calculate(_lines.Sum(l => l.LineTotalMinor), ServiceChargeBasisPoints, TaxBasisPoints, TaxIncluded);
        SubtotalMinor = price.SubtotalMinor;
        ServiceChargeMinor = price.ServiceChargeMinor;
        TaxMinor = price.TaxMinor;
        TotalMinor = price.TotalMinor;
    }

    /// <summary>
    /// Moves the order along the kitchen flow. The portal uses New → Ready → Served
    /// (Submitted → Ready → Completed) or Submitted → Rejected. The longer Submitted → Accepted →
    /// Preparing → Ready → Completed flow is still allowed, so orders already accepted can finish.
    /// </summary>
    public void TransitionTo(OrderStatus next, string? reason, DateTimeOffset now, Guid? staffUserId = null)
    {
        if (!CanTransitionTo(next))
            throw new DomainException($"An order that is {Status} can't become {next}.");

        if (next == OrderStatus.Rejected)
            RejectReason = Text.Required(reason, 200, "A reason for the guest");

        // Whoever takes a new order on (accepting it, or marking it ready) accepted it.
        if (Status == OrderStatus.Submitted && next is OrderStatus.Accepted or OrderStatus.Ready) AcceptedByStaffId = staffUserId;
        if (next == OrderStatus.Completed) ServedByStaffId = staffUserId;
        Status = next;
        UpdatedAt = now;
    }

    /// <summary>
    /// The table's bill is paid in full: a served order is done with and leaves the Served list.
    /// An order the kitchen is still working on stays where it is, and closes once it's served.
    /// </summary>
    /// <returns>Whether the order closed.</returns>
    public bool Close(DateTimeOffset now)
    {
        if (Status != OrderStatus.Completed) return false;
        Status = OrderStatus.Closed;
        UpdatedAt = now;
        return true;
    }

    public bool CanTransitionTo(OrderStatus next) => (Status, next) switch
    {
        (OrderStatus.Submitted, OrderStatus.Accepted or OrderStatus.Ready or OrderStatus.Rejected) => true,
        (OrderStatus.Accepted, OrderStatus.Preparing or OrderStatus.Ready or OrderStatus.Completed) => true,
        (OrderStatus.Preparing, OrderStatus.Ready or OrderStatus.Completed) => true,
        (OrderStatus.Ready, OrderStatus.Completed) => true,
        _ => false
    };
}

public enum OrderStatus
{
    /// <summary>Placed by the guest; waiting for staff ("New").</summary>
    Submitted = 0,
    Accepted = 1,
    Preparing = 2,
    Ready = 3,
    /// <summary>Served to the table.</summary>
    Completed = 4,
    Rejected = 5,
    /// <summary>Served, and the table's bill is paid in full. Only <see cref="Order.Close"/> gets an order here.</summary>
    Closed = 6
}

/// <summary>One item on an order, with its chosen options, priced when the order was placed.</summary>
public class OrderLine : Entity, ITenantOwned
{
    public const int MaxQuantity = 99;

    public Guid TenantId { get; private set; }
    public Guid OrderId { get; private set; }
    /// <summary>Where the line sits on the order, 0 first. Lines are always shown and split in this order.</summary>
    public int Position { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public int Quantity { get; private set; }
    /// <summary>Item price plus chosen options, per unit.</summary>
    public long UnitPriceMinor { get; private set; }
    public long LineTotalMinor { get; private set; }
    public string? Note { get; private set; }
    public List<OrderLineModifier> Modifiers { get; private set; } = [];

    private OrderLine() { }

    public OrderLine(Guid tenantId, Guid menuItemId, string name, string? nameAr, long basePriceMinor,
        int quantity, IReadOnlyList<OrderLineModifier> modifiers, string? note)
    {
        if (quantity is < 1 or > MaxQuantity) throw new DomainException($"Quantity must be between 1 and {MaxQuantity}.");

        TenantId = tenantId;
        MenuItemId = menuItemId;
        Name = name;
        NameAr = nameAr;
        Quantity = quantity;
        Modifiers = [.. modifiers];
        Note = Text.Optional(note, 200, "Item note");
        UnitPriceMinor = basePriceMinor + modifiers.Sum(m => m.PriceDeltaMinor);
        LineTotalMinor = checked(UnitPriceMinor * quantity);
    }

    internal void AttachTo(Order order, int position)
    {
        OrderId = order.Id;
        Position = position;
    }

    /// <summary>The same item, options and price, <paramref name="quantity"/> of them, for another order.</summary>
    internal OrderLine CopyWith(int quantity) =>
        new(TenantId, MenuItemId, Name, NameAr, UnitPriceMinor - Modifiers.Sum(m => m.PriceDeltaMinor), quantity,
            Modifiers.Select(m => m with { }).ToList(), Note);

    /// <summary>Some of this line went to another order.</summary>
    internal void Reduce(int quantity)
    {
        Quantity -= quantity;
        LineTotalMinor = checked(UnitPriceMinor * Quantity);
    }
}

/// <summary>A chosen option on an order line, e.g. Doneness: Medium well (+0).</summary>
public record OrderLineModifier(
    Guid OptionId, string GroupName, string? GroupNameAr, string Name, string? NameAr, long PriceDeltaMinor);
