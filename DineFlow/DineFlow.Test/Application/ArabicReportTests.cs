using System.Globalization;
using DineFlow.Application.Reports;
using DineFlow.Domain.Notifications;

namespace DineFlow.Test.Application;

/// <summary>The report e-mail for staff who read Arabic: Arabic figures, Jordanian month names, right to left.</summary>
public class ArabicReportTests
{
    private static ReportDto Report(ReportFrequency frequency, DateOnly start, DateOnly end) => new("Test Bistro", "Abdoun", "JOD", frequency, start, end,
        "English label", [new ReportMetricDto("revenue", "Revenue", ReportUnit.Money, 125_500, 100_000, 0.255)], [new ReportItemDto("Mansaf", 4, 48_000)]);

    [Theory]
    [InlineData(ReportFrequency.Daily, "2026-10-20", "2026-10-21", "الثلاثاء 20 تشرين الأول 2026")]
    [InlineData(ReportFrequency.Weekly, "2026-10-13", "2026-10-20", "13–19 تشرين الأول 2026")]
    [InlineData(ReportFrequency.Weekly, "2026-09-28", "2026-10-05", "28 أيلول – 4 تشرين الأول 2026")]
    [InlineData(ReportFrequency.Weekly, "2026-12-28", "2027-01-04", "28 كانون الأول 2026 – 3 كانون الثاني 2027")]
    [InlineData(ReportFrequency.Monthly, "2026-09-01", "2026-10-01", "أيلول 2026")]
    public void The_period_uses_the_month_names_read_in_jordan(ReportFrequency frequency, string start, string end, string expected)
    {
        var (subject, text, _) = ReportBuilder.Render(Report(frequency, DateOnly.Parse(start, CultureInfo.InvariantCulture), DateOnly.Parse(end, CultureInfo.InvariantCulture)), arabic: true);

        Assert.EndsWith($"عن {expected}", subject);
        Assert.Contains("تقريرك", text);
        Assert.DoesNotContain("English label", text);
    }

    [Fact]
    public void Figures_and_layout_are_arabic_and_english_stays_as_it_was()
    {
        var report = Report(ReportFrequency.Daily, new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21));

        var (arabicSubject, arabicText, arabicHtml) = ReportBuilder.Render(report, arabic: true);
        var (englishSubject, englishText, englishHtml) = ReportBuilder.Render(report);

        Assert.Equal("Test Bistro · Abdoun: التقرير اليومي عن الثلاثاء 20 تشرين الأول 2026", arabicSubject);
        Assert.Contains("الإيرادات", arabicText);
        Assert.Contains("JOD 125.500", arabicText);
        Assert.Contains("الأكثر مبيعاً", arabicText);
        Assert.Contains("dir=\"rtl\"", arabicHtml);
        Assert.Equal("Test Bistro · Abdoun: daily report, English label", englishSubject);
        Assert.Contains("Revenue", englishText);
        Assert.Contains("▲ 25.5%", englishText);
        Assert.Contains("dir=\"ltr\"", englishHtml);
    }
}
