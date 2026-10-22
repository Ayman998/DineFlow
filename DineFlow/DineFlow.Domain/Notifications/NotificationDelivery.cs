using DineFlow.Domain.Common;

namespace DineFlow.Domain.Notifications;

/// <summary>
/// One message to one recipient: a WhatsApp payment alert, a scheduled report e-mail or a test
/// (blueprint "Messaging &amp; Report Delivery", P1-STAFF-02). It is written first and sent by a
/// background worker, so a slow or failing provider never holds up a payment. Failures are kept
/// with their reason and retried with backoff; after <see cref="MaxAttempts"/> the message stays
/// failed until someone resends it.
/// </summary>
/// <remarks>
/// <see cref="Attempts"/> is checked on every save, so two workers can't both send the same message:
/// the one that claims it first (by counting the attempt) wins.
/// </remarks>
public class NotificationDelivery : Entity, ITenantOwned
{
    public const int MaxAttempts = 5;
    public const int MaxErrorLength = 500;

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationKind Kind { get; private set; }
    /// <summary>A phone number in E.164 form for WhatsApp, an e-mail address for e-mail.</summary>
    public string Recipient { get; private set; } = string.Empty;
    /// <summary>Makes each message unique per recipient, e.g. "payment:{id}", so a retried event can't send it twice.</summary>
    public string DedupeKey { get; private set; } = string.Empty;
    /// <summary>The thing the message is about, e.g. the payment, for linking from the log.</summary>
    public Guid? SubjectId { get; private set; }

    /// <summary>E-mail subject; null for WhatsApp.</summary>
    public string? Subject { get; private set; }
    /// <summary>The message as plain text: what WhatsApp shows, or the e-mail's text part.</summary>
    public string TextBody { get; private set; } = string.Empty;
    public string? HtmlBody { get; private set; }
    /// <summary>WhatsApp template parameters, in order, as JSON; the provider fills them into an approved template.</summary>
    public string? TemplateParameters { get; private set; }

    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? LastAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }
    public string? ProviderMessageId { get; private set; }

    private NotificationDelivery() { }

    public NotificationDelivery(
        Guid tenantId, Guid venueId, NotificationChannel channel, NotificationKind kind, string recipient, string dedupeKey,
        Guid? subjectId, string? subject, string textBody, string? htmlBody, string? templateParameters, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        Channel = channel;
        Kind = kind;
        Recipient = Text.Required(recipient, 254, "Recipient");
        DedupeKey = Text.Required(dedupeKey, 120, "Dedupe key");
        SubjectId = subjectId;
        Subject = Text.Optional(subject, 200, "Subject");
        TextBody = textBody;
        HtmlBody = htmlBody;
        TemplateParameters = templateParameters;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public bool IsDue(DateTimeOffset now) => Status == DeliveryStatus.Pending && NextAttemptAt <= now;

    /// <summary>Counts an attempt before sending; saving it is what claims the message for this worker.</summary>
    public void StartAttempt(DateTimeOffset now)
    {
        if (!IsDue(now)) throw new DomainException("This message isn't waiting to be sent.");
        Attempts++;
        LastAttemptAt = now;
    }

    public void Delivered(string? providerMessageId, DateTimeOffset now)
    {
        Status = DeliveryStatus.Sent;
        SentAt = now;
        ProviderMessageId = providerMessageId;
        LastError = null;
    }

    /// <summary>The provider refused it or couldn't be reached: try again later, or give up after <see cref="MaxAttempts"/>.</summary>
    public void Undelivered(string error, DateTimeOffset now)
    {
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        if (Attempts >= MaxAttempts) Status = DeliveryStatus.Failed;
        else NextAttemptAt = now + Backoff(Attempts);
    }

    /// <summary>A person asked to send a failed message again: it gets a fresh set of attempts.</summary>
    public void Resend(DateTimeOffset now)
    {
        if (Status != DeliveryStatus.Failed) throw new DomainException("Only a failed message can be sent again.");
        Status = DeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }

    /// <summary>30 seconds, 2 minutes, 10 minutes, then every 30 minutes.</summary>
    public static TimeSpan Backoff(int attempts) => attempts switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30)
    };
}

public enum NotificationChannel
{
    WhatsApp = 0,
    Email = 1
}

public enum NotificationKind
{
    /// <summary>A successful payment at a table (P1-STAFF-02).</summary>
    PaymentAlert = 0,
    /// <summary>A successful payment through a payment link.</summary>
    LinkPaymentAlert = 1,
    /// <summary>A scheduled report (P1-MER-09).</summary>
    Report = 2,
    /// <summary>Sent from settings to check the channel works.</summary>
    Test = 3
}

public enum DeliveryStatus
{
    /// <summary>Waiting to be sent, or to be retried.</summary>
    Pending = 0,
    Sent = 1,
    /// <summary>Gave up after <see cref="NotificationDelivery.MaxAttempts"/>; can be resent.</summary>
    Failed = 2
}
