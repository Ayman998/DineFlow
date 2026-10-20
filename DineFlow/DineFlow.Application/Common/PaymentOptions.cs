using System.ComponentModel.DataAnnotations;

namespace DineFlow.Application.Common;

public class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Which <c>IPaymentProvider</c> to use, e.g. "sandbox".</summary>
    [Required] public string Provider { get; set; } = "sandbox";

    /// <summary>Version of the Terms and Conditions guests accept by paying (recorded on each payment).</summary>
    [Required] public string TermsVersion { get; set; } = "2026-10";

    [Required, Url] public string TermsUrl { get; set; } = "https://dineflow.example/terms";
}
