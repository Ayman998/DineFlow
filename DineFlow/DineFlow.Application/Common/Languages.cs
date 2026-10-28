using System.Globalization;
using DineFlow.Domain.Venues;

namespace DineFlow.Application.Common;

/// <summary>
/// The languages DineFlow writes in: English, and Arabic for venues in Jordan. Text the API
/// generates (receipts, alerts, report e-mails) picks one with <see cref="IsArabic"/>. Arabic dates
/// use the month names read in Jordan (كانون الثاني, شباط, …) and keep Latin digits, like the apps.
/// </summary>
public static class Languages
{
    public const string English = "en";
    public const string Arabic = "ar";

    public static bool IsArabic(string? language) =>
        language is not null && (language.Equals(Arabic, StringComparison.OrdinalIgnoreCase)
            || language.StartsWith(Arabic + "-", StringComparison.OrdinalIgnoreCase));

    private static readonly string[] ArabicMonths =
    [
        "كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران",
        "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول"
    ];

    private static readonly string[] ArabicDays = ["الأحد", "الاثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة", "السبت"];

    public static string ArabicMonth(int month) => ArabicMonths[month - 1];

    public static string ArabicDay(DayOfWeek day) => ArabicDays[(int)day];

    /// <summary>"Table 4", or "طاولة 4" in Arabic.</summary>
    public static string LocationName(ServiceLocation location, bool arabic) =>
        !arabic ? location.DisplayName : location.Type switch
        {
            ServiceLocationType.Table => $"طاولة {location.Label}",
            ServiceLocationType.Counter => $"كاونتر {location.Label}",
            ServiceLocationType.HotelRoom => $"غرفة {location.Label}",
            ServiceLocationType.PoolBeach => $"المسبح/الشاطئ {location.Label}",
            _ => location.Label
        };

    /// <summary>The area's Arabic name for Arabic readers when it has one, otherwise its name.</summary>
    public static string AreaName(DiningArea area, bool arabic) => arabic && area.NameAr is { } nameAr ? nameAr : area.Name;

    /// <summary>"21 Oct 2026 19:05", or "21 تشرين الأول 2026 19:05" in Arabic.</summary>
    public static string DateTime(DateTimeOffset at, bool arabic) =>
        arabic
            ? $"{at.Day} {ArabicMonth(at.Month)} {at.Year} {at.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : at.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture);
}
