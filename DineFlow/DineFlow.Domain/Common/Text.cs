namespace DineFlow.Domain.Common;

/// <summary>Trimming and length rules for user-entered names and descriptions.</summary>
public static class Text
{
    public static string Required(string? value, int maxLength, string field)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) throw new DomainException($"{field} is required.");
        if (trimmed.Length > maxLength) throw new DomainException($"{field} must be {maxLength} characters or fewer.");
        return trimmed;
    }

    public static string? Optional(string? value, int maxLength, string field = "Text")
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > maxLength) throw new DomainException($"{field} must be {maxLength} characters or fewer.");
        return trimmed;
    }
}
