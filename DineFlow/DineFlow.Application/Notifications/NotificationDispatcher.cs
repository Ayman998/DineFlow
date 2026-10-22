using System.Text.Json;
using DineFlow.Application.Abstractions;
using DineFlow.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Notifications;

/// <summary>
/// Sends queued messages that are due (blueprint "Messaging &amp; Report Delivery"): WhatsApp through
/// <see cref="IWhatsAppSender"/>, e-mail through <see cref="IEmailSender"/>. A failure is recorded
/// on the message and retried with backoff; nothing here ever throws back to the caller, so one bad
/// message or provider outage can't stop the others.
/// </summary>
public class NotificationDispatcher(IAppDbContext db, IWhatsAppSender whatsApp, IEmailSender email, TimeProvider clock)
{
    public const int BatchSize = 20;

    /// <summary>Sends up to <see cref="BatchSize"/> due messages; returns how many were delivered.</summary>
    public async Task<int> DispatchDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Runs in the background without a tenant, so the tenant filter is bypassed.
        var due = await db.NotificationDeliveries.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).Take(BatchSize).Select(d => d.Id)
            .ToListAsync(ct);

        var delivered = 0;
        foreach (var id in due)
            if (await SendAsync(id, ct)) delivered++;
        return delivered;
    }

    private async Task<bool> SendAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var message = await db.NotificationDeliveries.IgnoreQueryFilters().SingleAsync(d => d.Id == id, ct);
        var now = clock.GetUtcNow();
        if (!message.IsDue(now)) return false;

        // Claim it: counting the attempt fails to save if another worker already did.
        message.StartAttempt(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }

        try
        {
            var providerId = message.Channel switch
            {
                NotificationChannel.WhatsApp => await whatsApp.SendAsync(new WhatsAppMessage(message.Recipient,
                    JsonSerializer.Deserialize<string[]>(message.TemplateParameters ?? "[]") ?? [], message.TextBody), ct),
                _ => await SendEmailAsync(message, ct)
            };
            message.Delivered(providerId, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.Undelivered(ex.Message, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(ct);
        return message.Status == DeliveryStatus.Sent;
    }

    private async Task<string?> SendEmailAsync(NotificationDelivery message, CancellationToken ct)
    {
        await email.SendAsync(new EmailMessage(message.Recipient, message.Subject ?? "DineFlow", message.TextBody,
            message.HtmlBody ?? System.Net.WebUtility.HtmlEncode(message.TextBody)), ct);
        return null;
    }
}
