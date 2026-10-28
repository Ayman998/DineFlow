using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Finance;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Onboarding;

/// <param name="Key">Stable id the merchant app turns into a title, hint and link, e.g. "payments.webhook".</param>
/// <param name="Required">Needed before guests pay real money; the rest are recommended.</param>
/// <param name="Count">The number behind the check where there is one (tables, staff, items…).</param>
public record GoLiveCheckDto(string Key, string Group, bool Required, bool Done, int? Count = null);

/// <param name="TestMode">Payments go through a test setup, so no card is charged yet.</param>
public record GoLiveDto(Guid VenueId, string PaymentProvider, bool TestMode, bool Ready, int RequiredDone, int RequiredTotal, IReadOnlyList<GoLiveCheckDto> Checks);

/// <summary>
/// The pilot's go-live checklist for one venue, worked out from what has actually happened rather
/// than ticked by hand: a printed QR scanned, a test payment confirmed by the gateway's webhook, a
/// refund, a receipt e-mail delivered, a settlement imported. It doubles as the acquirer's test
/// script for Mastercard Gateway (deploy/README.md lists the manual steps around it).
/// </summary>
public class GoLiveService(IAppDbContext db, ITenantContext tenant, PaymentEnvironment payments)
{
    public async Task<GoLiveDto> ChecklistAsync(Guid venueId, CancellationToken ct)
    {
        tenant.RequireTenantId();
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == venueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

        var checks = new List<GoLiveCheckDto>();
        void Add(string key, string group, bool required, bool done, int? count = null) =>
            checks.Add(new GoLiveCheckDto(key, group, required, done, count));

        // Venue
        Add("venue.tax", "venue", true, venue.TaxBasisPoints > 0);
        Add("venue.branding", "venue", false, venue.LogoUrl is not null);
        Add("venue.reviews", "venue", false, venue.ReviewsEnabled && venue.ReviewUrl is not null);

        // Tables and QR codes
        var tables = await db.QrTokens.CountAsync(q => q.Status == QrTokenStatus.Active
            && q.ServiceLocation.VenueId == venueId && q.ServiceLocation.IsActive, ct);
        Add("tables.qr", "tables", true, tables > 0, tables);
        Add("tables.scanned", "tables", true, await db.GuestSessions.AnyAsync(s => s.VenueId == venueId, ct));

        // Menu: a published menu with something guests can order.
        var orderable = await db.MenuItems.CountAsync(i => i.VenueId == venueId && i.IsAvailable, ct);
        var published = await db.Menus.AnyAsync(m => m.VenueId == venueId && m.IsPublished, ct);
        Add("menu.published", "menu", true, published && orderable > 0, orderable);
        var withPhotos = await db.MenuItems.CountAsync(i => i.VenueId == venueId && i.ImageUrl != null, ct);
        Add("menu.photos", "menu", false, orderable > 0 && withPhotos * 2 >= orderable, withPhotos);

        // Team: someone besides the owner to accept orders.
        var staff = await db.StaffUsers.CountAsync(u => u.IsActive && u.Role != StaffRole.Owner, ct);
        Add("team.staff", "team", true, staff > 0, staff);

        // Payments, in the order an acquirer's certification goes.
        var provider = payments.Provider;
        var venuePayments = db.Payments.Where(p => p.VenueId == venueId && p.Provider == provider);
        Add("payments.provider", "payments", true, provider != "sandbox");
        Add("payments.test_payment", "payments", true, await venuePayments.AnyAsync(p => p.Status == PaymentStatus.Succeeded, ct));
        // The webhook check proves the notification URL and secret are set in Merchant Administration.
        var paymentIds = venuePayments.Where(p => p.ProviderPaymentId != null).Select(p => p.ProviderPaymentId!);
        Add("payments.webhook", "payments", true,
            await db.ProcessedWebhooks.AnyAsync(w => w.Provider == provider && w.ViaWebhook && paymentIds.Contains(w.ProviderPaymentId), ct));
        Add("payments.refund", "payments", true,
            await db.Refunds.AnyAsync(r => r.VenueId == venueId && r.Provider == provider && r.Status == RefundStatus.Succeeded, ct));
        Add("payments.receipt", "payments", true,
            await db.NotificationDeliveries.AnyAsync(n => n.VenueId == venueId && n.Channel == NotificationChannel.Email && n.Status == DeliveryStatus.Sent, ct));
        Add("payments.settlement", "payments", true,
            await db.Settlements.AnyAsync(s => s.VenueId == venueId && s.Source == SettlementSource.File, ct));
        Add("payments.live", "payments", true, !payments.IsTest);

        // Alerts, reports and the POS are worth having but don't block a pilot.
        var contacts = await db.AlertContacts.CountAsync(c => c.VenueId == venueId, ct);
        Add("alerts.whatsapp", "alerts", false, venue.WhatsAppAlertsEnabled && contacts > 0, contacts);
        Add("alerts.reports", "alerts", false, await db.ReportSchedules.AnyAsync(r => r.VenueId == venueId && r.IsActive, ct));
        Add("pos.connected", "pos", false,
            await db.PosConnections.AnyAsync(c => c.VenueId == venueId && c.IsActive && c.LastCheckHealthy == true, ct));

        var required = checks.Where(c => c.Required).ToList();
        var done = required.Count(c => c.Done);
        return new GoLiveDto(venueId, provider, payments.IsTest, done == required.Count, done, required.Count, checks);
    }
}
