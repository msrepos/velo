using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Application;

namespace Velo.Modules.Identity.Infrastructure;

public sealed class AuthCryptography(IOptions<AuthOptions> options)
{
    private readonly byte[] key = Convert.FromBase64String(options.Value.OtpHashKey);

    public string HashCode(Guid challengeId, string phoneNumber, string code) =>
        HashPrivate($"otp:{challengeId:D}:{phoneNumber}:{code}");

    public string HashPrivate(string value) =>
        Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value)));

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool Matches(string expectedHash, string actualHash) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash));

    public static string GenerateCode() =>
        RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
