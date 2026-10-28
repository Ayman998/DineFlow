namespace DineFlow.Application.Abstractions;

/// <summary>
/// Sends WhatsApp messages (blueprint P1-STAFF-02). Business-initiated WhatsApp messages must use a
/// template the provider has approved, so a message carries the template's parameters as well as
/// the same text in full for logs and the development outbox.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>Where messages go, e.g. "outbox" in development or "meta" for the WhatsApp Cloud API.</summary>
    string Name { get; }

    /// <summary>Sends the message and returns the provider's message id. Throws when the provider refuses it or can't be reached.</summary>
    Task<string> SendAsync(WhatsAppMessage message, CancellationToken ct);
}

/// <param name="To">E.164 number, e.g. +971501234567.</param>
/// <param name="Parameters">The template's body parameters, in order.</param>
/// <param name="Text">The message as it reads once the template is filled in.</param>
/// <param name="Language">"en" or "ar": which of the template's translations the parameters are written for.</param>
public record WhatsAppMessage(string To, IReadOnlyList<string> Parameters, string Text, string Language = "en");
