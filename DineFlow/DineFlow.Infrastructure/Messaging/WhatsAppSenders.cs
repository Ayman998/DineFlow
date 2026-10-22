using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DineFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Messaging;

public class WhatsAppOptions
{
    public const string Section = "WhatsApp";
    public const string Outbox = "outbox";
    public const string Meta = "meta";

    /// <summary>"outbox" writes messages to <see cref="OutboxPath"/> instead of sending them; "meta" uses the WhatsApp Cloud API.</summary>
    public string Provider { get; set; } = Outbox;

    /// <summary>Folder for outbox messages, relative to the API's content root.</summary>
    public string OutboxPath { get; set; } = "App_Data/whatsapp";

    public MetaWhatsAppOptions MetaApi { get; set; } = new();
}

/// <summary>WhatsApp Business Platform (Cloud API) settings. The access token is a secret: set it with user-secrets or a key vault.</summary>
public class MetaWhatsAppOptions
{
    public string BaseUrl { get; set; } = "https://graph.facebook.com";
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>The sending number's id from WhatsApp Manager (not the phone number itself).</summary>
    public string PhoneNumberId { get; set; } = "";
    public string AccessToken { get; set; } = "";
    /// <summary>An approved template with four body parameters, e.g. "{{1}}: {{2}} paid. {{3}}. {{4}}".</summary>
    public string TemplateName { get; set; } = "payment_alert";
    public string TemplateLanguage { get; set; } = "en";
}

/// <summary>
/// Development WhatsApp: writes each message as a JSON file (recipient, text and template
/// parameters) instead of sending it, like the e-mail outbox.
/// </summary>
public class OutboxWhatsAppSender(IOptions<WhatsAppOptions> options, string contentRoot, ILogger<OutboxWhatsAppSender> logger) : IWhatsAppSender
{
    // A file for people to read, never served to a browser, so "·" and "+" can stay as they are.
    private static readonly JsonSerializerOptions OutboxJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Name => WhatsAppOptions.Outbox;

    public async Task<string> SendAsync(WhatsAppMessage message, CancellationToken ct)
    {
        var folder = Path.GetFullPath(Path.Combine(contentRoot, options.Value.OutboxPath));
        Directory.CreateDirectory(folder);
        var id = $"outbox_{Guid.NewGuid():N}";
        var path = Path.Combine(folder, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{message.To.TrimStart('+')}-{id[7..15]}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { id, message.To, message.Text, message.Parameters, at = DateTimeOffset.UtcNow },
            OutboxJson), ct);
        logger.LogInformation("Wrote WhatsApp message for {To} to {Path}", message.To, path);
        return id;
    }
}

/// <summary>
/// Sends through the WhatsApp Cloud API (POST /{version}/{phone-number-id}/messages) using the
/// approved template. A refusal from Meta is thrown with Meta's reason, so it shows in the delivery log.
/// </summary>
public class MetaWhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options) : IWhatsAppSender
{
    public string Name => WhatsAppOptions.Meta;

    public async Task<string> SendAsync(WhatsAppMessage message, CancellationToken ct)
    {
        var meta = options.Value.MetaApi;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{meta.BaseUrl.TrimEnd('/')}/{meta.ApiVersion}/{meta.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = message.To.TrimStart('+'),
                type = "template",
                template = new
                {
                    name = meta.TemplateName,
                    language = new { code = meta.TemplateLanguage },
                    components = new[]
                    {
                        new { type = "body", parameters = message.Parameters.Select(p => new { type = "text", text = p }).ToArray() }
                    }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", meta.AccessToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<MetaResponse>(ct).ConfigureAwait(false);
            throw new HttpRequestException($"WhatsApp refused the message ({(int)response.StatusCode}): {error?.Error?.Message ?? response.ReasonPhrase}");
        }
        var body = await response.Content.ReadFromJsonAsync<MetaResponse>(ct);
        return body?.Messages?.FirstOrDefault()?.Id ?? throw new HttpRequestException("WhatsApp didn't return a message id.");
    }

    private sealed record MetaResponse(
        [property: JsonPropertyName("messages")] MetaMessage[]? Messages,
        [property: JsonPropertyName("error")] MetaError? Error);

    private sealed record MetaMessage([property: JsonPropertyName("id")] string Id);

    private sealed record MetaError([property: JsonPropertyName("message")] string? Message);
}
