using System.Text.Json;
using DineFlow.Application.Common;

namespace DineFlow.Application.Notifications;

/// <summary>
/// What a queued WhatsApp message keeps in <c>NotificationDelivery.TemplateParameters</c>: the
/// template's language and its body parameters, so a retry or resend uses the same translation of
/// the template the parameters were written for. Messages queued before alerts had a language
/// hold just the parameters, which were English.
/// </summary>
internal sealed record WhatsAppTemplate(string Language, IReadOnlyList<string> Parameters)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static WhatsAppTemplate FromJson(string? json) =>
        json?.TrimStart().StartsWith('{') == true
            ? JsonSerializer.Deserialize<WhatsAppTemplate>(json, Json) ?? new(Languages.English, [])
            : new(Languages.English, JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? []);
}
