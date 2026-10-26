using System.ComponentModel.DataAnnotations;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Audit;

/// <param name="Area">Limits the list to one kind of action, e.g. "payment" or "alerts".</param>
public record AuditQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    [StringLength(40)] string? Area = null,
    [Range(1, 10_000)] int Page = 1,
    [Range(1, 100)] int PageSize = 50);

public record AuditEntryDto(Guid Id, DateTimeOffset At, string Action, string Description, string ObjectType, Guid ObjectId, string? StaffName);

public record AuditPageDto(IReadOnlyList<AuditEntryDto> Items, int Total, int Page, int PageSize);

/// <summary>
/// The audit log for owners and managers (blueprint section 11, E15 "audit"): who did which
/// sensitive thing and when, e.g. refunds, viewing a guest's contact details, payment links and
/// notification settings. Read-only; entries are never edited or deleted.
/// </summary>
public class AuditService(IAppDbContext db, ITenantContext tenant)
{
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["payment.refund.requested"] = "Requested a refund",
        ["review.contact.viewed"] = "Viewed a guest's contact details",
        ["payment_link.created"] = "Created a payment link",
        ["payment_link.cancelled"] = "Deactivated a payment link",
        ["alerts.whatsapp.enabled"] = "Turned WhatsApp payment alerts on",
        ["alerts.whatsapp.disabled"] = "Turned WhatsApp payment alerts off",
        ["alerts.contact.added"] = "Added a WhatsApp alert number",
        ["alerts.contact.removed"] = "Removed a WhatsApp alert number",
        ["notification.resent"] = "Resent a failed message",
        ["report_schedule.created"] = "Created an e-mail report",
        ["report_schedule.updated"] = "Changed an e-mail report",
        ["report_schedule.deleted"] = "Deleted an e-mail report",
        ["report_schedule.sent_now"] = "Sent an e-mail report now",
        ["settlement.imported"] = "Imported a settlement report",
        ["reconciliation.resolved"] = "Resolved a reconciliation difference",
        ["pos.connection.saved"] = "Changed the POS connection",
        ["pos.mapping.saved"] = "Changed POS table or item mapping",
        ["pos.command.retried"] = "Sent an item to the POS again"
    };

    public async Task<AuditPageDto> ListAsync(AuditQuery query, CancellationToken ct)
    {
        tenant.RequireTenantId();
        var entries = db.AuditEntries.AsNoTracking();
        if (query.From is { } from) entries = entries.Where(a => a.CreatedAt >= from);
        if (query.To is { } to) entries = entries.Where(a => a.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(query.Area))
        {
            var area = query.Area.Trim().ToLowerInvariant();
            entries = entries.Where(a => a.Action.StartsWith(area));
        }

        var total = await entries.CountAsync(ct);
        var page = await entries.OrderByDescending(a => a.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var staffIds = page.Select(a => a.StaffUserId).Distinct().ToList();
        var staff = await db.StaffUsers.AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);

        return new AuditPageDto(page.Select(a => new AuditEntryDto(a.Id, a.CreatedAt, a.Action,
            Descriptions.GetValueOrDefault(a.Action, a.Action), a.ObjectType, a.ObjectId, staff.GetValueOrDefault(a.StaffUserId))).ToList(),
            total, query.Page, query.PageSize);
    }
}
