using System.Text;
using DineFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Email;

public class EmailOptions
{
    public const string Section = "Email";
    public const string File = "file";
    public const string Smtp = "smtp";

    /// <summary>"file" writes .eml files (development); "smtp" sends them (see SmtpEmailSender).</summary>
    public string Provider { get; set; } = File;

    public string From { get; set; } = "receipts@dineflow.local";

    /// <summary>Folder for .eml files, relative to the API's content root.</summary>
    public string OutboxPath { get; set; } = "App_Data/outbox";
}

/// <summary>
/// Development e-mail: writes each message as an .eml file you can open in any mail app, instead
/// of sending it. Production sets Email:Provider to "smtp".
/// </summary>
public class FileEmailSender(IOptions<EmailOptions> options, string contentRoot, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var folder = Path.GetFullPath(Path.Combine(contentRoot, options.Value.OutboxPath));
        Directory.CreateDirectory(folder);

        var boundary = $"df-{Guid.NewGuid():N}";
        var eml = new StringBuilder()
            .AppendLine($"From: {options.Value.From}")
            .AppendLine($"To: {message.To}")
            .AppendLine($"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Subject))}?=")
            .AppendLine($"Date: {DateTimeOffset.UtcNow:r}")
            .AppendLine("MIME-Version: 1.0")
            .AppendLine($"Content-Type: multipart/alternative; boundary=\"{boundary}\"")
            .AppendLine()
            .AppendLine($"--{boundary}")
            .AppendLine("Content-Type: text/plain; charset=utf-8")
            .AppendLine()
            .AppendLine(message.TextBody)
            .AppendLine($"--{boundary}")
            .AppendLine("Content-Type: text/html; charset=utf-8")
            .AppendLine()
            .AppendLine(message.HtmlBody)
            .AppendLine($"--{boundary}--")
            .ToString();

        var safeTo = new string(message.To.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        var path = Path.Combine(folder, $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeTo}.eml");
        await File.WriteAllTextAsync(path, eml, Encoding.UTF8, ct);
        logger.LogInformation("Wrote e-mail \"{Subject}\" to {Path}", message.Subject, path);
    }
}
