using DineFlow.Api.Common;
using DineFlow.Application.Onboarding;
using DineFlow.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>Settings → Team: who signs in to the portal. Owners and managers; see TeamService for who may change whom.</summary>
[ApiController]
[Authorize(Policy = Policies.MerchantManager)]
[Route("v1/merchant/team")]
public class TeamController(TeamService team) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<StaffMemberDto>> List(CancellationToken ct) => team.ListAsync(User.UserId(), ct);

    [HttpPost]
    public Task<StaffPasswordDto> Add(AddStaffRequest request, CancellationToken ct) => team.AddAsync(request, User.UserId(), ct);

    [HttpPut("{staffUserId:guid}")]
    public Task<StaffMemberDto> Update(Guid staffUserId, UpdateStaffRequest request, CancellationToken ct) =>
        team.UpdateAsync(staffUserId, request, User.UserId(), ct);

    [HttpPost("{staffUserId:guid}/reset-password")]
    public Task<StaffPasswordDto> ResetPassword(Guid staffUserId, CancellationToken ct) =>
        team.ResetPasswordAsync(staffUserId, User.UserId(), ct);
}

/// <summary>The pilot go-live checklist for a venue (see GoLiveService).</summary>
[ApiController]
[Authorize(Policy = Policies.MerchantManager)]
[Route("v1/merchant/venues/{venueId:guid}/go-live")]
public class GoLiveController(GoLiveService goLive) : ControllerBase
{
    [HttpGet]
    public Task<GoLiveDto> Checklist(Guid venueId, CancellationToken ct) => goLive.ChecklistAsync(venueId, ct);
}
