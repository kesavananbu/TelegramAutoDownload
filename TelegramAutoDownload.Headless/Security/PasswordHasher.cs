using System.Security.Cryptography;
using System.Text;

namespace TelegramAutoDownload.Headless.Security;

/// <summary>PBKDF2-SHA256 password storage (OWASP baseline: 600k iterations, 16-byte salt).</summary>
public static class PasswordHasher
{
    public const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "pbkdf2-sha256";

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Pbkdf2(password, salt);
        return Format(salt, hash);
    }

    public static bool Verify(string password, string stored)
    {
        if (TryParsePbkdf2(stored, out var iter, out var salt, out var expected))
        {
            var actual = Pbkdf2(password, salt, iter);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        if (TryParseLegacySha256(stored, out var legacyHash))
        {
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return CryptographicOperations.FixedTimeEquals(actual, legacyHash);
        }

        return false;
    }

    public static bool IsLegacySha256Base64(string stored) => TryParseLegacySha256(stored, out _);

    public static bool IsPbkdf2Format(string stored) =>
        stored.StartsWith(Prefix + "$", StringComparison.Ordinal);

    public static string DummyHashForTiming { get; } = HashPassword("__dummy_timing_user__");

    private static byte[] Pbkdf2(string password, byte[] salt, int iterations = Iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

    private static string Format(byte[] salt, byte[] hash) =>
        $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";

    private static bool TryParsePbkdf2(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('$');
        if (parts.Length != 4) return false;
        if (!string.Equals(parts[0], Prefix, StringComparison.Ordinal)) return false;
        if (!int.TryParse(parts[1], out iterations) || iterations < 100_000) return false;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            hash = Convert.FromBase64String(parts[3]);
            return salt.Length >= 8 && hash.Length >= 16;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseLegacySha256(string stored, out byte[] hash)
    {
        hash = [];
        try
        {
            if (stored.Contains('$', StringComparison.Ordinal)) return false;
            hash = Convert.FromBase64String(stored);
            return hash.Length == 32;
        }
        catch
        {
            return false;
        }
    }
}
