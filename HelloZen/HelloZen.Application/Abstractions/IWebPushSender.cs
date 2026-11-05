namespace HelloZen.Application.Abstractions;

/// <summary>
/// Sends Web Push notifications to guests' phones, e.g. "your pickup order is ready". Delivery is
/// best effort and happens in the background; the tracking page also rings while it's open, so a
/// lost push never leaves the guest without an alert.
/// </summary>
public interface IWebPushSender
{
    /// <summary>
    /// The VAPID public key browsers subscribe with: the uncompressed P-256 point, base64url without
    /// padding. Null when push is switched off.
    /// </summary>
    Task<string?> PublicKeyAsync(CancellationToken ct);

    /// <summary>Queues a notification for one browser. Never throws; failures are logged.</summary>
    void Queue(WebPushSubscription subscription, WebPushNotification notification);
}

/// <summary>A browser's push subscription, as <c>PushSubscription.toJSON()</c> gives it (keys base64url).</summary>
public record WebPushSubscription(string Endpoint, string P256dh, string Auth);

/// <summary>
/// What the phone shows. <paramref name="Url"/> (a path in the guest app, e.g. /track/{id}) opens
/// when it's tapped; a later notification with the same <paramref name="Tag"/> replaces this one.
/// <paramref name="Lang"/> is "en" or "ar" (shown right to left).
/// </summary>
public record WebPushNotification(string Title, string Body, string Url, string Tag, string Lang);
