namespace DineFlow.Application.Abstractions;

/// <summary>
/// The canonical POS adapter contract (blueprint section 6). Every connector implements it, so the
/// guest app and the rest of DineFlow never change when a POS is added. A connector declares what
/// its POS can do in <see cref="Capabilities"/>; DineFlow only asks for those.
/// </summary>
/// <remarks>
/// Errors: throw <see cref="PosUnavailableException"/> when the POS can't be reached or didn't answer
/// (DineFlow retries, for as long as it takes), and <see cref="PosRejectedException"/> when the POS
/// refused the request for a reason retrying won't fix.
/// </remarks>
public interface IPosConnector
{
    /// <summary>The provider key stored on the connection, e.g. "foodics".</summary>
    string Provider { get; }
    string DisplayName { get; }
    PosCapabilities Capabilities { get; }
    /// <summary>The connector needs an access token to work.</summary>
    bool RequiresAccessToken { get; }

    Task<PosHealth> HealthCheckAsync(PosContext context, CancellationToken ct);

    Task<IReadOnlyList<PosCheckSummary>> GetOpenChecksAsync(PosContext context, string tableRef, CancellationToken ct);

    Task<PosCheck> GetCheckDetailsAsync(PosContext context, string checkRef, CancellationToken ct);

    /// <summary>Sends an order; opens a check for the table when <see cref="PosOrder.CheckRef"/> is null, otherwise adds to it.</summary>
    Task<PosOrderResult> CreateOrderAsync(PosContext context, PosOrder order, CancellationToken ct);

    /// <summary>
    /// Applies a payment (and tip) to the check. <see cref="PosPayment.ExternalPaymentId"/> is DineFlow's
    /// payment id; it is recorded on the tender so DineFlow can tell whether a payment was already applied.
    /// </summary>
    Task<PosPostingResult> PostPaymentAsync(PosContext context, PosPayment payment, CancellationToken ct);

    /// <summary>Closes a fully paid check (capability <see cref="PosCapabilities.AutoClose"/>).</summary>
    Task CloseCheckAsync(PosContext context, string checkRef, CancellationToken ct);
}

/// <summary>Connector capabilities (blueprint section 6).</summary>
[Flags]
public enum PosCapabilities
{
    None = 0,
    PaymentPosting = 1,
    AutoClose = 2,
    TipPosting = 4,
    MenuSync = 8,
    OrderCreate = 16,
    Refund = 32,
    WebhookEvents = 64
}

/// <param name="AccessToken">Decrypted for this call only.</param>
public record PosContext(Guid VenueId, string? BranchRef, string? AccessToken);

public record PosHealth(bool Healthy, string Message);

public record PosCheckSummary(string CheckRef, string TableRef, long TotalMinor, long PaidMinor, bool IsOpen);

/// <param name="Payments">Tenders on the check, each with the external payment id it was posted with (when there is one).</param>
public record PosCheck(string CheckRef, string TableRef, long TotalMinor, long PaidMinor, bool IsOpen,
    IReadOnlyList<PosCheckLine> Lines, IReadOnlyList<PosCheckPayment> Payments);

public record PosCheckLine(string Name, int Quantity, long LineTotalMinor);

public record PosCheckPayment(string PostingRef, string? ExternalPaymentId, long AmountMinor, long TipMinor);

/// <param name="ExternalOrderId">DineFlow's order id, so a retried send can't add the order twice.</param>
public record PosOrder(string? CheckRef, string TableRef, string ExternalOrderId, int Number, string Currency,
    IReadOnlyList<PosOrderLine> Lines, long TotalMinor, string? Note);

/// <param name="ProductRef">The POS product, from the menu item's mapping; required by POSes that only take their own items.</param>
public record PosOrderLine(string Name, string? ProductRef, int Quantity, long UnitPriceMinor, long LineTotalMinor, IReadOnlyList<string> Options);

public record PosOrderResult(string CheckRef, string OrderRef);

/// <param name="AmountMinor">What goes against the bill (excluding tip).</param>
public record PosPayment(string CheckRef, string ExternalPaymentId, long AmountMinor, long TipMinor, string Currency, string Method, string Reference);

/// <param name="RemainingMinor">What the POS says is still owed on the check, when it says.</param>
public record PosPostingResult(string PostingRef, long? RemainingMinor);

/// <summary>The POS couldn't be reached or didn't answer in time. <see cref="MayHaveApplied"/> when it might have done it anyway.</summary>
public class PosUnavailableException(string message, bool mayHaveApplied = false, Exception? inner = null) : Exception(message, inner)
{
    public bool MayHaveApplied { get; } = mayHaveApplied;
}

/// <summary>The POS refused the request; retrying won't help until something is fixed.</summary>
public class PosRejectedException(string message) : Exception(message);

/// <summary>Finds the connector for a provider key.</summary>
public interface IPosConnectorRegistry
{
    IReadOnlyList<IPosConnector> All { get; }
    IPosConnector? Find(string provider);
}

/// <summary>Encrypts secrets (POS access tokens) before they're stored, and decrypts them for use.</summary>
public interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}
