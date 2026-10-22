using System.Globalization;
using System.Net;
using System.Text;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Common;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.PaymentLinks;

/// <summary>
/// The merchant's Payment Links area (blueprint 4.4, 9.2 /v1/merchant/payment-links/*, E16):
/// create a link for an amount, share it, follow its status and payments, deactivate it, and
/// export the list. Payments through links are ordinary transactions, so refunds work the same.
/// </summary>
public class PaymentLinkService(
    IAppDbContext db,
    ITenantContext tenant,
    IQrTokenService tokens,
    IEmailSender email,
    IOptions<GuestOptions> guestOptions,
    TimeProvider clock)
{
    /// <summary>Creates a link; safe to retry with the same clientRequestId. E-mails the customer when an address is given.</summary>
    public async Task<PaymentLinkDetailDto> CreateAsync(CreatePaymentLinkRequest request, Guid staffUserId, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        if (await FindRequestAsync(request.ClientRequestId, ct) is { } existing) return await GetAsync(existing.Id, ct);

        var venue = await db.Venues.Include(v => v.Tenant).SingleOrDefaultAsync(v => v.Id == request.VenueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var validFor = request.ValidityUnit == ValidityUnit.Days ? TimeSpan.FromDays(request.ValidFor) : TimeSpan.FromHours(request.ValidFor);
        var now = clock.GetUtcNow();

        var id = Guid.NewGuid();
        PaymentLink link;
        try
        {
            link = new PaymentLink(id, tenantId, venue.Id, request.ClientRequestId, venue.Currency, request.AmountMinor, request.Reference,
                request.AllowSplit, request.RequestTip, validFor, request.CustomerEmail, staffUserId, tokens.Hash(tokens.CreatePaymentLinkToken(id)), now);
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_payment_link", ex.Message);
        }

        db.PaymentLinks.Add(link);
        db.AuditEntries.Add(new AuditEntry(tenantId, staffUserId, "payment_link.created", nameof(PaymentLink), link.Id, now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The same request arrived twice at once; the other one created the link.
            db.ChangeTracker.Clear();
            if (await FindRequestAsync(request.ClientRequestId, ct) is { } again) return await GetAsync(again.Id, ct);
            throw;
        }

        if (link.CustomerEmail is { } to) await TryNotifyAsync(link, venue, to, ct);
        return await GetAsync(link.Id, ct);
    }

    public async Task<PaymentLinkPageDto> ListAsync(PaymentLinkQuery query, CancellationToken ct)
    {
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == query.VenueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var now = clock.GetUtcNow();
        var links = Search(db.PaymentLinks.AsNoTracking().Where(l => l.VenueId == venue.Id), venue.Currency, query.Search);

        var counts = new PaymentLinkCountsDto(
            await links.CountAsync(ct),
            await Filter(links, PaymentLinkFilter.Active, now).CountAsync(ct),
            await Filter(links, PaymentLinkFilter.Paid, now).CountAsync(ct),
            await Filter(links, PaymentLinkFilter.Expired, now).CountAsync(ct),
            await Filter(links, PaymentLinkFilter.Cancelled, now).CountAsync(ct));

        var filtered = Filter(links, query.Status, now);
        var total = await filtered.CountAsync(ct);
        var page = await filtered.OrderByDescending(l => l.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PaymentLinkPageDto(await RowsAsync(page, now, ct), total, query.Page, query.PageSize, counts);
    }

    public async Task<PaymentLinkDetailDto> GetAsync(Guid linkId, CancellationToken ct)
    {
        var link = await db.PaymentLinks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == linkId, ct)
            ?? throw AppException.NotFound("payment_link_not_found", "Payment link not found.");
        var now = clock.GetUtcNow();
        var row = (await RowsAsync([link], now, ct)).Single();

        var payments = await db.Payments.AsNoTracking().Where(p => p.PaymentLinkId == link.Id).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        var paymentIds = payments.Select(p => p.Id).ToList();
        var refunded = await db.Refunds.AsNoTracking()
            .Where(r => paymentIds.Contains(r.PaymentId) && r.Status == RefundStatus.Succeeded)
            .GroupBy(r => r.PaymentId).Select(g => new { g.Key, Sum = g.Sum(r => r.AmountMinor) })
            .ToDictionaryAsync(g => g.Key, g => g.Sum, ct);
        var staffIds = new[] { link.CreatedByStaffId, link.CancelledByStaffId ?? Guid.Empty };
        var staff = await db.StaffUsers.AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        string? Name(Guid? id) => id is { } key ? staff.GetValueOrDefault(key) : null;

        var paymentDtos = payments.Select(p =>
        {
            var back = refunded.GetValueOrDefault(p.Id);
            return new PaymentLinkPaymentDto(p.Id, p.Reference, TransactionService.StateOf(p, back), p.Method, p.CardBrand, p.CardLast4,
                p.Mode, p.Shares, p.SplitPeople, p.BillAmountMinor, p.TipMinor, p.AmountMinor, back, p.CreatedAt, p.SucceededAt);
        }).ToList();

        return new PaymentLinkDetailDto(row, Math.Max(0, link.AmountMinor - row.PaidMinor), Name(link.CreatedByStaffId),
            link.CustomerNotifiedAt, link.CancelledAt, Name(link.CancelledByStaffId), paymentDtos,
            Timeline(link, payments, Name(link.CreatedByStaffId), Name(link.CancelledByStaffId), now));
    }

    /// <summary>Turns the link off so nobody can start a new payment on it. Payments already made stay as they are.</summary>
    public async Task<PaymentLinkDetailDto> CancelAsync(Guid linkId, Guid staffUserId, CancellationToken ct)
    {
        await Concurrency.RetryAsync(db, async () =>
        {
            var link = await db.PaymentLinks.SingleOrDefaultAsync(l => l.Id == linkId, ct)
                ?? throw AppException.NotFound("payment_link_not_found", "Payment link not found.");
            if (link.Status == PaymentLinkStatus.Cancelled) return true;

            var now = clock.GetUtcNow();
            try { link.Cancel(staffUserId, now); }
            catch (DomainException ex) { throw AppException.Conflict("payment_link_paid", ex.Message); }

            db.AuditEntries.Add(new AuditEntry(link.TenantId, staffUserId, "payment_link.cancelled", nameof(PaymentLink), link.Id, now));
            await db.SaveChangesAsync(ct);
            return true;
        });
        return await GetAsync(linkId, ct);
    }

    /// <summary>E-mails the link to the customer (again, or to a new address). Only open links can be sent.</summary>
    public async Task<PaymentLinkDetailDto> NotifyAsync(Guid linkId, string to, CancellationToken ct)
    {
        var link = await db.PaymentLinks.SingleOrDefaultAsync(l => l.Id == linkId, ct)
            ?? throw AppException.NotFound("payment_link_not_found", "Payment link not found.");
        if (!link.IsPayable(clock.GetUtcNow()))
            throw AppException.Conflict("payment_link_closed", "This link is no longer active, so there's nothing to send.");

        var venue = await db.Venues.Include(v => v.Tenant).SingleAsync(v => v.Id == link.VenueId, ct);
        if (!await TryNotifyAsync(link, venue, to, ct))
            throw AppException.Conflict("email_failed", "The e-mail couldn't be sent. Try again, or share the link another way.");
        return await GetAsync(linkId, ct);
    }

    /// <summary>The filtered list as CSV, in the venue's time zone.</summary>
    public async Task<string> ExportCsvAsync(PaymentLinkQuery query, CancellationToken ct)
    {
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == query.VenueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var now = clock.GetUtcNow();
        var links = await Filter(Search(db.PaymentLinks.AsNoTracking().Where(l => l.VenueId == venue.Id), venue.Currency, query.Search), query.Status, now)
            .OrderByDescending(l => l.CreatedAt).Take(50_000).ToListAsync(ct);
        var rows = await RowsAsync(links, now, ct);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;
        string Local(DateTimeOffset? at) => at is { } value ? TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
        string Major(long minor) => (minor / (decimal)Math.Pow(10, TransactionService.MinorDigits(venue.Currency)))
            .ToString($"F{TransactionService.MinorDigits(venue.Currency)}", CultureInfo.InvariantCulture);

        var csv = new StringBuilder("Created,Link,Reference,Status,Currency,Amount,Paid,Tips,Payments,Expires,Paid at,Customer e-mail\r\n");
        foreach (var row in rows)
        {
            csv.AppendJoin(',', Local(row.CreatedAt), row.Code, Cell(row.Reference), row.Status.ToString(), row.Currency,
                Major(row.AmountMinor), Major(row.PaidMinor), Major(row.TipsMinor), row.PaymentCount.ToString(CultureInfo.InvariantCulture),
                Local(row.ExpiresAt), Local(row.PaidAt), Cell(row.CustomerEmail));
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    // ---- Helpers ----

    private Task<PaymentLink?> FindRequestAsync(Guid clientRequestId, CancellationToken ct) =>
        db.PaymentLinks.AsNoTracking().SingleOrDefaultAsync(l => l.ClientRequestId == clientRequestId, ct);

    /// <summary>The public URL; the token is derived again from the link id, never stored.</summary>
    private string UrlOf(PaymentLink link) =>
        $"{guestOptions.Value.AppBaseUrl.TrimEnd('/')}/l/{tokens.CreatePaymentLinkToken(link.Id)}";

    /// <summary>Sends the link by e-mail. A failure doesn't undo anything; the merchant can resend or share it another way.</summary>
    private async Task<bool> TryNotifyAsync(PaymentLink link, Venue venue, string to, CancellationToken ct)
    {
        try
        {
            await email.SendAsync(PaymentLinkEmail.Build(to.Trim(), venue, link, UrlOf(link)), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        link.Notified(to, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static IQueryable<PaymentLink> Filter(IQueryable<PaymentLink> links, PaymentLinkFilter filter, DateTimeOffset now) => filter switch
    {
        PaymentLinkFilter.Active => links.Where(l => l.Status == PaymentLinkStatus.Active && l.ExpiresAt > now),
        PaymentLinkFilter.Expired => links.Where(l => l.Status == PaymentLinkStatus.Active && l.ExpiresAt <= now),
        PaymentLinkFilter.Paid => links.Where(l => l.Status == PaymentLinkStatus.Paid),
        PaymentLinkFilter.Cancelled => links.Where(l => l.Status == PaymentLinkStatus.Cancelled),
        _ => links
    };

    /// <summary>Matches a link code, the merchant's reference, the customer's e-mail or the amount.</summary>
    private static IQueryable<PaymentLink> Search(IQueryable<PaymentLink> links, string currency, string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text)) return links;

        var code = text.ToUpperInvariant();
        var lower = text.ToLowerInvariant();
        long? amount = decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var major)
            ? (long)Math.Round(major * (decimal)Math.Pow(10, TransactionService.MinorDigits(currency)))
            : null;
        // Short searches like "50" are amounts, not pieces of a code.
        var byCode = code.StartsWith("PL-") || code.Length >= 4;
        // Lower-cased on both sides so the match ignores case whatever the database's collation.
        return links.Where(l => (byCode && l.Code.Contains(code)) || (l.Reference != null && l.Reference.ToLower().Contains(lower))
            || (l.CustomerEmail != null && l.CustomerEmail.ToLower().Contains(lower)) || (amount != null && l.AmountMinor == amount));
    }

    private async Task<IReadOnlyList<PaymentLinkRowDto>> RowsAsync(IReadOnlyList<PaymentLink> links, DateTimeOffset now, CancellationToken ct)
    {
        var ids = links.Select(l => l.Id).ToList();
        var paid = await db.Payments.AsNoTracking()
            .Where(p => p.PaymentLinkId != null && ids.Contains(p.PaymentLinkId.Value) && p.Status == PaymentStatus.Succeeded)
            .GroupBy(p => p.PaymentLinkId!.Value)
            .Select(g => new { g.Key, Count = g.Count(), Paid = g.Sum(p => p.BillAmountMinor), Tips = g.Sum(p => p.TipMinor) })
            .ToDictionaryAsync(g => g.Key, ct);

        return links.Select(l =>
        {
            var totals = paid.GetValueOrDefault(l.Id);
            return new PaymentLinkRowDto(l.Id, l.Code, l.Reference, l.StatusAt(now), l.Currency, l.AmountMinor,
                totals?.Paid ?? 0, totals?.Tips ?? 0, totals?.Count ?? 0, l.AllowSplit, l.RequestTip, l.CustomerEmail,
                l.CreatedAt, l.ExpiresAt, l.PaidAt, UrlOf(l));
        }).ToList();
    }

    private static IReadOnlyList<TimelineEventDto> Timeline(
        PaymentLink link, IReadOnlyList<Payment> payments, string? createdBy, string? cancelledBy, DateTimeOffset now)
    {
        string Money(long minor) => TransactionService.FormatMoney(minor, link.Currency);
        var events = new List<TimelineEventDto>
        {
            new(link.CreatedAt, "link", $"Link created{(createdBy is null ? "" : $" by {createdBy}")} for {Money(link.AmountMinor)}")
        };
        if (link.CustomerNotifiedAt is { } notified) events.Add(new(notified, "link", $"Link e-mailed to {link.CustomerEmail}"));

        foreach (var p in payments)
        {
            events.Add(new(p.CreatedAt, "payment", $"{p.Reference}: payment started · {Money(p.AmountMinor)} by {p.Method}"));
            if (p.Status == PaymentStatus.Succeeded && p.SucceededAt is { } paidAt)
                events.Add(new(paidAt, "payment", $"{p.Reference}: paid {Money(p.AmountMinor)}{(p.TipMinor > 0 ? $" (tip {Money(p.TipMinor)})" : "")}"));
            else if (p.Status == PaymentStatus.Failed)
                events.Add(new(p.UpdatedAt, "payment", $"{p.Reference}: declined{(p.FailureReason is { } reason ? $" ({reason})" : "")}"));
            else if (p.Status == PaymentStatus.Cancelled)
                events.Add(new(p.UpdatedAt, "payment", $"{p.Reference}: the customer left the payment page without paying"));
        }

        if (link.PaidAt is { } fullyPaid) events.Add(new(fullyPaid, "link", "Paid in full; the link closed"));
        if (link.CancelledAt is { } cancelled) events.Add(new(cancelled, "link", $"Deactivated{(cancelledBy is null ? "" : $" by {cancelledBy}")}"));
        if (link.StatusAt(now) == PaymentLinkStatus.Expired) events.Add(new(link.ExpiresAt, "link", "Expired"));

        return events.OrderBy(e => e.At).ToList();
    }

    /// <summary>A CSV cell: quoted, and never starting with a formula character (CSV injection).</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if ("=+-@\t\r".Contains(value[0])) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

/// <summary>The e-mail that sends a customer their payment link (P1-LNK-03).</summary>
internal static class PaymentLinkEmail
{
    public static EmailMessage Build(string to, Venue venue, PaymentLink link, string url)
    {
        var brand = venue.Tenant.DisplayName;
        var amount = TransactionService.FormatMoney(link.AmountMinor, link.Currency);
        var expires = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(link.ExpiresAt, zone)
            : link.ExpiresAt;
        var reference = link.Reference is { } r ? $" · Reference {r}" : "";

        var text = new StringBuilder()
            .AppendLine($"{brand} · {venue.Name}")
            .AppendLine($"You've been sent a payment link for {amount}{reference}.")
            .AppendLine()
            .AppendLine($"Pay securely here: {url}")
            .AppendLine()
            .AppendLine($"Link {link.Code} · active until {expires:dd MMM yyyy HH:mm}. The amount includes all taxes and charges.")
            .ToString();

        static string E(string s) => WebUtility.HtmlEncode(s);
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;color:#101828">
              <h2 style="margin:0">{E(brand)} · {E(venue.Name)}</h2>
              <p style="color:#475467;margin:4px 0 16px">You've been sent a payment link{E(reference)}.</p>
              <p style="font-size:28px;font-weight:800;margin:0 0 16px">{E(amount)}</p>
              <p><a href="{E(url)}" style="display:inline-block;background:#101828;color:#fff;padding:12px 20px;border-radius:12px;text-decoration:none;font-weight:700">Pay now</a></p>
              <p style="color:#475467">Link {E(link.Code)} · active until {expires:dd MMM yyyy HH:mm}. The amount includes all taxes and charges.</p>
            </div>
            """;

        return new EmailMessage(to, $"Payment request from {brand} ({link.Code})", text, html);
    }
}
