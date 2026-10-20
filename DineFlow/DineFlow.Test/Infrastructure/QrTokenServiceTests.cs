using System.Security.Cryptography;
using DineFlow.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace DineFlow.Test.Infrastructure;

public class QrTokenServiceTests
{
    private static QrTokenService Create(string? key = null) =>
        new(Options.Create(new QrOptions { TokenKey = key ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }));

    [Fact]
    public void Token_is_27_url_safe_characters_and_well_formed()
    {
        var token = Create().CreateToken(Guid.NewGuid());

        Assert.Equal(27, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]{27}$", token);
        Assert.True(Create().IsWellFormed(token));
    }

    [Fact]
    public void Same_id_and_key_give_the_same_token_so_qr_codes_can_be_reprinted()
    {
        var service = Create();
        var id = Guid.NewGuid();

        Assert.Equal(service.CreateToken(id), service.CreateToken(id));
    }

    [Fact]
    public void Different_ids_or_keys_give_different_tokens()
    {
        var id = Guid.NewGuid();
        var a = Create();

        Assert.NotEqual(a.CreateToken(id), a.CreateToken(Guid.NewGuid()));
        Assert.NotEqual(a.CreateToken(id), Create().CreateToken(id));
    }

    [Fact]
    public void Hash_is_lowercase_hex_sha256()
    {
        Assert.Matches("^[0-9a-f]{64}$", Create().Hash("anything"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it and is 27c")]
    [InlineData("1234567890123456789012345678")]
    public void Malformed_tokens_are_rejected(string token)
    {
        Assert.False(Create().IsWellFormed(token));
    }
}
