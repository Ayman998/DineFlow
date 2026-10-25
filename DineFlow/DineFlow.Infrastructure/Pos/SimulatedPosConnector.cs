using DineFlow.Application.Abstractions;

namespace DineFlow.Infrastructure.Pos;

/// <summary>How the simulated POS behaves, to try the failure cases from blueprint 13.1 by hand.</summary>
public enum SimulatorMode
{
    Online,
    /// <summary>Can't be reached: nothing is applied, and DineFlow keeps retrying.</summary>
    Offline,
    /// <summary>The next request is applied but the answer never arrives (once), like a POS timing out after taking the tender.</summary>
    TimeoutAfterApplying,
    /// <summary>Refuses every request, like a POS rejecting a tender.</summary>
    Refuse
}

/// <summary>
/// A POS that lives in memory, for development and tests (state is lost when the API restarts).
/// It follows the canonical contract like a real connector: checks per table, orders and tenders
/// recognised by DineFlow's ids so a retry is never applied twice, and closing only when paid.
/// </summary>
public class SimulatedPosConnector : IPosConnector
{
    public const string ProviderName = "simulator";

    private readonly object _gate = new();
    private readonly List<SimCheck> _checks = [];
    private int _next = 1;

    public string Provider => ProviderName;
    public string DisplayName => "Simulated POS (for testing)";
    public PosCapabilities Capabilities => PosCapabilities.PaymentPosting | PosCapabilities.AutoClose | PosCapabilities.TipPosting | PosCapabilities.OrderCreate;
    public bool RequiresAccessToken => false;

    public SimulatorMode Mode { get; set; } = SimulatorMode.Online;

    public Task<PosHealth> HealthCheckAsync(PosContext context, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(Mode == SimulatorMode.Offline
                ? new PosHealth(false, "The simulated POS is offline.")
                : new PosHealth(true, $"Simulated POS is online · {_checks.Count(c => c.VenueId == context.VenueId && c.IsOpen)} open checks"));
        }
    }

    public Task<IReadOnlyList<PosCheckSummary>> GetOpenChecksAsync(PosContext context, string tableRef, CancellationToken ct)
    {
        lock (_gate)
        {
            Reachable();
            IReadOnlyList<PosCheckSummary> open = _checks.Where(c => c.VenueId == context.VenueId && c.TableRef == tableRef && c.IsOpen)
                .Select(c => new PosCheckSummary(c.Ref, c.TableRef, c.TotalMinor, c.PaidMinor, true)).ToList();
            return Task.FromResult(open);
        }
    }

    public Task<PosCheck> GetCheckDetailsAsync(PosContext context, string checkRef, CancellationToken ct)
    {
        lock (_gate)
        {
            Reachable();
            return Task.FromResult(Find(context, checkRef).ToCheck());
        }
    }

    public Task<PosOrderResult> CreateOrderAsync(PosContext context, PosOrder order, CancellationToken ct)
    {
        lock (_gate)
        {
            Reachable();
            var existing = _checks.FirstOrDefault(c => c.VenueId == context.VenueId && c.OrderIds.ContainsKey(order.ExternalOrderId));
            if (existing is not null) return Task.FromResult(new PosOrderResult(existing.Ref, existing.OrderIds[order.ExternalOrderId]));

            var check = order.CheckRef is { } checkRef ? Find(context, checkRef) : null;
            if (check is null)
            {
                check = new SimCheck(context.VenueId, $"SIM-{_next++:D4}", order.TableRef);
                _checks.Add(check);
            }
            if (!check.IsOpen) throw new PosRejectedException($"Check {check.Ref} is already closed.");

            var orderRef = $"{check.Ref}-{check.OrderIds.Count + 1}";
            check.OrderIds[order.ExternalOrderId] = orderRef;
            check.Lines.AddRange(order.Lines.Select(l => new PosCheckLine(l.Name, l.Quantity, l.LineTotalMinor)));
            check.TotalMinor += order.TotalMinor;
            AfterApplying();
            return Task.FromResult(new PosOrderResult(check.Ref, orderRef));
        }
    }

    public Task<PosPostingResult> PostPaymentAsync(PosContext context, PosPayment payment, CancellationToken ct)
    {
        lock (_gate)
        {
            Reachable();
            var check = Find(context, payment.CheckRef);
            var existing = check.Payments.FirstOrDefault(p => p.ExternalPaymentId == payment.ExternalPaymentId);
            if (existing is not null) return Task.FromResult(new PosPostingResult(existing.PostingRef, check.TotalMinor - check.PaidMinor));
            if (!check.IsOpen) throw new PosRejectedException($"Check {check.Ref} is already closed.");

            var tender = new PosCheckPayment($"{check.Ref}-T{check.Payments.Count + 1}", payment.ExternalPaymentId, payment.AmountMinor, payment.TipMinor);
            check.Payments.Add(tender);
            AfterApplying();
            return Task.FromResult(new PosPostingResult(tender.PostingRef, check.TotalMinor - check.PaidMinor));
        }
    }

    public Task CloseCheckAsync(PosContext context, string checkRef, CancellationToken ct)
    {
        lock (_gate)
        {
            Reachable();
            var check = Find(context, checkRef);
            if (check.PaidMinor < check.TotalMinor)
                throw new PosRejectedException($"Check {check.Ref} still has {check.TotalMinor - check.PaidMinor} (minor units) to pay.");
            check.IsOpen = false;
            return Task.CompletedTask;
        }
    }

    /// <summary>Every check the simulator holds for a venue, newest first, for the settings page.</summary>
    public IReadOnlyList<PosCheck> Checks(Guid venueId)
    {
        lock (_gate) return _checks.Where(c => c.VenueId == venueId).Reverse().Select(c => c.ToCheck()).ToList();
    }

    private void Reachable()
    {
        switch (Mode)
        {
            case SimulatorMode.Offline: throw new PosUnavailableException("The simulated POS is offline.");
            case SimulatorMode.Refuse: throw new PosRejectedException("The simulated POS refused the request.");
        }
    }

    /// <summary>In "timeout" mode the change is kept but the answer is lost, once.</summary>
    private void AfterApplying()
    {
        if (Mode != SimulatorMode.TimeoutAfterApplying) return;
        Mode = SimulatorMode.Online;
        throw new PosUnavailableException("The simulated POS didn't answer in time.", mayHaveApplied: true);
    }

    private SimCheck Find(PosContext context, string checkRef) =>
        _checks.FirstOrDefault(c => c.VenueId == context.VenueId && c.Ref == checkRef)
        ?? throw new PosRejectedException($"The POS has no check {checkRef}.");

    private sealed class SimCheck(Guid venueId, string reference, string tableRef)
    {
        public Guid VenueId { get; } = venueId;
        public string Ref { get; } = reference;
        public string TableRef { get; } = tableRef;
        public Dictionary<string, string> OrderIds { get; } = [];
        public List<PosCheckLine> Lines { get; } = [];
        public List<PosCheckPayment> Payments { get; } = [];
        public long TotalMinor { get; set; }
        public long PaidMinor => Payments.Sum(p => p.AmountMinor);
        public bool IsOpen { get; set; } = true;

        public PosCheck ToCheck() => new(Ref, TableRef, TotalMinor, PaidMinor, IsOpen, Lines.ToList(), Payments.ToList());
    }
}
