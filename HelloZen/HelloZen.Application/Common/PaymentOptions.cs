using System.ComponentModel.DataAnnotations;

namespace HelloZen.Application.Common;

public class PaymentOptions
{
    public const string Section = "Payments";

    /// <summary>Which <c>IPaymentProvider</c> to use, e.g. "sandbox".</summary>
    [Required] public string Provider { get; set; } = "sandbox";

    /// <summary>
    /// Version of the Terms and Conditions guests accept by paying (recorded on each payment). Bump it
    /// whenever the terms text changes (web/projects/guest/src/app/pages/legal/legal.i18n.ts).
    /// </summary>
    [Required] public string TermsVersion { get; set; } = "2026-11";

    /// <summary>The terms guests accept. "/terms" is the guest app's own page on whatever domain it runs.</summary>
    [Required] public string TermsUrl { get; set; } = "/terms";
}
