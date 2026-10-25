using DineFlow.Domain.Common;

namespace DineFlow.Domain.Pos;

/// <summary>
/// One thing DineFlow has to tell the POS: send an order, post a payment, or close a check
/// (blueprint E9, 7.1 POSTING_TO_POS / POSTING_PENDING / POSTED). Commands are written when the
/// order is accepted or the payment confirmed, and a background worker carries them out in order
/// for each bill. The guest's payment never depends on this: if the POS is down, the command waits
/// and is retried, and nobody is charged again.
/// </summary>
/// <remarks>
/// <see cref="Attempts"/> is checked on save, so two workers can't send the same command.
/// <see cref="Uncertain"/> marks an attempt whose result is unknown (e.g. a timeout after the POS
/// may have applied it); the next attempt first asks the POS whether it already has it.
/// </remarks>
public class PosCommand : Entity, ITenantOwned
{
    public const int MaxErrorLength = 500;
    /// <summary>A command still waiting after this long is flagged for staff.</summary>
    public static readonly TimeSpan AttentionAfter = TimeSpan.FromMinutes(15);

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public PosCommandKind Kind { get; private set; }
    public Guid CheckId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public string Provider { get; private set; } = string.Empty;

    public PosCommandStatus Status { get; private set; } = PosCommandStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public bool Uncertain { get; private set; }
    public string? LastError { get; private set; }
    /// <summary>What the POS called it: the order or check id, or the payment/tender id.</summary>
    public string? ResultRef { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private PosCommand() { }

    private PosCommand(Guid tenantId, Guid venueId, PosCommandKind kind, Guid checkId, Guid? orderId, Guid? paymentId, string provider, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        Kind = kind;
        CheckId = checkId;
        OrderId = orderId;
        PaymentId = paymentId;
        Provider = provider;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public static PosCommand SendOrder(Guid tenantId, Guid venueId, Guid checkId, Guid orderId, string provider, DateTimeOffset now) =>
        new(tenantId, venueId, PosCommandKind.SendOrder, checkId, orderId, null, provider, now);

    public static PosCommand PostPayment(Guid tenantId, Guid venueId, Guid checkId, Guid paymentId, string provider, DateTimeOffset now) =>
        new(tenantId, venueId, PosCommandKind.PostPayment, checkId, null, paymentId, provider, now);

    public static PosCommand CloseCheck(Guid tenantId, Guid venueId, Guid checkId, string provider, DateTimeOffset now) =>
        new(tenantId, venueId, PosCommandKind.CloseCheck, checkId, null, null, provider, now);

    public bool IsDue(DateTimeOffset now) => Status == PosCommandStatus.Pending && NextAttemptAt <= now;

    /// <summary>Waiting long enough that staff should know (blueprint "payment succeeds but POS is offline for an extended period").</summary>
    public bool NeedsAttention(DateTimeOffset now) =>
        Status == PosCommandStatus.Failed || (Status == PosCommandStatus.Pending && now - CreatedAt >= AttentionAfter);

    /// <summary>Counts an attempt before calling the POS; saving it is what claims the command for this worker.</summary>
    public void StartAttempt(DateTimeOffset now)
    {
        if (!IsDue(now)) throw new DomainException("This command isn't waiting to be sent.");
        Attempts++;
        LastAttemptAt = now;
    }

    public void Succeeded(string? resultRef, DateTimeOffset now)
    {
        Status = PosCommandStatus.Done;
        ResultRef = resultRef;
        CompletedAt = now;
        Uncertain = false;
        LastError = null;
    }

    /// <summary>The POS couldn't be reached or didn't answer: try again later, for as long as it takes.</summary>
    /// <param name="uncertain">The POS may have applied it anyway (e.g. a timeout); check before retrying.</param>
    public void Unavailable(string error, bool uncertain, DateTimeOffset now)
    {
        LastError = Trim(error);
        Uncertain = Uncertain || uncertain;
        NextAttemptAt = now + Backoff(Attempts);
    }

    /// <summary>The POS refused it (e.g. a table that isn't mapped): it won't work by retrying, so staff need to look.</summary>
    public void Rejected(string error, DateTimeOffset now)
    {
        Status = PosCommandStatus.Failed;
        LastError = Trim(error);
        LastAttemptAt = now;
    }

    /// <summary>Staff fixed the cause (e.g. mapped the table) and want it sent again.</summary>
    public void Retry(DateTimeOffset now)
    {
        if (Status == PosCommandStatus.Done) throw new DomainException("This has already reached the POS.");
        Status = PosCommandStatus.Pending;
        NextAttemptAt = now;
    }

    /// <summary>30 seconds, 1, 2, 5, 10, then every 15 minutes: a POS outage never gives up.</summary>
    public static TimeSpan Backoff(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(1),
        3 => TimeSpan.FromMinutes(2),
        4 => TimeSpan.FromMinutes(5),
        5 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(15)
    };

    private static string Trim(string error) => error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
}

public enum PosCommandKind
{
    /// <summary>Send an accepted order to the POS (blueprint createOrder), opening the POS check on the first one.</summary>
    SendOrder = 0,
    /// <summary>Apply a confirmed payment and tip as a tender (blueprint postPayment).</summary>
    PostPayment = 1,
    /// <summary>Close the POS check once the bill is paid in full (blueprint closeCheck, AUTO_CLOSE).</summary>
    CloseCheck = 2
}

public enum PosCommandStatus
{
    /// <summary>Waiting to be sent or retried (a payment here is blueprint POSTING_PENDING).</summary>
    Pending = 0,
    /// <summary>The POS has it (a payment here is blueprint POSTED).</summary>
    Done = 1,
    /// <summary>The POS refused it; staff need to fix the cause and retry.</summary>
    Failed = 2
}
