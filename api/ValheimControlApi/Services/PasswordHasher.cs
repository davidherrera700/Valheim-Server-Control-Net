using System.Security.Cryptography;

namespace ValheimControlApi.Services;

/// <summary>
/// Salted PBKDF2 password hashing, using only what's built into .NET's base
/// class library (no external crypto dependency needed). This is real
/// password security, unlike the simple SHA-256 hash used for the app's
/// "delete confirmation" courtesy gate elsewhere in the project - user
/// account passwords deserve a proper, slow, salted KDF.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySizeBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string storedHashBase64, string storedSaltBase64)
    {
        var salt = Convert.FromBase64String(storedSaltBase64);
        var expectedHash = Convert.FromBase64String(storedHashBase64);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySizeBytes);

        // Constant-time comparison - avoids leaking timing information about
        // how many bytes matched, same reasoning as everywhere else this
        // project compares secrets.
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
