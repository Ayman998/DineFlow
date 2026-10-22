using System.Text.Json;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Common;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Notifications;

/// <summary>
/// Settings → Notifications (blueprint P1-STAFF-02, E28): turn WhatsApp payment alerts on or off,
/// choose who gets them, send a test, and see what was delivered or failed. Changes are audit-logged.
/// </summary>
public class AlertService(IAppDbContext db, ITenantContext tenant, IWhatsAppSender whatsApp, TimeProvider clock)
{
    public async Task<AlertSettingsDto> GetAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var contacts = await db.AlertContacts.AsNoTracking().Where(c => c.VenueId == venue.Id).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        return new AlertSettingsDto(venue.WhatsAppAlertsEnabled,
            contacts.Select(c => new AlertContactDto(c.Id, c.Name, c.PhoneNumber, c.CreatedAt)).ToList(), whatsApp.Name);
    }

    public async Task<AlertSettingsDto> SetEnabledAsync(Guid venueId, bool enabled, Guid staffUserId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        if (venue.WhatsAppAlertsEnabled != enabled)
        {
            venue.SetWhatsAppAlerts(enabled);
            Audit(staffUserId, enabled ? "alerts.whatsapp.enabled" : "alerts.whatsapp.disabled", nameof(Venue), venue.Id);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(venueId, ct);
    }

    public async Task<AlertSettingsDto> AddContactAsync(Guid venueId, AlertContactRequest request, Guid staffUserId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        AlertContact contact;
        try { contact = new AlertContact(venue.TenantId, venue.Id, request.Name, request.PhoneNumber, clock.GetUtcNow()); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_contact", ex.Message); }

        var existing = await db.AlertContacts.Where(c => c.VenueId == venue.Id).Select(c => c.PhoneNumber).ToListAsync(ct);
        if (existing.Contains(contact.PhoneNumber))
            throw AppException.Conflict("contact_exists", "That number already gets alerts.");
        if (existing.Count >= AlertContact.MaxPerVenue)
            throw AppException.Conflict("too_many_contacts", $"Up to {AlertContact.MaxPerVenue} numbers can get alerts.");

        db.AlertContacts.Add(contact);
        Audit(staffUserId, "alerts.contact.added", nameof(AlertContact), contact.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(venueId, ct);
    }

    public async Task<AlertSettingsDto> RemoveContactAsync(Guid contactId, Guid staffUserId, CancellationToken ct)
    {
        var contact = await db.AlertContacts.SingleOrDefaultAsync(c => c.Id == contactId, ct)
            ?? throw AppException.NotFound("contact_not_found", "That number isn't on the list.");
        db.AlertContacts.Remove(contact);
        Audit(staffUserId, "alerts.contact.removed", nameof(AlertContact), contact.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(contact.VenueId, ct);
    }

    /// <summary>Queues a test message to every alert number, to check they're right and the channel works.</summary>
    public async Task SendTestAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await db.Venues.Include(v => v.Tenant).SingleOrDefaultAsync(v => v.Id == venueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var contacts = await db.AlertContacts.AsNoTracking().Where(c => c.VenueId == venue.Id).ToListAsync(ct);
        if (contacts.Count == 0) throw AppException.Conflict("no_contacts", "Add a number first.");

        var now = clock.GetUtcNow();
        var parameters = new[] { $"{venue.Tenant.DisplayName} · {venue.Name}", "Test", "Payment alerts are working", "No money moved" };
        foreach (var contact in contacts)
            db.NotificationDeliveries.Add(new NotificationDelivery(venue.TenantId, venue.Id, NotificationChannel.WhatsApp, NotificationKind.Test,
                contact.PhoneNumber, $"test:{Guid.NewGuid():N}", null, null,
                $"{parameters[0]}\nThis is a test of DineFlow payment alerts. You'll get a message like this for every payment.",
                null, JsonSerializer.Serialize(parameters), now));
        await db.SaveChangesAsync(ct);
    }

    public async Task<DeliveryPageDto> DeliveriesAsync(DeliveryQuery query, CancellationToken ct)
    {
        var venue = await VenueAsync(query.VenueId, ct);
        var all = db.NotificationDeliveries.AsNoTracking().Where(d => d.VenueId == venue.Id);
        var filtered = all;
        if (query.Channel is { } channel) filtered = filtered.Where(d => d.Channel == channel);
        if (query.Status is { } status) filtered = filtered.Where(d => d.Status == status);

        var total = await filtered.CountAsync(ct);
        var failed = await (query.Channel is { } c ? all.Where(d => d.Channel == c) : all).CountAsync(d => d.Status == DeliveryStatus.Failed, ct);
        var page = await filtered.OrderByDescending(d => d.CreatedAt).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new DeliveryPageDto(page.Select(ToDto).ToList(), total, query.Page, query.PageSize, failed);
    }

    /// <summary>Sends a failed message again, with a fresh set of attempts.</summary>
    public async Task<DeliveryDto> ResendAsync(Guid deliveryId, Guid staffUserId, CancellationToken ct)
    {
        var delivery = await db.NotificationDeliveries.SingleOrDefaultAsync(d => d.Id == deliveryId, ct)
            ?? throw AppException.NotFound("delivery_not_found", "Message not found.");
        try { delivery.Resend(clock.GetUtcNow()); }
        catch (DomainException ex) { throw AppException.Conflict("not_failed", ex.Message); }
        Audit(staffUserId, "notification.resent", nameof(NotificationDelivery), delivery.Id);
        await db.SaveChangesAsync(ct);
        return ToDto(delivery);
    }

    internal static DeliveryDto ToDto(NotificationDelivery d) => new(d.Id, d.Channel, d.Kind, d.Recipient, d.Subject, d.TextBody, d.Status,
        d.Attempts, d.CreatedAt, d.SentAt, d.Status == DeliveryStatus.Pending ? d.NextAttemptAt : null, d.LastError, d.SubjectId);

    private async Task<Venue> VenueAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId, ct) ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

    private void Audit(Guid staffUserId, string action, string objectType, Guid objectId) =>
        db.AuditEntries.Add(new AuditEntry(tenant.RequireTenantId(), staffUserId, action, objectType, objectId, clock.GetUtcNow()));
}
