using System.Security.Cryptography;
using System.Text;
using DineFlow.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Security;

/// <summary>
/// QR token = base64url(first 20 bytes of HMAC-SHA256(key, qrTokenId)): 27 URL-safe characters,
/// 160 bits, not guessable and not sequential. The database stores only SHA-256 of it.
/// </summary>
public class QrTokenService(IOptions<QrOptions> options) : IQrTokenService
{
    private const int TokenBytes = 20;
    private const int TokenLength = 27; // base64url of 20 bytes, without padding

    private readonly byte[] _key = options.Value.TokenKeyBytes();

    public string CreateToken(Guid qrTokenId)
    {
        var mac = HMACSHA256.HashData(_key, qrTokenId.ToByteArray());
        return Base64Url(mac.AsSpan(0, TokenBytes));
    }

    public string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token))).ToLowerInvariant();

    public bool IsWellFormed(string token) =>
        token.Length == TokenLength && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
