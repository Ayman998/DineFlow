using System.Globalization;
using System.Net;
using System.Text;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Reports;

/// <summary>
/// Works out a venue's report for a period and renders it as an e-mail. Queries bypass the tenant
/// filter because scheduled reports run in the background; every query is limited to the venue,
/// which belongs to exactly one tenant, and merchant calls check the venue is theirs first.
/// </summary>
public class ReportBuilder(IAppDbContext db)
{
    public const int TopItemCount = 5;

    /// <param name="start">First local day of the period.</param>
    /// <param name="end">Local day after the period ends.</param>
    public async Task<ReportDto> BuildAsync(Venue venue, ReportFrequency frequency, DateTime start, DateTime end, CancellationToken ct)
    {
        var zone = Zone(venue);
        var previousStart = frequency switch
        {
            ReportFrequency.Monthly => start.AddMonths(-1),
            ReportFrequency.Weekly => start.AddDays(-7),
            _ => start.AddDays(-1)
        };
        var (from, to, before) = (Utc(start, zone), Utc(end, zone), Utc(previousStart, zone));
        var now = await FiguresAsync(venue.Id, from, to, ct);
        var then = await FiguresAsync(venue.Id, before, from, ct);

        ReportMetricDto Metric(string key, string label, ReportUnit unit, Func<Figures, decimal> value)
        {
            var (current, previous) = (value(now), value(then));
            return new(key, label, unit, current, previous, previous == 0 ? null : Math.Round((double)((current - previous) / previous), 4));
        }

        var metrics = new List<ReportMetricDto>
        {
            Metric("revenue", "Revenue", ReportUnit.Money, f => f.Revenue),
            Metric("tips", "Tips", ReportUnit.Money, f => f.Tips),
            Metric("refunds", "Refunds", ReportUnit.Money, f => f.Refunds),
            Metric("net", "Net (revenue + tips − refunds)", ReportUnit.Money, f => f.Revenue + f.Tips - f.Refunds),
            Metric("orders", "Orders", ReportUnit.Count, f => f.Orders),
            Metric("payments", "Successful payments", ReportUnit.Count, f => f.Payments),
            Metric("average", "Average payment", ReportUnit.Money, f => f.Payments == 0 ? 0 : Math.Round((f.Revenue + f.Tips) / f.Payments)),
            Metric("failures", "Payment failures", ReportUnit.Count, f => f.Failures),
            Metric("links", "Paid through payment links", ReportUnit.Money, f => f.LinkRevenue),
            Metric("reviews", "Ratings", ReportUnit.Count, f => f.Reviews),
            Metric("rating", "Average rating", ReportUnit.Rating, f => f.Rating)
        };

        var top = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.VenueId == venue.Id && o.Status != OrderStatus.Rejected && o.CreatedAt >= from && o.CreatedAt < to)
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.Name)
            .Select(g => new { Name = g.Key, Quantity = g.Sum(l => l.Quantity), Total = g.Sum(l => l.LineTotalMinor) })
            .OrderByDescending(g => g.Quantity).ThenByDescending(g => g.Total).ThenBy(g => g.Name)
            .Take(TopItemCount).ToListAsync(ct);

        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().Where(t => t.Id == venue.TenantId).Select(t => t.DisplayName).SingleAsync(ct);
        return new ReportDto(tenant, venue.Name, venue.Currency, frequency, DateOnly.FromDateTime(start), DateOnly.FromDateTime(end),
            PeriodLabel(frequency, start, end), metrics, top.Select(t => new ReportItemDto(t.Name, t.Quantity, t.Total)).ToList());
    }

    /// <summary>The report as an e-mail: a table of figures with the change from the period before, then the top items.</summary>
    public static (string Subject, string Text, string Html) Render(ReportDto report)
    {
        string Value(ReportMetricDto m, decimal value) => m.Unit switch
        {
            ReportUnit.Money => TransactionService.FormatMoney((long)value, report.Currency),
            ReportUnit.Rating => value == 0 ? "–" : value.ToString("0.0", CultureInfo.InvariantCulture) + " / 5",
            _ => value.ToString("N0", CultureInfo.InvariantCulture)
        };
        static string Change(ReportMetricDto m) => m.Change switch
        {
            null => "",
            > 0 => $"▲ {m.Change.Value * 100:0.#}%",
            < 0 => $"▼ {-m.Change.Value * 100:0.#}%",
            _ => "="
        };

        var frequency = report.Frequency.ToString().ToLowerInvariant();
        var subject = $"{report.BrandName} · {report.VenueName}: {frequency} report, {report.PeriodLabel}";

        var text = new StringBuilder()
            .AppendLine($"{report.BrandName} · {report.VenueName}")
            .AppendLine($"Your {frequency} report for {report.PeriodLabel}")
            .AppendLine();
        foreach (var m in report.Metrics)
            text.AppendLine($"{m.Label,-34} {Value(m, m.Value),16}   was {Value(m, m.Previous)} {Change(m)}");
        if (report.TopItems.Count > 0)
        {
            text.AppendLine().AppendLine("Best sellers");
            foreach (var item in report.TopItems)
                text.AppendLine($"{item.Quantity,4} × {item.Name,-30} {TransactionService.FormatMoney(item.TotalMinor, report.Currency)}");
        }
        text.AppendLine().AppendLine("Amounts include taxes and charges. Sent by DineFlow; change who gets it under Reports.");

        static string E(string s) => WebUtility.HtmlEncode(s);
        var rows = string.Concat(report.Metrics.Select(m =>
        {
            var colour = m.Change is null or 0 ? "#475467" : (m.Change > 0) == (m.Key is not ("refunds" or "failures")) ? "#067647" : "#B42318";
            return $"""<tr><td style="padding:6px 0">{E(m.Label)}</td><td style="padding:6px 8px;text-align:right;font-weight:700">{E(Value(m, m.Value))}</td><td style="padding:6px 8px;text-align:right;color:#475467">{E(Value(m, m.Previous))}</td><td style="padding:6px 0;text-align:right;color:{colour}">{E(Change(m))}</td></tr>""";
        }));
        var items = report.TopItems.Count == 0 ? "" : $"""
            <h3 style="margin:24px 0 8px">Best sellers</h3>
            <table style="width:100%;border-collapse:collapse">{string.Concat(report.TopItems.Select(i =>
                $"""<tr><td style="padding:4px 0">{i.Quantity} × {E(i.Name)}</td><td style="padding:4px 0;text-align:right">{E(TransactionService.FormatMoney(i.TotalMinor, report.Currency))}</td></tr>"""))}</table>
            """;
        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:560px;color:#101828">
              <h2 style="margin:0">{E(report.BrandName)} · {E(report.VenueName)}</h2>
              <p style="color:#475467;margin:4px 0 16px">Your {frequency} report for {E(report.PeriodLabel)}</p>
              <table style="width:100%;border-collapse:collapse;font-size:14px">
                <tr style="color:#475467;font-size:12px;text-transform:uppercase"><th style="text-align:left;padding:4px 0">Figure</th><th style="text-align:right;padding:4px 8px">This period</th><th style="text-align:right;padding:4px 8px">Before</th><th></th></tr>
                {rows}
              </table>
              {items}
              <p style="color:#475467;font-size:12px;margin-top:24px">Amounts include taxes and charges. Sent by DineFlow; change who gets it under Reports.</p>
            </div>
            """;
        return (subject, text.ToString(), html);
    }

    internal static TimeZoneInfo Zone(Venue venue) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>"Tue 21 Oct 2026", "13–19 Oct 2026" or "September 2026".</summary>
    internal static string PeriodLabel(ReportFrequency frequency, DateTime start, DateTime end)
    {
        var c = CultureInfo.InvariantCulture;
        var last = end.AddDays(-1);
        return frequency switch
        {
            ReportFrequency.Monthly => start.ToString("MMMM yyyy", c),
            ReportFrequency.Weekly when start.Month == last.Month => $"{start.Day}–{last.ToString("d MMM yyyy", c)}",
            ReportFrequency.Weekly when start.Year == last.Year => $"{start.ToString("d MMM", c)} – {last.ToString("d MMM yyyy", c)}",
            ReportFrequency.Weekly => $"{start.ToString("d MMM yyyy", c)} – {last.ToString("d MMM yyyy", c)}",
            _ => start.ToString("ddd d MMM yyyy", c)
        };
    }

    private sealed record Figures(decimal Revenue, decimal Tips, decimal Refunds, decimal Orders, decimal Payments, decimal Failures,
        decimal LinkRevenue, decimal Reviews, decimal Rating);

    private async Task<Figures> FiguresAsync(Guid venueId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var ledger = db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(e => e.VenueId == venueId && e.CreatedAt >= from && e.CreatedAt < to);
        async Task<long> Sum(LedgerEntryType type) => await ledger.Where(e => e.Type == type).SumAsync(e => (long?)e.AmountMinor, ct) ?? 0;
        var payments = db.Payments.IgnoreQueryFilters().AsNoTracking().Where(p => p.VenueId == venueId);
        var linkPaymentIds = payments.Where(p => p.PaymentLinkId != null).Select(p => p.Id);
        var reviews = db.Reviews.IgnoreQueryFilters().AsNoTracking().Where(r => r.VenueId == venueId && r.CreatedAt >= from && r.CreatedAt < to);
        var reviewCount = await reviews.CountAsync(ct);

        return new Figures(
            await Sum(LedgerEntryType.BillPayment),
            await Sum(LedgerEntryType.Tip),
            await Sum(LedgerEntryType.Refund),
            await db.Orders.IgnoreQueryFilters().AsNoTracking().CountAsync(o => o.VenueId == venueId && o.Status != OrderStatus.Rejected
                && o.CreatedAt >= from && o.CreatedAt < to, ct),
            await payments.CountAsync(p => p.Status == PaymentStatus.Succeeded && p.SucceededAt >= from && p.SucceededAt < to, ct),
            await payments.CountAsync(p => p.Status == PaymentStatus.Failed && p.UpdatedAt >= from && p.UpdatedAt < to, ct),
            await ledger.Where(e => e.Type == LedgerEntryType.BillPayment && linkPaymentIds.Contains(e.PaymentId)).SumAsync(e => (long?)e.AmountMinor, ct) ?? 0,
            reviewCount,
            reviewCount == 0 ? 0 : Math.Round((decimal)await reviews.AverageAsync(r => (double)r.Rating, ct), 1));
    }

    private static DateTimeOffset Utc(DateTime local, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
}
