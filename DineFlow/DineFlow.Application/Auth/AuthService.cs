using System.ComponentModel.DataAnnotations;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Auth;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(128, MinimumLength = StaffUser.MinPasswordLength)] string NewPassword);

public record LoginResultDto(string Token, DateTimeOffset ExpiresAt, StaffProfileDto Profile);

public record StaffProfileDto(Guid Id, string Email, string DisplayName, string Role, Guid TenantId, string TenantName);

/// <summary>Merchant sign-in with email and password; issues a tenant-scoped JWT.</summary>
public class AuthService(IAppDbContext db, IPasswordService passwords, ITokenIssuer tokenIssuer, TimeProvider clock)
{
    // Checked when the email is unknown, so "no such user" takes as long as "wrong password"
    // and response times don't reveal which emails have accounts.
    private static readonly Lazy<StaffUser> DummyUser = new(() =>
        new StaffUser(Guid.Empty, "nobody@invalid", "nobody", StaffRole.Staff));

    public async Task<LoginResultDto> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Nobody is signed in yet, so the tenant filter can't apply to this lookup.
        var user = await db.StaffUsers.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            var dummy = DummyUser.Value;
            if (dummy.PasswordHash.Length == 0) dummy.SetPasswordHash(passwords.Hash(dummy, Guid.NewGuid().ToString()));
            passwords.Verify(dummy, request.Password);
        }

        if (user is null || !user.IsActive || !passwords.Verify(user, request.Password))
            throw AppException.Unauthorized("invalid_credentials", "Email or password is incorrect.");

        var tenantName = await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.DisplayName).SingleAsync(ct);
        var issued = tokenIssuer.IssueStaffToken(user);

        return new LoginResultDto(issued.Token, issued.ExpiresAt, ToProfile(user, tenantName));
    }

    public async Task<StaffProfileDto> GetProfileAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.StaffUsers.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw AppException.Unauthorized("invalid_session", "Please sign in again.");

        var tenantName = await db.Tenants.Where(t => t.Id == user.TenantId).Select(t => t.DisplayName).SingleAsync(ct);
        return ToProfile(user, tenantName);
    }

    /// <summary>Anyone signed in can change their own password, e.g. the temporary one from Settings → Team.</summary>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await db.StaffUsers.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct)
            ?? throw AppException.Unauthorized("invalid_session", "Please sign in again.");
        if (!passwords.Verify(user, request.CurrentPassword))
            throw AppException.BadRequest("wrong_password", "Your current password isn't right.");
        if (request.NewPassword == request.CurrentPassword)
            throw AppException.BadRequest("weak_password", "Choose a password you haven't used here.");

        user.SetPasswordHash(passwords.Hash(user, request.NewPassword));
        db.AuditEntries.Add(new AuditEntry(user.TenantId, user.Id, "account.password_changed", nameof(StaffUser), user.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
    }

    private static StaffProfileDto ToProfile(StaffUser u, string tenantName) =>
        new(u.Id, u.Email, u.DisplayName, u.Role.ToString(), u.TenantId, tenantName);
}
