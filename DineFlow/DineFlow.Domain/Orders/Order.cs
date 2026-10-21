using DineFlow.Domain.Common;

namespace DineFlow.Domain.Orders;

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

        var price = OrderPricing.Calculate(lines.Sum(l => l.LineTotalMinor), serviceChargeBasisPoints, taxBasisPoints, taxIncluded);
        SubtotalMinor = price.SubtotalMinor;
        ServiceChargeMinor = price.ServiceChargeMinor;
        ServiceChargeBasisPoints = serviceChargeBasisPoints;
        TaxMinor = price.TaxMinor;
        TaxBasisPoints = taxBasisPoints;
        TaxIncluded = price.TaxIncluded;
        TotalMinor = price.TotalMinor;
    }

    /// <summary>Adds the order to the table's open bill.</summary>
    public void AssignToCheck(Guid checkId) => CheckId = checkId;

    /// <summary>
    /// Moves the order along the kitchen flow. Allowed: Submitted → Accepted or Rejected;
    /// Accepted → Preparing, Ready or Completed; Preparing → Ready or Completed; Ready → Completed.
    /// </summary>
    public void TransitionTo(OrderStatus next, string? reason, DateTimeOffset now, Guid? staffUserId = null)
    {
        if (!CanTransitionTo(next))
            throw new DomainException($"An order that is {Status} can't become {next}.");

        if (next == OrderStatus.Rejected)
            RejectReason = Text.Required(reason, 200, "A reason for the guest");

        Status = next;
        UpdatedAt = now;
        if (next == OrderStatus.Accepted) AcceptedByStaffId = staffUserId;
        if (next == OrderStatus.Completed) ServedByStaffId = staffUserId;
    }

    public bool CanTransitionTo(OrderStatus next) => (Status, next) switch
    {
        (OrderStatus.Submitted, OrderStatus.Accepted or OrderStatus.Rejected) => true,
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
    Rejected = 5
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
}

/// <summary>A chosen option on an order line, e.g. Doneness: Medium well (+0).</summary>
public record OrderLineModifier(
    Guid OptionId, string GroupName, string? GroupNameAr, string Name, string? NameAr, long PriceDeltaMinor);
