using System.Globalization;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Notifications;

/// <summary>
/// Queues a WhatsApp message to the venue's alert contacts for every successful payment
/// (blueprint P1-STAFF-02): table, amount and tip, whether the bill is paid or what's left, order
/// numbers, reference and time. The wording is short so it reads on a smartwatch through
/// notification mirroring (P1-STAFF-03). It's in the venue's default language, English or Arabic.
/// Sending happens later, in <see cref="NotificationDispatcher"/>.
/// </summary>
/// <remarks>
/// Each message has four template parameters, for an approved WhatsApp template such as
/// "{{1}}: {{2}} paid. {{3}}. {{4}}" (in Arabic "{{1}}: تم دفع {{2}}. {{3}}. {{4}}"). WhatsApp doesn't
/// allow line breaks inside parameters, so each one is a single line.
/// </remarks>
public class PaymentAlerts(IAppDbContext db, IOptions<FeatureOptions> features, TimeProvider clock)
{
    /// <summary>A payment at a table went through.</summary>
    /// <param name="location">Where it was paid, with its area loaded.</param>
    public Task QueueTableAsync(Payment payment, ServiceLocation location, long remainingMinor, CancellationToken ct) =>
        QueueAsync(payment, NotificationKind.PaymentAlert,
            arabic => $"{Languages.LocationName(location, arabic)}{(location.DiningArea is { } area ? $" ({Languages.AreaName(area, arabic)})" : "")}",
            arabic => remainingMinor == 0 ? (arabic ? "تم دفع الفاتورة بالكامل" : "Bill fully paid") : LeftToPay(remainingMinor, payment.Currency, arabic),
            async () => payment.CheckId is { } checkId
                ? await db.Orders.IgnoreQueryFilters().Where(o => o.CheckId == checkId && o.Status != Domain.Orders.OrderStatus.Rejected)
                    .OrderBy(o => o.Number).Select(o => o.Number).ToListAsync(ct)
                : [], ct);

    /// <summary>A payment through a payment link went through.</summary>
    public Task QueueLinkAsync(Payment payment, PaymentLink link, long remainingMinor, CancellationToken ct) =>
        QueueAsync(payment, NotificationKind.LinkPaymentAlert,
            arabic => $"{(arabic ? "رابط الدفع" : "Payment link")} {link.Code}{(link.Reference is { } r ? $" ({r})" : "")}",
            arabic => remainingMinor == 0 ? (arabic ? "تم دفع الرابط بالكامل" : "Link fully paid") : LeftToPay(remainingMinor, payment.Currency, arabic),
            () => Task.FromResult(new List<int>()), ct);

    private async Task QueueAsync(Payment payment, NotificationKind kind, Func<bool, string> place, Func<bool, string> status,
        Func<Task<List<int>>> orderNumbers, CancellationToken ct)
    {
        if (!features.Value.WhatsAppAlerts) return;
        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking().Include(v => v.Tenant).SingleAsync(v => v.Id == payment.VenueId, ct);
        if (!venue.WhatsAppAlertsEnabled) return;
        var contacts = await db.AlertContacts.IgnoreQueryFilters().AsNoTracking().Where(c => c.VenueId == venue.Id).ToListAsync(ct);
        if (contacts.Count == 0) return;

        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;
        var when = TimeZoneInfo.ConvertTime(payment.SucceededAt ?? clock.GetUtcNow(), zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        var numbers = await orderNumbers();

        (string Text, string Json) Message(bool arabic)
        {
            var split = payment.Mode switch
            {
                PaymentMode.Equal => arabic ? $" · {payment.Shares} من {payment.SplitPeople} حصص" : $" · {payment.Shares} of {payment.SplitPeople} shares",
                PaymentMode.Items => arabic ? " · حسب الصنف" : " · by item",
                PaymentMode.Custom => arabic ? " · دفعة جزئية" : " · part payment",
                _ => ""
            };
            var tip = payment.TipMinor > 0 ? $" ({(arabic ? "إكرامية" : "tip")} {Money(payment.TipMinor, payment.Currency)})" : "";
            var orders = numbers.Count == 0 ? null
                : arabic ? "الطلبات " + string.Join("، ", numbers.Select(n => $"#{n}")) : "Orders " + string.Join(", ", numbers.Select(n => $"#{n}"));
            var parameters = new[]
            {
                $"{venue.Tenant.DisplayName} · {place(arabic)}",
                $"{Money(payment.AmountMinor, payment.Currency)}{tip}{split}",
                status(arabic),
                string.Join(" · ", new[] { orders, payment.Reference, when }.Where(s => s is not null))
            };
            var text = arabic
                ? $"{parameters[0]}\nتم دفع {parameters[1]}\n{parameters[2]}\n{parameters[3]}"
                : $"{parameters[0]}\n{parameters[1]} paid\n{parameters[2]}\n{parameters[3]}";
            return (text, new WhatsAppTemplate(arabic ? Languages.Arabic : Languages.English, parameters).ToJson());
        }

        var (text, json) = Message(Languages.IsArabic(venue.DefaultLocale));
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

    private static string LeftToPay(long minor, string currency, bool arabic) =>
        arabic ? $"متبقٍ {Money(minor, currency)} للدفع" : $"{Money(minor, currency)} left to pay";

    private static string Money(long minor, string currency) => TransactionService.FormatMoney(minor, currency);
}
