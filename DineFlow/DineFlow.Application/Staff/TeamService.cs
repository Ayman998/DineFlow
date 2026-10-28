using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Staff;

public record StaffMemberDto(Guid Id, string Email, string DisplayName, StaffRole Role, bool IsActive, bool IsYou);

/// <param name="TemporaryPassword">Shown once to the person who added or reset the account; DineFlow keeps only its hash.</param>
public record StaffPasswordDto(StaffMemberDto Member, string TemporaryPassword);

public record AddStaffRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(StaffUser.MaxNameLength)] string DisplayName,
    StaffRole Role);

public record UpdateStaffRequest(
    [Required, StringLength(StaffUser.MaxNameLength)] string DisplayName,
    StaffRole Role,
    bool IsActive);

/// <summary>
/// Settings → Team: who can sign in to the merchant portal. Owners manage everyone; managers add
/// and manage staff (waiters, cashiers) only. Nobody changes their own role or switches themselves
/// off, so the owner making a change is always still an active owner afterwards and a tenant can't
/// lose its last one. Every change is audit-logged.
/// </summary>
public class TeamService(IAppDbContext db, ITenantContext tenant, IPasswordService passwords, TimeProvider clock)
{
    public async Task<IReadOnlyList<StaffMemberDto>> ListAsync(Guid actorId, CancellationToken ct)
    {
        tenant.RequireTenantId();
        var users = await db.StaffUsers.AsNoTracking().ToListAsync(ct);
        return users.OrderByDescending(u => u.IsActive).ThenBy(u => u.Role).ThenBy(u => u.DisplayName)
            .Select(u => ToDto(u, actorId)).ToList();
    }

    public async Task<StaffPasswordDto> AddAsync(AddStaffRequest request, Guid actorId, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var actor = await ActorAsync(actorId, ct);
        RequireMayManage(actor, request.Role);

        var email = request.Email.Trim().ToLowerInvariant();
        // E-mail addresses are unique across tenants, since sign-in starts from the address alone.
        if (await db.StaffUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw AppException.Conflict("email_taken", "Someone already signs in with that e-mail address.");

        var user = new StaffUser(tenantId, email, "-", request.Role);
        user.Rename(request.DisplayName);
        var password = TemporaryPassword.Create();
        user.SetPasswordHash(passwords.Hash(user, password));
        db.StaffUsers.Add(user);
        db.AuditEntries.Add(new AuditEntry(tenantId, actorId, "staff.added", nameof(StaffUser), user.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return new StaffPasswordDto(ToDto(user, actorId), password);
    }

    public async Task<StaffMemberDto> UpdateAsync(Guid staffUserId, UpdateStaffRequest request, Guid actorId, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var actor = await ActorAsync(actorId, ct);
        var user = await FindAsync(staffUserId, ct);
        RequireMayManage(actor, user.Role);
        RequireMayManage(actor, request.Role);

        if (user.Id == actorId && (request.Role != user.Role || !request.IsActive))
            throw AppException.BadRequest("cannot_change_self", "You can't change your own role or switch yourself off. Ask another owner.");

        user.Rename(request.DisplayName);
        user.SetRole(request.Role);
        if (request.IsActive) user.Activate(); else user.Deactivate();
        db.AuditEntries.Add(new AuditEntry(tenantId, actorId, "staff.updated", nameof(StaffUser), user.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return ToDto(user, actorId);
    }

    /// <summary>For someone who forgot their password: a new temporary one, which they change after signing in.</summary>
    public async Task<StaffPasswordDto> ResetPasswordAsync(Guid staffUserId, Guid actorId, CancellationToken ct)
    {
        var tenantId = tenant.RequireTenantId();
        var actor = await ActorAsync(actorId, ct);
        var user = await FindAsync(staffUserId, ct);
        RequireMayManage(actor, user.Role);
        if (user.Id == actorId)
            throw AppException.BadRequest("cannot_change_self", "Change your own password under Your password instead.");

        var password = TemporaryPassword.Create();
        user.SetPasswordHash(passwords.Hash(user, password));
        db.AuditEntries.Add(new AuditEntry(tenantId, actorId, "staff.password_reset", nameof(StaffUser), user.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return new StaffPasswordDto(ToDto(user, actorId), password);
    }

    private async Task<StaffUser> ActorAsync(Guid actorId, CancellationToken ct) =>
        await db.StaffUsers.SingleOrDefaultAsync(u => u.Id == actorId && u.IsActive, ct)
        ?? throw AppException.Unauthorized("invalid_session", "Please sign in again.");

    private async Task<StaffUser> FindAsync(Guid staffUserId, CancellationToken ct) =>
        await db.StaffUsers.SingleOrDefaultAsync(u => u.Id == staffUserId, ct)
        ?? throw AppException.NotFound("staff_not_found", "That team member doesn't exist.");

    private static void RequireMayManage(StaffUser actor, StaffRole role)
    {
        var allowed = actor.Role switch
        {
            StaffRole.Owner => true,
            StaffRole.Manager => role == StaffRole.Staff,
            _ => false
        };
        if (!allowed) throw AppException.Forbidden("role_not_allowed", "Only an owner can add or change owners and managers.");
    }

    private static StaffMemberDto ToDto(StaffUser u, Guid actorId) =>
        new(u.Id, u.Email, u.DisplayName, u.Role, u.IsActive, u.Id == actorId);
}

/// <summary>Random passwords for new and reset accounts: 14 characters without look-alikes (0/O, 1/l/I).</summary>
public static class TemporaryPassword
{
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string Create(int length = 14) =>
        new(Enumerable.Range(0, length).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
}
