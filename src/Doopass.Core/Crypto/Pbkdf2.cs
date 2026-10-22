using System.Security.Cryptography;

namespace Doopass.Core.Crypto;

/// <summary>PBKDF2-HMAC-SHA256 key derivation (same parameters as the original).</summary>
public static class Pbkdf2
{
    /// <summary>Derives a 32-byte key from a password and salt.</summary>
    public static byte[] DeriveKey(byte[] password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    /// <summary>Derives a 32-byte key from a password string and salt.</summary>
    public static byte[] DeriveKey(string password, byte[] salt, int iterations)
    {
        return DeriveKey(System.Text.Encoding.UTF8.GetBytes(password), salt, iterations);
    }
}
