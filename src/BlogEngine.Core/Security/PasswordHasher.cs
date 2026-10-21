using System.Globalization;
using System.Security.Cryptography;

namespace BlogEngine.Core.Security;

/// <summary>
/// Hash delle password con PBKDF2 (HMAC-SHA512, come ASP.NET Core Identity). Formato memorizzato:
/// <c>v1.{iterazioni}.{salt base64}.{hash base64}</c>; il numero di iterazioni è salvato con l'hash,
/// così può essere aumentato in futuro senza invalidare le password esistenti.
/// </summary>
public static class PasswordHasher
{
    public const int MinimumLength = 8;

    private const string Version = "v1";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashSize);
        return string.Join('.', Version, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Verifica la password in tempo costante; un hash in formato sconosciuto non è mai valido.</summary>
    public static bool Verify(string? storedHash, string? password)
    {
        if (string.IsNullOrEmpty(storedHash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        var parts = storedHash.Split('.');
        if (parts.Length != 4 || parts[0] != Version
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
