using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Doopass.Core.Crypto;

/// <summary>
/// A byte-compatible implementation of the Fernet token format
/// (https://github.com/fernet/spec/blob/master/Spec.md), matching the
/// <c>cryptography.fernet.Fernet</c> behavior used by the original Doopass.
///
/// A token is: version (0x80) || timestamp (8 bytes, big-endian) ||
/// IV (16 bytes) || AES-128-CBC ciphertext (PKCS7) || HMAC-SHA256 (32 bytes),
/// all encoded as URL-safe base64.
/// </summary>
public static class Fernet
{
    private const byte Version = 0x80;
    private const int KeyLength = 32;

    /// <summary>Encrypts <paramref name="plaintext"/> with a 32-byte raw key.</summary>
    public static string Encrypt(byte[] key, byte[] plaintext)
    {
        ValidateKey(key);

        var signingKey = key.AsSpan(0, 16);
        var encryptionKey = key.AsSpan(16, 16);

        var iv = RandomNumberGenerator.GetBytes(16);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var ciphertext = Aes(encryptionKey, iv, plaintext, encrypt: true);

        var payload = new byte[1 + 8 + 16 + ciphertext.Length];
        payload[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, 8), timestamp);
        iv.CopyTo(payload.AsSpan(9, 16));
        ciphertext.CopyTo(payload.AsSpan(25));

        var mac = HMACSHA256.HashData(signingKey, payload);

        var token = new byte[payload.Length + 32];
        payload.CopyTo(token, 0);
        mac.CopyTo(token, payload.Length);

        return Base64Url.Encode(token);
    }

    /// <summary>
    /// Decrypts a <paramref name="token"/> with a 32-byte raw key.
    /// Throws <see cref="IncorrectPasswordError"/> when the token cannot be
    /// authenticated (wrong key or corrupted data).
    /// </summary>
    public static byte[] Decrypt(byte[] key, string token)
    {
        ValidateKey(key);

        byte[] full;
        try
        {
            full = Base64Url.Decode(token);
        }
        catch (FormatException ex)
        {
            throw new IncorrectPasswordError("Password is incorrect!", ex);
        }

        if (full.Length < 57 || full[0] != Version)
        {
            throw new IncorrectPasswordError("Password is incorrect!");
        }

        var signingKey = key.AsSpan(0, 16);
        var encryptionKey = key.AsSpan(16, 16);

        var payload = full.AsSpan(0, full.Length - 32);
        var mac = full.AsSpan(full.Length - 32, 32);

        var expectedMac = HMACSHA256.HashData(signingKey, payload);

        if (!CryptographicOperations.FixedTimeEquals(expectedMac, mac))
        {
            throw new IncorrectPasswordError("Password is incorrect!");
        }

        var iv = payload.Slice(9, 16);
        var ciphertext = payload.Slice(25);

        try
        {
            return Aes(encryptionKey, iv, ciphertext.ToArray(), encrypt: false);
        }
        catch (CryptographicException ex)
        {
            throw new IncorrectPasswordError("Password is incorrect!", ex);
        }
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != KeyLength)
        {
            throw new ArgumentException($"Fernet key must be {KeyLength} bytes.", nameof(key));
        }
    }

    private static byte[] Aes(ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv, byte[] data, bool encrypt)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        return transform.TransformFinalBlock(data, 0, data.Length);
    }
}
