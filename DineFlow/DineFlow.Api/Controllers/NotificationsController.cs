using DineFlow.Api.Common;
using DineFlow.Application.Audit;
using DineFlow.Application.Common;
using DineFlow.Application.Notifications;
using DineFlow.Application.Reports;
using DineFlow.Domain.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DineFlow.Api.Controllers;

/// <summary>WhatsApp payment alerts (blueprint P1-STAFF-02): who gets them, tests, and the delivery log.</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant")]
public class MerchantNotificationsController(AlertService alerts) : ControllerBase
{
    [HttpGet("venues/{venueId:guid}/alerts")]
    [RequiresFeature(nameof(FeatureOptions.WhatsAppAlerts))]
    public Task<AlertSettingsDto> Alerts(Guid venueId, CancellationToken ct) => alerts.GetAsync(venueId, ct);

    [HttpPut("venues/{venueId:guid}/alerts")]
    [Authorize(Policy = Policies.MerchantManager)]
    [RequiresFeature(nameof(FeatureOptions.WhatsAppAlerts))]
    public Task<AlertSettingsDto> SetAlerts(Guid venueId, AlertToggleRequest request, CancellationToken ct) =>
        alerts.SetEnabledAsync(venueId, request.Enabled, User.UserId(), ct);

    [HttpPost("venues/{venueId:guid}/alerts/contacts")]
    [Authorize(Policy = Policies.MerchantManager)]
    [RequiresFeature(nameof(FeatureOptions.WhatsAppAlerts))]
    public Task<AlertSettingsDto> AddContact(Guid venueId, AlertContactRequest request, CancellationToken ct) =>
        alerts.AddContactAsync(venueId, request, User.UserId(), ct);

    [HttpDelete("alerts/contacts/{contactId:guid}")]
    [Authorize(Policy = Policies.MerchantManager)]
    [RequiresFeature(nameof(FeatureOptions.WhatsAppAlerts))]
    public Task<AlertSettingsDto> RemoveContact(Guid contactId, CancellationToken ct) => alerts.RemoveContactAsync(contactId, User.UserId(), ct);

    /// <summary>Queues a test message to every alert number.</summary>
    [HttpPost("venues/{venueId:guid}/alerts/test")]
    [Authorize(Policy = Policies.MerchantManager)]
    [RequiresFeature(nameof(FeatureOptions.WhatsAppAlerts))]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public async Task<IActionResult> Test(Guid venueId, CancellationToken ct)
    {
        await alerts.SendTestAsync(venueId, ct);
        return Accepted();
    }

    /// <summary>WhatsApp and e-mail messages for the venue, newest first, with their delivery status.</summary>
    [HttpGet("notifications")]
    public Task<DeliveryPageDto> Deliveries([FromQuery] DeliveryQuery query, CancellationToken ct) => alerts.DeliveriesAsync(query, ct);

    [HttpPost("notifications/{deliveryId:guid}/resend")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<DeliveryDto> Resend(Guid deliveryId, CancellationToken ct) => alerts.ResendAsync(deliveryId, User.UserId(), ct);
}

/// <summary>Scheduled e-mail reports (blueprint 9.2 /v1/merchant/report-schedules/*). Changing them is for owners and managers.</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[RequiresFeature(nameof(FeatureOptions.EmailReports))]
[Route("v1/merchant/report-schedules")]
public class MerchantReportSchedulesController(ReportService reports) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ReportScheduleDto>> List([FromQuery] Guid venueId, CancellationToken ct) => reports.ListAsync(venueId, ct);

    [HttpPost]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<ReportScheduleDto> Create(ReportScheduleRequest request, CancellationToken ct) => reports.CreateAsync(request, User.UserId(), ct);

    [HttpPut("{scheduleId:guid}")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<ReportScheduleDto> Update(Guid scheduleId, ReportScheduleRequest request, CancellationToken ct) =>
        reports.UpdateAsync(scheduleId, request, User.UserId(), ct);

    [HttpDelete("{scheduleId:guid}")]
    [Authorize(Policy = Policies.MerchantManager)]
    public async Task<IActionResult> Delete(Guid scheduleId, CancellationToken ct)
    {
        await reports.DeleteAsync(scheduleId, User.UserId(), ct);
        return NoContent();
    }

    /// <summary>Queues the latest report to the schedule's recipients now.</summary>
    [HttpPost("{scheduleId:guid}/send")]
    [Authorize(Policy = Policies.MerchantManager)]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public async Task<IActionResult> SendNow(Guid scheduleId, CancellationToken ct) =>
        Accepted(new { queued = await reports.SendNowAsync(scheduleId, User.UserId(), ct) });

    /// <summary>The report for the last complete day, week or month, as it would be sent.</summary>
    [HttpGet("preview")]
    public Task<ReportDto> Preview([FromQuery] Guid venueId, [FromQuery] ReportFrequency frequency, CancellationToken ct) =>
        reports.PreviewAsync(venueId, frequency, ct);
}

/// <summary>The audit log (blueprint section 11): owners and managers only.</summary>
[ApiController]
[Authorize(Policy = Policies.MerchantManager)]
[Route("v1/merchant/audit")]
public class MerchantAuditController(AuditService audit) : ControllerBase
{
    [HttpGet]
    public Task<AuditPageDto> List([FromQuery] AuditQuery query, CancellationToken ct) => audit.ListAsync(query, ct);
}

/// <summary>Which switchable features are on, so the merchant app can hide the ones that aren't.</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant/features")]
public class MerchantFeaturesController(IOptions<FeatureOptions> features) : ControllerBase
{
    [HttpGet]
    public FeatureOptions Get() => features.Value;
}
