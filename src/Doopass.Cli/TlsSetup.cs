using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Doopass.Core.Configuration;

namespace Doopass.Cli;

/// <summary>
/// Creates a self-signed TLS certificate with the machine's LAN addresses in
/// its SAN and stores it in <c>settings.json</c>, so a later
/// <c>serve</c> picks it up without command-line arguments.
/// </summary>
internal static class TlsSetup
{
    private const string Help = """
        Usage: doopass setup-tls [--pfx <path>] [--pass <password>] [--force]

          --pfx <path>   Certificate path (default: ~/doopass/doopass.pfx)
          --pass <pwd>   PKCS#12 password (default: random, stored in settings.json)
          --force        Replace an existing certificate (phones must re-pair)
          -h, --help     Show this help
        """;

    /// <summary>Runs the command and returns the process exit code.</summary>
    public static int Run(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Help);
            return 0;
        }

        var force = args.Contains("--force");
        var explicitPath = Option(args, "--pfx");
        var password = Option(args, "--pass");

        if (args.Any(arg => arg.StartsWith("--", StringComparison.Ordinal)
                && arg is not ("--pfx" or "--pass" or "--force"))
            || (args.Contains("--pfx") && explicitPath is null)
            || (args.Contains("--pass") && password is null))
        {
            Console.Error.WriteLine($"Invalid option. {Environment.NewLine}{Help}");
            return 2;
        }

        var settings = new Settings();
        var target = explicitPath ?? Path.Combine(settings.AppDir, "doopass.pfx");

        if (File.Exists(target) && !force)
        {
            Console.Error.WriteLine(
                $"A certificate already exists at {target}. Re-run with --force to replace it " +
                "(phone clients will have to trust the new fingerprint).");
            return 1;
        }

        password ??= RandomPassword();
        var addresses = LocalAddresses();

        try
        {
            using var certificate = CreateCertificate(addresses);

            var directory = Path.GetDirectoryName(Path.GetFullPath(target));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(target, certificate.Export(X509ContentType.Pkcs12, password));

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            // Fail here, not at the next host start: the file must load with the
            // password we just stored.
            using var check = X509CertificateLoader.LoadPkcs12FromFile(target, password);
            if (!check.HasPrivateKey)
            {
                throw new InvalidOperationException("The exported certificate has no private key.");
            }

            settings.SetTls(Path.GetFullPath(target), password);

            var fingerprint = Convert.ToHexString(SHA256.HashData(check.RawData)).ToLowerInvariant();
            var lan = addresses
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(address))
                .ToList();
            var hosts = new List<string> { Environment.MachineName, "localhost" };
            hosts.AddRange(lan.Select(address => address.ToString()));

            Console.WriteLine("TLS certificate created.");
            Console.WriteLine($"  certificate: {Path.GetFullPath(target)}");
            Console.WriteLine($"  password:    stored in {settings.SettingsFile} (plain text)");
            Console.WriteLine($"  fingerprint: {fingerprint}");
            Console.WriteLine($"  hosts:       {string.Join(", ", hosts)}");
            Console.WriteLine();
            Console.WriteLine("Start the host and open:");
            Console.WriteLine($"  https://localhost:7240");
            foreach (var address in lan)
            {
                Console.WriteLine($"  https://{address}:7240  (from another device on this network)");
            }

            Console.WriteLine();
            Console.WriteLine("Phones that were paired with an older certificate must trust this one again.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not create the certificate: {ex.Message}");
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string RandomPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static List<IPAddress> LocalAddresses()
    {
        var addresses = new List<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;

                    if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
                        && !IPAddress.IsLoopback(address)
                        && !addresses.Contains(address))
                    {
                        addresses.Add(address);
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No interfaces to enumerate: localhost is still enough to start.
        }

        return addresses;
    }

    private static X509Certificate2 CreateCertificate(IReadOnlyList<IPAddress> addresses)
    {
        var host = Environment.MachineName;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        san.AddDnsName("localhost");

        foreach (var address in addresses)
        {
            san.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, critical: false));

        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(825));
    }
}
