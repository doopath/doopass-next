namespace Doopass.Core.Crypto;

/// <summary>
/// URL-safe base64 encoding/decoding, mirroring Python's
/// <c>base64.urlsafe_b64encode</c> / <c>urlsafe_b64decode</c>.
/// </summary>
public static class Base64Url
{
    /// <summary>Encodes bytes to a padded URL-safe base64 string.</summary>
    public static string Encode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>Decodes a (possibly unpadded) URL-safe base64 string.</summary>
    public static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');

        base64 = (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        };

        return Convert.FromBase64String(base64);
    }
}
