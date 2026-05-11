using System.Globalization;
using System.Security.Cryptography;

namespace WvWAnalyst.Api.Auth;

public sealed class PasswordHashService
{
    private const int SaltByteCount = 16;
    private const int HashByteCount = 32;
    private const int IterationCount = 100_000;
    private const string Prefix = "pbkdf2-sha256.v1";

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltByteCount);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            IterationCount,
            HashAlgorithmName.SHA256,
            HashByteCount);

        return string.Join(
            '$',
            Prefix,
            IterationCount.ToString(CultureInfo.InvariantCulture),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        var parts = passwordHash.Split('$');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], Prefix, StringComparison.Ordinal) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations) ||
            iterations <= 0)
        {
            return false;
        }

        try
        {
            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expectedHash = Convert.FromBase64String(parts[3]);
            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
