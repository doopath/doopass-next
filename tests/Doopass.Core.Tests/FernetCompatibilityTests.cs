using System.Text;
using Doopass.Core.Crypto;
using Doopass.Core.Storage;
using Xunit;

namespace Doopass.Core.Tests;

/// <summary>
/// Validates byte-for-byte compatibility with the original Python
/// implementation (cryptography.fernet + PBKDF2-HMAC-SHA256).
/// </summary>
public class FernetCompatibilityTests
{
    // Generated with the original algorithm via Python `cryptography` 50.0.2.
    private const string GoldenPassword = "test-password";
    private const string GoldenPlaintext = "github = token123\nemail = a@b.c";
    private const string GoldenStoreEnc =
        "mcCBEoGrzP-KYP76ke10IAABhqCAAAAAAGrag_9WlQTGZBWPElAr4XzSqxqeItR5t8NkvSd0BGUjjkGRpG6GvKbQD7xHsmt7N1RdL4Lc-lpBgoaL7u5EVEnepq_yn-z5qfP9AYe7JqtAqcz4Ng==";
    private const string GoldenSaltHex = "000102030405060708090a0b0c0d0e0f";
    private const string GoldenKeyBase64Url = "faBrNVsvpAAmvgRLAUXpEQKer-100ZQptnBEwJ5xBsg=";

    [Fact]
    public void DecryptGoldenStore_ProducesExpectedPlaintext()
    {
        var plaintext = Encoding.UTF8.GetString(Store.Decrypt(GoldenPassword, GoldenStoreEnc));

        Assert.Equal(GoldenPlaintext, plaintext);
    }

    [Fact]
    public void Pbkdf2_MatchesPythonDerivedKey()
    {
        var salt = Convert.FromHexString(GoldenSaltHex);
        var key = Pbkdf2.DeriveKey(GoldenPassword, salt, 100_000);

        Assert.Equal(GoldenKeyBase64Url, Base64Url.Encode(key));
    }

    [Fact]
    public void EncryptThenDecrypt_RoundTrips()
    {
        var plaintext = Encoding.UTF8.GetBytes("github = abc123\nemail = a@b.c\nnote = =equals=");

        var encrypted = Store.Encrypt(GoldenPassword, plaintext);
        var decrypted = Store.Decrypt(GoldenPassword, encrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void DecryptWithWrongPassword_ThrowsIncorrectPassword()
    {
        Assert.Throws<IncorrectPasswordError>(
            () => Store.Decrypt("wrong-password", GoldenStoreEnc));
    }

    [Fact]
    public void FernetToken_RoundTripsDirectly()
    {
        var salt = Convert.FromHexString(GoldenSaltHex);
        var key = Pbkdf2.DeriveKey(GoldenPassword, salt, 100_000);

        var token = Fernet.Encrypt(key, Encoding.UTF8.GetBytes("hello fernet"));
        var decrypted = Encoding.UTF8.GetString(Fernet.Decrypt(key, token));

        Assert.Equal("hello fernet", decrypted);
    }
}
