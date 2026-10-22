using System.Globalization;
using System.Text.Json;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Notifications;

/// <summary>
/// Queues a WhatsApp message to the venue's alert contacts for every successful payment
/// (blueprint P1-STAFF-02): table, amount and tip, whether the bill is paid or what's left, order
/// numbers, reference and time. The wording is short so it reads on a smartwatch through
/// notification mirroring (P1-STAFF-03). Sending happens later, in <see cref="NotificationDispatcher"/>.
/// </summary>
/// <remarks>
/// Each message has four template parameters, for an approved WhatsApp template such as
/// "{{1}}: {{2}} paid. {{3}}. {{4}}". WhatsApp doesn't allow line breaks inside parameters, so
/// each one is a single line.
/// </remarks>
public class PaymentAlerts(IAppDbContext db, IOptions<FeatureOptions> features, TimeProvider clock)
{
    /// <summary>A payment at a table went through.</summary>
    public Task QueueTableAsync(Payment payment, string place, long remainingMinor, CancellationToken ct) =>
        QueueAsync(payment, NotificationKind.PaymentAlert, place, remainingMinor == 0 ? "Bill fully paid" : $"{Money(remainingMinor, payment.Currency)} left to pay",
            async () =>
            {
                var numbers = payment.CheckId is { } checkId
                    ? await db.Orders.IgnoreQueryFilters().Where(o => o.CheckId == checkId && o.Status != Domain.Orders.OrderStatus.Rejected)
                        .OrderBy(o => o.Number).Select(o => o.Number).ToListAsync(ct)
                    : [];
                return numbers.Count == 0 ? null : "Orders " + string.Join(", ", numbers.Select(n => $"#{n}"));
            }, ct);

    /// <summary>A payment through a payment link went through.</summary>
    public Task QueueLinkAsync(Payment payment, PaymentLink link, long remainingMinor, CancellationToken ct) =>
        QueueAsync(payment, NotificationKind.LinkPaymentAlert,
            $"Payment link {link.Code}{(link.Reference is { } r ? $" ({r})" : "")}",
            remainingMinor == 0 ? "Link fully paid" : $"{Money(remainingMinor, payment.Currency)} left to pay",
            () => Task.FromResult<string?>(null), ct);

    private async Task QueueAsync(Payment payment, NotificationKind kind, string place, string status,
        Func<Task<string?>> detail, CancellationToken ct)
    {
        if (!features.Value.WhatsAppAlerts) return;
        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking().Include(v => v.Tenant).SingleAsync(v => v.Id == payment.VenueId, ct);
        if (!venue.WhatsAppAlertsEnabled) return;
        var contacts = await db.AlertContacts.IgnoreQueryFilters().AsNoTracking().Where(c => c.VenueId == venue.Id).ToListAsync(ct);
        if (contacts.Count == 0) return;

        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;
        var when = TimeZoneInfo.ConvertTime(payment.SucceededAt ?? clock.GetUtcNow(), zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        var split = payment.Mode switch
        {
            PaymentMode.Equal => $" · {payment.Shares} of {payment.SplitPeople} shares",
            PaymentMode.Items => " · by item",
            PaymentMode.Custom => " · part payment",
            _ => ""
        };
        var parameters = new[]
        {
            $"{venue.Tenant.DisplayName} · {place}",
            $"{Money(payment.AmountMinor, payment.Currency)}{(payment.TipMinor > 0 ? $" (tip {Money(payment.TipMinor, payment.Currency)})" : "")}{split}",
            status,
            string.Join(" · ", new[] { await detail(), payment.Reference, when }.Where(s => s is not null))
        };
        var text = $"{parameters[0]}\n{parameters[1]} paid\n{parameters[2]}\n{parameters[3]}";
        var json = JsonSerializer.Serialize(parameters);

        var now = clock.GetUtcNow();
        foreach (var contact in contacts)
            db.NotificationDeliveries.Add(new NotificationDelivery(venue.TenantId, venue.Id, NotificationChannel.WhatsApp, kind,
                contact.PhoneNumber, $"payment:{payment.Id}", payment.Id, null, text, null, json, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Already queued for this payment (the unique key refused a duplicate); nothing to add.
            db.ChangeTracker.Clear();
        }
    }

    private static string Money(long minor, string currency) => TransactionService.FormatMoney(minor, currency);
}
