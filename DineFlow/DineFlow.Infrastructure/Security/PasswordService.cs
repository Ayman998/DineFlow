using DineFlow.Application.Abstractions;
using DineFlow.Domain.Staff;
using Microsoft.AspNetCore.Identity;

namespace DineFlow.Infrastructure.Security;

/// <summary>ASP.NET Core Identity's PBKDF2 password hasher, without the rest of Identity.</summary>
public class PasswordService : IPasswordService
{
    private readonly PasswordHasher<StaffUser> _hasher = new();

    public string Hash(StaffUser user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(StaffUser user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
