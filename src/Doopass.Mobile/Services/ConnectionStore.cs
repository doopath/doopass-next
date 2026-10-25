using Doopass.Core.Diagnostics;
using Microsoft.Maui.Storage;

namespace Doopass.Mobile.Services;

/// <summary>
/// Persisted connection configuration of the thin client on this phone:
/// the host URL, the pinned certificate fingerprint (trust-on-first-use) and
/// the session token. Everything lives in SecureStorage (Android Keystore);
/// there is no local vault on the phone — only the address, the pin and the
/// token of the host.
/// </summary>
public sealed class ConnectionStore
{
    private const string HostKey = "doopass.host";
    private const string PinKey = "doopass.pin";
    private const string TokenKey = "doopass.token";

    public string? Host { get; private set; }

    /// <summary>Base URL of the host, e.g. <c>http://192.168.1.10:7240</c>.</summary>
    public Uri? HostUrl => Uri.TryCreate(Host, UriKind.Absolute, out var uri) ? uri : null;

    /// <summary>SHA-256 hex fingerprint of the pinned TLS certificate; null for plain HTTP.</summary>
    public string? PinnedFingerprint { get; private set; }

    public string? Token { get; private set; }

    public bool IsConfigured => !string.IsNullOrEmpty(Host);

    /// <summary>Loads stored configuration at application start.</summary>
    public async Task LoadAsync()
    {
        Host = await GetSecureAsync(HostKey);
        PinnedFingerprint = await GetSecureAsync(PinKey);
        Token = await GetSecureAsync(TokenKey);
    }

    /// <summary>Saves host + pin; clears the old token (a fresh login follows).</summary>
    public async Task SaveHostAsync(string hostUrl, string? pinnedFingerprint)
    {
        Host = hostUrl.TrimEnd('/');
        PinnedFingerprint = string.IsNullOrWhiteSpace(pinnedFingerprint) ? null : pinnedFingerprint.Trim().ToLowerInvariant();
        Token = null;

        await SetSecureAsync(HostKey, Host);
        await SetSecureAsync(PinKey, PinnedFingerprint);
        await SetSecureAsync(TokenKey, null);
    }

    public async Task SetTokenAsync(string? token)
    {
        Token = token;
        await SetSecureAsync(TokenKey, token);
    }

    /// <summary>Forgets everything (host, pin, token).</summary>
    public async Task ClearAsync()
    {
        Host = null;
        PinnedFingerprint = null;
        Token = null;

        SecureStorage.Remove(HostKey);
        SecureStorage.Remove(PinKey);
        SecureStorage.Remove(TokenKey);
        await Task.CompletedTask;
    }

    private static async Task<string?> GetSecureAsync(string key)
    {
        try
        {
            return await SecureStorage.GetAsync(key);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SecureStorage read failed for {key}", ex);
            return null;
        }
    }

    private static async Task SetSecureAsync(string key, string? value)
    {
        try
        {
            if (value is null)
            {
                SecureStorage.Remove(key);
                return;
            }

            await SecureStorage.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"SecureStorage write failed for {key}", ex);
        }
    }
}
