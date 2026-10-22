namespace DineFlow.Application.Common;

/// <summary>
/// Switches for features that can be turned off per environment without a release (blueprint E15
/// "feature flags"), e.g. while a provider is being onboarded. A disabled feature's endpoints answer
/// 404 feature_disabled, the merchant app hides it, and its background work stops.
/// </summary>
public class FeatureOptions
{
    public const string Section = "Features";

    public bool PaymentLinks { get; set; } = true;
    public bool WhatsAppAlerts { get; set; } = true;
    public bool EmailReports { get; set; } = true;

    public bool IsEnabled(string feature) => feature switch
    {
        nameof(PaymentLinks) => PaymentLinks,
        nameof(WhatsAppAlerts) => WhatsAppAlerts,
        nameof(EmailReports) => EmailReports,
        _ => false
    };
}
