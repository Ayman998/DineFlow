using System.Text.RegularExpressions;
using DineFlow.Domain.Common;

namespace DineFlow.Domain.Notifications;

/// <summary>
/// A merchant or staff number that gets a WhatsApp message for every successful payment at the
/// venue (blueprint P1-STAFF-02). Numbers are kept in E.164 form, e.g. +962791234567.
/// </summary>
public class AlertContact : Entity, ITenantOwned
{
    public const int MaxPerVenue = 10;

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;

    private AlertContact() { }

    public AlertContact(Guid tenantId, Guid venueId, string name, string phoneNumber, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        Name = Text.Required(name, 60, "Name");
        PhoneNumber = NormalizePhone(phoneNumber);
        CreatedAt = now;
    }

    /// <summary>"+962 79 123 4567" → "+962791234567"; throws unless it's a full international number.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = SpacingPattern.Replace(phone ?? "", "");
        if (digits.StartsWith("00")) digits = "+" + digits[2..];
        if (!E164Pattern.IsMatch(digits))
            throw new DomainException("Enter the number in international format, starting with + and the country code, e.g. +962 79 123 4567.");
        return digits;
    }

    // Plain regexes rather than [GeneratedRegex]: the source generator doesn't run in every IDE build.
    private static readonly Regex SpacingPattern = new(@"[\s\-().]", RegexOptions.Compiled);
    private static readonly Regex E164Pattern = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);
}
