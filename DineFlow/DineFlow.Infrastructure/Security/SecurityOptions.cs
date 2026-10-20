using System.ComponentModel.DataAnnotations;

namespace DineFlow.Infrastructure.Security;

public class JwtOptions
{
    public const string Section = "Jwt";

    [Required] public string Issuer { get; set; } = string.Empty;
    [Required] public string Audience { get; set; } = string.Empty;

    /// <summary>Base64 HMAC-SHA256 key, at least 32 bytes. Keep it in user-secrets or a key vault.</summary>
    [Required] public string SigningKey { get; set; } = string.Empty;

    public TimeSpan StaffTokenLifetime { get; set; } = TimeSpan.FromHours(12);

    public byte[] SigningKeyBytes() => KeyBytes.Decode(SigningKey);
}

public class QrOptions
{
    public const string Section = "Qr";

    /// <summary>
    /// Base64 HMAC key, at least 32 bytes, that QR tokens are derived from. Changing it
    /// invalidates every printed QR code, so treat it like a signing key.
    /// </summary>
    [Required] public string TokenKey { get; set; } = string.Empty;

    public byte[] TokenKeyBytes() => KeyBytes.Decode(TokenKey);
}

internal static class KeyBytes
{
    public const int MinimumLength = 32;

    public static byte[] Decode(string base64)
    {
        try { return Convert.FromBase64String(base64); }
        catch (FormatException) { return []; }
    }

    public static bool IsStrong(string base64) => Decode(base64).Length >= MinimumLength;
}
