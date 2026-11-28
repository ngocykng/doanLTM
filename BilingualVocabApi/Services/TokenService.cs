using System.Security.Cryptography;
using System.Text;

namespace BilingualVocabApi.Services;

/// <summary>Ký và xác thực Bearer token bằng HMAC-SHA256 kèm thời hạn sử dụng.</summary>
public sealed class TokenService
{
    private readonly byte[] _secret;

    public TokenService(IConfiguration configuration)
    {
        var configured = Environment.GetEnvironmentVariable("AUTH_TOKEN_SECRET")
            ?? configuration["AUTH_TOKEN_SECRET"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            var fallbackSeed = Environment.GetEnvironmentVariable("DB_PASSWORD")
                ?? configuration["DB_PASSWORD"]
                ?? "bilingual-vocab-dau-default-hmac-seed";
            configured = $"ltm-hmac::{fallbackSeed}::{Environment.MachineName}";
        }
        _secret = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public string IssueToken(string userId, TimeSpan? lifetime = null)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime ?? TimeSpan.FromHours(24)).ToUnixTimeSeconds();
        var payload = $"{userId}.{expiresAt}";
        var sig = ComputeSignature(payload);
        return $"{payload}.{sig}";
    }

    public bool TryValidateToken(string? token, out string userId)
    {
        userId = string.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var parts = token.Trim().Split('.');
        if (parts.Length != 3) return false;

        var candidateUserId = parts[0];
        if (string.IsNullOrWhiteSpace(candidateUserId)) return false;
        if (!long.TryParse(parts[1], out var expiresAt)) return false;
        if (expiresAt < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;

        var expectedSig = ComputeSignature($"{candidateUserId}.{expiresAt}");
        var actualBytes = Encoding.UTF8.GetBytes(parts[2]);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedSig);
        if (actualBytes.Length != expectedBytes.Length
            || !CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
        {
            return false;
        }

        userId = candidateUserId;
        return true;
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_secret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
