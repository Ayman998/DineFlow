namespace DineFlow.Api.Common;

/// <summary>
/// Settings that are fine on a developer's machine but wrong for real guests and money. Outside
/// Development, errors stop the API from starting and warnings are logged at startup, so a
/// half-configured server fails loudly instead of quietly taking payments it can't receipt.
/// </summary>
public static class ProductionChecks
{
    public record Result(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

    public static Result Run(IConfiguration config, bool isDevelopment)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (isDevelopment) return new Result(errors, warnings);

        // The sandbox accepts test cards and marks bills paid without moving money, so it must be
        // switched on deliberately anywhere but a developer's machine (e.g. for a demo environment).
        if (config["Payments:Provider"] is null or "sandbox" && !config.GetValue<bool>("Payments:Sandbox:AllowOutsideDevelopment"))
            errors.Add("The sandbox payment provider only runs in Development. Configure a real provider (Payments:Provider = mastercard), or set Payments:Sandbox:AllowOutsideDevelopment for a demo.");

        if (config.GetValue<bool>("Seed:Enabled"))
            errors.Add("Seed:Enabled must be false outside Development: it creates a demo tenant with a known password. Create venues with the 'provision' command.");

        if (!IsHttps(config["Guest:AppBaseUrl"]))
            errors.Add("Guest:AppBaseUrl must be the guest app's https address: printed QR codes and payment links point to it.");

        if ((config["Email:Provider"] ?? "file") != "smtp" && !config.GetValue<bool>("Email:AllowFileOutsideDevelopment"))
            errors.Add("Email:Provider must be 'smtp' outside Development, or guests never get their receipts. Set Email:AllowFileOutsideDevelopment for a demo.");

        if (config["Payments:Provider"] == "mastercard" && config["Payments:Mastercard:GatewayUrl"] is { } gateway
            && gateway.Contains("test-gateway", StringComparison.OrdinalIgnoreCase))
            warnings.Add($"Payments:Mastercard:GatewayUrl is the test gateway ({gateway}): cards aren't charged. Switch to the live gateway URL your acquirer gives you before opening to guests.");

        if ((config["WhatsApp:Provider"] ?? "outbox") != "meta")
            warnings.Add("WhatsApp:Provider is 'outbox': payment alerts are written to files on the server, not sent.");

        if (config["Payments:TermsUrl"] is not { } terms || terms.Contains("example", StringComparison.OrdinalIgnoreCase) || !IsHttps(terms))
            warnings.Add("Payments:TermsUrl should be your published https terms page; guests accept it before paying.");

        if ((config.GetSection("Cors:Origins").Get<string[]>() ?? []).Any(o => o.Contains("localhost", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Cors:Origins still lists localhost. Behind the bundled Caddy proxy the apps call the API on their own origin, so the list can be empty.");

        return new Result(errors, warnings);
    }

    private static bool IsHttps(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !uri.IsLoopback;
}
