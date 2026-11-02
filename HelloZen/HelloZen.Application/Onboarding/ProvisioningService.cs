using HelloZen.Application.Abstractions;
using HelloZen.Application.Common;
using HelloZen.Application.Staff;
using HelloZen.Domain.Audit;
using HelloZen.Domain.Staff;
using HelloZen.Domain.Tenants;
using HelloZen.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace HelloZen.Application.Onboarding;

/// <summary>
/// What the operator types to bring a restaurant onto helloZen. The defaults are a venue in Amman:
/// JOD, Asia/Amman, Arabic first, Jordan's 16% general sales tax and a 10% service charge, all of
/// which the owner can change in Settings afterwards.
/// </summary>
public record ProvisionRequest(
    string BusinessName,
    string VenueName,
    string OwnerEmail,
    string OwnerName,
    string? LegalName = null,
    string? VenueNameAr = null,
    int Tables = 10,
    string Country = "JO",
    string Currency = "JOD",
    string TimeZone = "Asia/Amman",
    string Locale = "ar",
    int TaxBasisPoints = 1600,
    int ServiceChargeBasisPoints = 1000,
    bool PricesIncludeTax = true,
    string? OwnerPassword = null);

/// <param name="TemporaryPassword">The owner's first password, printed once; they change it under Settings → Team.
/// When the request named one, it's that password.</param>
public record ProvisionResult(Guid TenantId, Guid VenueId, string OwnerEmail, string TemporaryPassword, int Tables);

/// <summary>
/// Creates a tenant, its first venue with numbered tables (each with a QR code) and the owner's
/// account, in one save. Run by the operator from the server (the API's "provision" command):
/// there is no public sign-up during the pilot.
/// </summary>
public class ProvisioningService(IAppDbContext db, IQrTokenService qrTokens, IPasswordService passwords, TimeProvider clock)
{
    public const int MaxTables = 200;

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessName) || string.IsNullOrWhiteSpace(request.VenueName))
            throw AppException.BadRequest("invalid_request", "A business name and a venue name are required.");
        if (string.IsNullOrWhiteSpace(request.OwnerName))
            throw AppException.BadRequest("invalid_request", "The owner's name is required.");
        if (!System.Net.Mail.MailAddress.TryCreate(request.OwnerEmail?.Trim(), out _))
            throw AppException.BadRequest("invalid_request", "The owner's e-mail address isn't valid.");
        if (request.Tables is < 0 or > MaxTables)
            throw AppException.BadRequest("invalid_request", $"Tables must be between 0 and {MaxTables}.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone, out _))
            throw AppException.BadRequest("invalid_request", $"Unknown time zone '{request.TimeZone}'.");
        RequireAcceptable(request.OwnerPassword);

        var email = request.OwnerEmail!.Trim().ToLowerInvariant();
        if (await db.StaffUsers.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw AppException.Conflict("email_taken", $"{email} already signs in to helloZen.");

        var tenant = new Tenant((request.LegalName ?? request.BusinessName).Trim(), request.BusinessName.Trim(), request.Country);
        tenant.Activate();
        db.Tenants.Add(tenant);

        var venue = new Venue(tenant.Id, request.VenueName.Trim(), request.Currency, request.TimeZone, request.Locale);
        venue.SetCharges(request.ServiceChargeBasisPoints, request.TaxBasisPoints, request.PricesIncludeTax);
        if (!string.IsNullOrWhiteSpace(request.VenueNameAr)) venue.SetArabicName(request.VenueNameAr);
        db.Venues.Add(venue);

        if (request.Tables > 0)
        {
            var area = new DiningArea(tenant.Id, venue.Id, "Main hall", 0);
            db.DiningAreas.Add(area);
            for (var n = 1; n <= request.Tables; n++)
            {
                var location = new ServiceLocation(tenant.Id, venue.Id, area.Id, ServiceLocationType.Table, n.ToString());
                var qrId = Guid.NewGuid();
                db.ServiceLocations.Add(location);
                db.QrTokens.Add(new QrToken(qrId, tenant.Id, location.Id, 1, qrTokens.Hash(qrTokens.CreateToken(qrId))));
            }
        }

        var owner = new StaffUser(tenant.Id, email, "-", StaffRole.Owner);
        owner.Rename(request.OwnerName);
        var password = request.OwnerPassword ?? TemporaryPassword.Create();
        owner.SetPasswordHash(passwords.Hash(owner, password));
        db.StaffUsers.Add(owner);

        await db.SaveChangesAsync(ct);
        return new ProvisionResult(tenant.Id, venue.Id, email, password, request.Tables);
    }

    /// <summary>
    /// Lets someone back in who can't sign in any more, e.g. an owner who lost the temporary password:
    /// there's no "forgot password" link during the pilot, and only an owner or manager can reset
    /// passwords from Settings → Team. Run by the operator (the API's "reset-password" command).
    /// </summary>
    /// <param name="newPassword">The password to set, or null for a random temporary one.</param>
    /// <returns>The temporary password when one was made, otherwise null.</returns>
    public async Task<string?> ResetPasswordAsync(string email, string? newPassword, CancellationToken ct)
    {
        var address = email.Trim().ToLowerInvariant();
        var user = await db.StaffUsers.IgnoreQueryFilters().SingleOrDefaultAsync(u => u.Email == address, ct)
            ?? throw AppException.NotFound("staff_not_found", $"No one signs in to helloZen as {address}.");
        if (!user.IsActive)
            throw AppException.Conflict("account_switched_off", $"{address} is switched off. An owner can switch it back on under Settings → Team.");
        RequireAcceptable(newPassword);

        var password = newPassword ?? TemporaryPassword.Create();
        user.SetPasswordHash(passwords.Hash(user, password));
        db.AuditEntries.Add(new AuditEntry(user.TenantId, user.Id, "account.password_reset_by_operator", nameof(StaffUser), user.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return newPassword is null ? password : null;
    }

    /// <summary>A password the operator chose follows the same length rule as one changed in Settings.</summary>
    private static void RequireAcceptable(string? password)
    {
        if (password is { Length: < StaffUser.MinPasswordLength or > 128 })
            throw AppException.BadRequest("weak_password", $"Use {StaffUser.MinPasswordLength} to 128 characters.");
    }
}
