using DineFlow.Application.Auth;
using DineFlow.Application.Common;
using DineFlow.Domain.Staff;

namespace DineFlow.Test.Application;

public class AuthServiceTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<StaffUser> CreateOwnerAsync()
    {
        var tenant = await _host.CreateTenantAsync("Demo Bistro");
        var owner = new StaffUser(tenant.Id, "Owner@Demo.Test", "Owner", StaffRole.Owner);
        owner.SetPasswordHash(_host.Passwords.Hash(owner, Password));
        _host.Db.StaffUsers.Add(owner);
        await _host.Db.SaveChangesAsync();
        _host.CurrentTenant = null; // signing in happens before there is a tenant
        return owner;
    }

    [Fact]
    public async Task Correct_password_signs_in_with_case_insensitive_email()
    {
        var owner = await CreateOwnerAsync();

        var result = await _host.Auth.LoginAsync(new LoginRequest("  OWNER@demo.test ", Password), default);

        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.Equal(owner.Id, result.Profile.Id);
        Assert.Equal("Owner", result.Profile.Role);
        Assert.Equal("Demo Bistro", result.Profile.TenantName);
    }

    [Theory]
    [InlineData("owner@demo.test", "wrong password")]
    [InlineData("nobody@demo.test", Password)]
    public async Task Wrong_password_or_unknown_email_gives_the_same_error(string email, string password)
    {
        await CreateOwnerAsync();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            _host.Auth.LoginAsync(new LoginRequest(email, password), default));

        Assert.Equal("invalid_credentials", ex.Code);
        Assert.Equal(401, ex.StatusCode);
    }
}
