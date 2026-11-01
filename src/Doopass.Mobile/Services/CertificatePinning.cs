using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Doopass.Mobile.Services;

/// <summary>
/// TLS handlers for the thin client. Android's default native handler ignores
/// <c>ServerCertificateCustomValidationCallback</c>, so a self-signed host could
/// be neither probed nor pinned; <see cref="SocketsHttpHandler"/> honours it.
/// </summary>
internal static class CertificatePinning
{
    /// <summary>Lowercase hex SHA-256 of the DER certificate, the form the app pins.</summary>
    public static string Fingerprint(X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())).ToLowerInvariant();

    /// <summary>Accepts any certificate and reports its fingerprint; TOFU probe only.</summary>
    public static SocketsHttpHandler Probe(Action<string> onFingerprint)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null)
            {
                return false;
            }

            onFingerprint(Fingerprint(certificate));
            return true;
        };
        return handler;
    }

    /// <summary>Accepts only the certificate whose SHA-256 fingerprint was pinned.</summary>
    public static SocketsHttpHandler Pinned(string fingerprint)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback =
            (_, certificate, _, _) => Matches(certificate, fingerprint);
        return handler;
    }

    /// <summary>Compares a handshake certificate with the stored pin.</summary>
    public static bool Matches(X509Certificate? certificate, string fingerprint) =>
        certificate is not null
        && string.Equals(Fingerprint(certificate), fingerprint, StringComparison.OrdinalIgnoreCase);
}
