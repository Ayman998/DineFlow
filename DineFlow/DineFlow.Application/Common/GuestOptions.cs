using System.ComponentModel.DataAnnotations;

namespace DineFlow.Application.Common;

public class GuestOptions
{
    public const string Section = "Guest";

    /// <summary>Base URL of the guest web app; QR codes point to {AppBaseUrl}/q/{token}.</summary>
    [Required, Url]
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>How long a guest session stays valid after scanning.</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(4);
}
