using System.Security.Cryptography;
using System.Text;

namespace BilingualVocabApi.Services;

public static class PasswordSecurity
{
    private const string Prefix = "pbkdf2$";
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string? inputPassword, string? storedPassword)
    {
        if (string.IsNullOrEmpty(inputPassword) || string.IsNullOrEmpty(storedPassword))
            return false;

        if (!storedPassword.StartsWith(Prefix, StringComparison.Ordinal))
            return string.Equals(inputPassword, storedPassword, StringComparison.Ordinal);

        var parts = storedPassword.Split('$');
        if (parts.Length != 3) return false;

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expectedHash = Convert.FromBase64String(parts[2]);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(inputPassword), salt, Iterations, HashAlgorithmName.SHA256, expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsHashed(string? storedPassword) =>
        !string.IsNullOrEmpty(storedPassword) && storedPassword.StartsWith(Prefix, StringComparison.Ordinal);
}
