using DineFlow.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DineFlow.Infrastructure.Email;

public class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    /// <summary>587 with STARTTLS is what Microsoft 365, Google Workspace, SendGrid, Mailgun and SES all offer.</summary>
    public int Port { get; set; } = 587;

    /// <summary>StartTls, SslOnConnect (port 465) or None (a relay on a private network only).</summary>
    public string Security { get; set; } = "StartTls";

    public string Username { get; set; } = string.Empty;

    /// <summary>A secret: set it with user-secrets or an environment variable, never in appsettings.json.</summary>
    public string Password { get; set; } = string.Empty;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    public SecureSocketOptions SocketOptions => Security.ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" => SecureSocketOptions.SslOnConnect,
        "none" => SecureSocketOptions.None,
        _ => throw new InvalidOperationException($"Email:Smtp:Security must be StartTls, SslOnConnect or None, not '{Security}'.")
    };
}

/// <summary>
/// Sends e-mail (receipts, payment links, reports) through any SMTP server. Failures throw, so the
/// notification outbox retries the message with its usual backoff (see NotificationWorker).
/// </summary>
public class SmtpEmailSender(IOptions<EmailOptions> email, IOptions<SmtpOptions> smtp, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var mime = Build(email.Value, message);
        var settings = smtp.Value;

        using var client = new SmtpClient { Timeout = (int)settings.Timeout.TotalMilliseconds };
        await client.ConnectAsync(settings.Host, settings.Port, settings.SocketOptions, ct);
        if (settings.Username.Length > 0) await client.AuthenticateAsync(settings.Username, settings.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
        logger.LogInformation("Sent e-mail \"{Subject}\" via {Host}", message.Subject, settings.Host);
    }

    /// <summary>A plain-text part with the HTML alternative, so every mail client shows something readable.</summary>
    public static MimeMessage Build(EmailOptions options, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(options.From));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.TextBody, HtmlBody = message.HtmlBody }.ToMessageBody();
        return mime;
    }
}
