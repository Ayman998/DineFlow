using DineFlow.Api.Common;
using DineFlow.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineFlow.Api.Controllers;

[ApiController]
[Route("v1/merchant/auth")]
public class MerchantAuthController(AuthService auth) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.LoginRateLimit)]
    public Task<LoginResultDto> Login(LoginRequest request, CancellationToken ct) =>
        auth.LoginAsync(request, ct);

    [HttpGet("me")]
    [Authorize(Policy = Policies.Merchant)]
    public Task<StaffProfileDto> Me(CancellationToken ct) =>
        auth.GetProfileAsync(User.UserId(), ct);
}
