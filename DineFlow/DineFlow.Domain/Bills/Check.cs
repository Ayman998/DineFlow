using DineFlow.Domain.Common;

namespace DineFlow.Domain.Bills;

/// <summary>
/// The open bill (tab) at one service location (blueprint "Bill/Check"). Every order placed at
/// the table joins the open check; payments settle it; once nothing is left to pay it closes
/// and the next order opens a new one. In standalone mode DineFlow is the bill's source of truth;
/// with a POS connector this becomes a mirror of the POS check.
/// </summary>
public class Check : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public CheckStatus Status { get; private set; } = CheckStatus.Open;
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset? ClosedAt { get; private set; }

    private Check() { }

    public Check(Guid tenantId, Guid venueId, Guid serviceLocationId, string currency, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        ServiceLocationId = serviceLocationId;
        Currency = currency;
        CreatedAt = now;
    }

    public void Close(DateTimeOffset now)
    {
        if (Status == CheckStatus.Closed) return;
        Status = CheckStatus.Closed;
        ClosedAt = now;
    }
}

public enum CheckStatus
{
    Open = 0,
    Closed = 1
}
