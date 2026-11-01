using System.Collections.Concurrent;
using System.Security.Cryptography;
using Doopass.Cli.Services;
using Doopass.Core;
using Doopass.Core.Configuration;
using Doopass.Core.Storage;
using Doopass.Core.Validation;
using Doopass.WebUi.Abstractions;

namespace Doopass.Cli.Api;

/// <summary>An authenticated thin-client session over the REST API (sliding TTL).</summary>
public sealed class ApiSession(string token)
{
    /// <summary>The opaque bearer token that identifies this session.</summary>
    public string Token { get; } = token;

    /// <summary>When the token was issued; the absolute TTL is measured from here.</summary>
    public DateTimeOffset CreatedUtc { get; internal set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of the last authorized call; drives the sliding TTL.</summary>
    public DateTimeOffset LastSeenUtc { get; internal set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Hosts the single unlocked <see cref="Store"/> shared by all API tokens.
/// One store in memory (instead of one per session) removes the race of two
/// parallel stores writing the same <c>store.enc</c>. The coordinator also owns
/// token registration, the sliding session TTL and the auto-relock when the
/// last session goes away (logout, TTL expiry).
/// </summary>
public sealed class ApiVaultCoordinator
{
    /// <summary>Session lifetime used when <c>api.session_ttl_minutes</c> is absent (30 minutes).</summary>
    public static readonly TimeSpan DefaultSessionTtl = TimeSpan.FromMinutes(30);

    /// <summary>Hard cap on a token's lifetime, no matter how active it is.</summary>
    public static readonly TimeSpan DefaultAbsoluteSessionTtl = TimeSpan.FromHours(8);

    private readonly object _gate = new();
    private readonly Settings _settings;
    private readonly IVaultChangeNotifier _notifier;
    private readonly AuthThrottle _throttle;
    private readonly ConcurrentDictionary<string, ApiSession> _sessions = new();
    private Store? _store;

    /// <summary>Creates the coordinator, reading the optional session TTLs from settings.</summary>
    /// <param name="settings">Host-wide settings (store path, backups, API options).</param>
    /// <param name="notifier">Server-wide change notifier used to wake clients.</param>
    /// <param name="throttle">Shared master-password failure counter.</param>
    public ApiVaultCoordinator(Settings settings, IVaultChangeNotifier notifier, AuthThrottle throttle)
    {
        _settings = settings;
        _notifier = notifier;
        _throttle = throttle;

        // Overridable for TTL tests.
        Clock = () => DateTimeOffset.UtcNow;

        var ttl = DefaultSessionTtl;

        if (settings.Sections.Contains("api", StringComparer.Ordinal))
        {
            var api = settings.GetSection("api");

            if (api.TryGetValue("session_ttl_minutes", out var raw)
                && double.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
                && minutes > 0)
            {
                ttl = TimeSpan.FromMinutes(minutes);
            }
        }

        SessionTtl = ttl;

        var absoluteTtl = DefaultAbsoluteSessionTtl;

        if (settings.Sections.Contains("api", StringComparer.Ordinal))
        {
            var api = settings.GetSection("api");

            if (api.TryGetValue("absolute_session_ttl_hours", out var rawHours)
                && double.TryParse(rawHours, System.Globalization.CultureInfo.InvariantCulture, out var hours)
                && hours > 0)
            {
                absoluteTtl = TimeSpan.FromHours(hours);
            }
        }

        AbsoluteSessionTtl = absoluteTtl;
    }

    /// <summary>Time source; replaced with a fake clock in TTL tests.</summary>
    public Func<DateTimeOffset> Clock { get; set; }

    /// <summary>Sliding session TTL (reads <c>api.session_ttl_minutes</c> from settings.json when present).</summary>
    public TimeSpan SessionTtl { get; }

    /// <summary>
    /// Absolute session lifetime (reads <c>api.absolute_session_ttl_hours</c>, 8 h
    /// by default): an active token must still not live forever.
    /// </summary>
    public TimeSpan AbsoluteSessionTtl { get; }

    /// <summary>The host version reported to clients.</summary>
    public string ServerVersion => Doopass.Core.Constants.Version;

    /// <summary>Number of live sessions (exposed for tests and diagnostics).</summary>
    public int SessionCount { get { lock (_gate) { return _sessions.Count; } } }

    /// <summary>
    /// Per-test isolation: drops every token, relocks and removes the store file,
    /// restoring the clock source. Not part of the production surface.
    /// </summary>
    internal void TestReset()
    {
        lock (_gate)
        {
            _sessions.Clear();
            _store = null;
            Clock = () => DateTimeOffset.UtcNow;
            _throttle.Reset();

            if (File.Exists(_settings.StoreFile))
            {
                File.Delete(_settings.StoreFile);
            }

            if (Directory.Exists(_settings.BackupsDir))
            {
                Directory.Delete(_settings.BackupsDir, recursive: true);
            }
        }
    }

    /// <summary>Safe-view of the vault state; reveals no secrets.</summary>
    public VaultState GetState()
    {
        lock (_gate)
        {
            return new VaultState(StoreExistsCore, _store is not null, ServerVersion);
        }
    }

    private bool StoreExistsCore => Store.DoesStoreFileExist(_settings.StoreFile);

    /// <summary>
    /// How long a REST login should pause before checking the password. Shares
    /// the host-wide failure counter with the Blazor page; it never refuses, so
    /// the ASP.NET limiter remains the only hard cap on REST attempts.
    /// </summary>
    public TimeSpan LoginDelay() => _throttle.DelayBeforeAttempt();

    /// <summary>
    /// Validates the master password and issues a token. When some session has
    /// already unlocked the vault, a correct password just adds a token — the
    /// live store is kept so parallel clients never race over the file.
    /// </summary>
    public (bool Success, string? Token, string? Error) Login(string password)
    {
        lock (_gate)
        {
            PruneExpired_locked(Clock());

            if (!StoreExistsCore)
            {
                return (false, null, "Store is not initialized. Please initialize it first.");
            }

            var store = Store.TryInitializeStore(password, _settings.StoreFile);

            if (store is null)
            {
                _throttle.RegisterFailure();
                return (false, null, "Incorrect password!");
            }

            _throttle.RegisterSuccess();
            _store ??= store;
            var token = NewToken();
            _sessions[token] = NewSession_locked(token);
            return (true, token, null);
        }
    }

    /// <summary>Creates a new store, validating the password first. Fails when the store already exists.</summary>
    public (bool Success, string? Token, string? Error) Init(string password)
    {
        lock (_gate)
        {
            PruneExpired_locked(Clock());

            if (StoreExistsCore)
            {
                return (false, null, "Store already exists!");
            }

            var (valid, message) = new PasswordValidator(password).Validate();

            if (!valid)
            {
                return (false, null, message);
            }

            _store = new Store(password, _settings.StoreFile);
            var token = NewToken();
            _sessions[token] = NewSession_locked(token);
            return (true, token, null);
        }
    }

    /// <summary>Expires the given token. When the last session goes away, the vault relocks.</summary>
    public void Logout(string token)
    {
        lock (_gate)
        {
            if (_sessions.TryRemove(token, out _))
            {
                RelockIfIdle_locked();
            }
        }
    }

    /// <summary>Removes expired sessions; sliding TTL, so any tracked call refreshes <c>LastSeenUtc</c>.</summary>
    public void PruneExpiredSessions()
    {
        lock (_gate)
        {
            PruneExpired_locked(Clock());
        }
    }

    /// <summary>Validates a token and slides its TTL on authorized calls; null when unknown/expired.</summary>
    public string? TouchToken(string token)
    {
        lock (_gate)
        {
            PruneExpired_locked(Clock());

            if (_sessions.TryGetValue(token, out var session))
            {
                session.LastSeenUtc = Clock();
                return token;
            }

            return null;
        }
    }

    /// <summary>Validates a hub token WITHOUT sliding its TTL.</summary>
    public bool IsTokenValid(string token)
    {
        lock (_gate)
        {
            PruneExpired_locked(Clock());
            return _sessions.ContainsKey(token);
        }
    }

    /// <summary>Lists every key; requires a live token.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <returns>Keys in storage order.</returns>
    public IReadOnlyList<string> GetKeys(string token)
    {
        return UseStore(token, static store => store.GetKeys());
    }

    /// <summary>Reads one value; requires a live token.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <param name="key">Key to read.</param>
    /// <returns>The stored value.</returns>
    public string GetValue(string token, string key)
    {
        return UseStore(token, store => store.GetValue(key));
    }

    /// <summary>Adds a pair, persists it and notifies every client.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <param name="key">New key; must be unique.</param>
    /// <param name="value">Secret value.</param>
    public void Add(string token, string key, string value)
    {
        UseStore(token, store => store.AddProperty(key, value));
        _notifier.NotifyVaultChanged();
    }

    /// <summary>Renames a key and replaces its value (remove + add), then notifies clients.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <param name="oldKey">Existing key.</param>
    /// <param name="newKey">Target key.</param>
    /// <param name="newValue">New value.</param>
    public void Update(string token, string oldKey, string newKey, string newValue)
    {
        // Same semantics as the web session: remove + add.
        UseStore(token, store =>
        {
            store.RemoveProperty(oldKey);
            store.AddProperty(newKey, newValue);
        });
        _notifier.NotifyVaultChanged();
    }

    /// <summary>Removes a key and notifies every client.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <param name="key">Key to remove.</param>
    public void Remove(string token, string key)
    {
        UseStore(token, store => store.RemoveProperty(key));
        _notifier.NotifyVaultChanged();
    }

    /// <summary>Re-reads the store from disk under the current password.</summary>
    public void Reload(string token)
    {
        lock (_gate)
        {
            RequireToken_locked(token);

            if (_store is not null)
            {
                _store = new Store(_store.Password, _settings.StoreFile);
            }
        }
    }

    /// <summary>Backup names without extension, newest first (token must be live).</summary>
    public IReadOnlyList<string> GetBackupNames(string token)
    {
        lock (_gate)
        {
            RequireToken_locked(token);
            return BuildStoreBackup().GetBackupNames();
        }
    }

    /// <summary>Creates a dated snapshot of the vault and notifies clients.</summary>
    /// <param name="token">Caller's bearer token.</param>
    public void CreateBackup(string token)
    {
        lock (_gate)
        {
            RequireToken_locked(token);
            BuildStoreBackup().MakeBackup();
        }

        _notifier.NotifyBackupsChanged();
    }

    /// <summary>Swaps the store with a backup (safety backup first) and re-reads the unlocked store.</summary>
    public void RestoreBackup(string token, string backupName)
    {
        lock (_gate)
        {
            RequireToken_locked(token);

            var password = _store?.Password
                ?? throw new StoreIsNotInitializedError("Store isn't initialized!");

            var backups = BuildStoreBackup();
            RequireExistingBackup_locked(backups, backupName);

            var name = backupName + _settings.BackupExtension;

            // Validate the candidate BEFORE any write: swapping in a file we cannot
            // decrypt (tampered, truncated, from another vault) would destroy the
            // live store, and only then would the failure be noticed.
            Store.Decrypt(password, backups.GetBackupContent(name));

            backups.SwapStoreWithBackup(name);
            _store = new Store(password, _settings.StoreFile);
        }

        _notifier.NotifyBackupsChanged();
        _notifier.NotifyVaultChanged();
    }

    /// <summary>Deletes a snapshot by extensionless name and notifies clients.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <param name="backupName">Snapshot name without the configured extension.</param>
    public void DeleteBackup(string token, string backupName)
    {
        lock (_gate)
        {
            RequireToken_locked(token);

            var backups = BuildStoreBackup();
            RequireExistingBackup_locked(backups, backupName);

            backups.DeleteBackup(backupName + _settings.BackupExtension);
        }

        _notifier.NotifyBackupsChanged();
    }

    /// <summary>
    /// Rejects unsafe (path-traversal) and unknown backup names before any file
    /// operation — including the safety backup a restore would otherwise write.
    /// </summary>
    private static void RequireExistingBackup_locked(StoreBackup backups, string backupName)
    {
        if (!StoreBackup.IsSafeBackupName(backupName)
            || !backups.GetBackupNames().Contains(backupName, StringComparer.Ordinal))
        {
            throw new PropertyDoesNotExistError($"Backup '{backupName}' does not exist!");
        }
    }

    /// <summary>Reads every settings section; requires a live token.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <returns>Section names paired with a copy of their key/value pairs.</returns>
    public IReadOnlyList<(string Section, IReadOnlyDictionary<string, string> Pairs)> GetSettings(string token)
    {
        return UseStore(token, _ =>
        {
            return _settings.Sections
                .Select(section => (Section: section, Pairs: VisibleSection(section)))
                .ToList();
        });
    }

    // The TLS key password is a secret: the settings UI/API must never return
    // it, even to an authenticated client.
    private IReadOnlyDictionary<string, string> VisibleSection(string section)
    {
        var pairs = _settings.GetSection(section);

        if (!string.Equals(section, "api", StringComparison.Ordinal) || !pairs.ContainsKey("tls_pass"))
        {
            return pairs;
        }

        return pairs
            .Where(pair => pair.Key != "tls_pass")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>Writes one setting value and notifies clients.</summary>
    /// <param name="token">Caller's bearer token.</param>
    /// <param name="section">Existing section name.</param>
    /// <param name="key">Setting key (created when absent).</param>
    /// <param name="value">New value.</param>
    public void SetSetting(string token, string section, string key, string value)
    {
        lock (_gate)
        {
            RequireToken_locked(token);

            if (!_settings.Sections.Contains(section, StringComparer.Ordinal))
            {
                throw new PropertyDoesNotExistError($"Settings section '{section}' does not exist!");
            }

            _settings.ModifyProperty(section, key, value);
        }

        _notifier.NotifySettingsChanged();
    }

    /// <summary>Rewrites <c>settings.json</c> from the embedded defaults and notifies clients.</summary>
    /// <param name="token">Caller's bearer token.</param>
    public void RestoreDefaultSettings(string token)
    {
        lock (_gate)
        {
            RequireToken_locked(token);
            _settings.RestoreDefaults();
        }

        _notifier.NotifySettingsChanged();
    }

    private void RequireToken_locked(string token)
    {
        if (!_sessions.ContainsKey(token))
        {
            throw new UnauthorizedAccessException("Session expired or unauthorized.");
        }
    }

    /// <summary>Validates the token and runs <paramref name="action"/> against the coordinator's unlocked store.</summary>
    private T UseStore<T>(string token, Func<Store, T> action)
    {
        lock (_gate)
        {
            RequireToken_locked(token);
            return action(_store!);
        }
    }

    private void UseStore(string token, Action<Store> action)
    {
        lock (_gate)
        {
            RequireToken_locked(token);
            action(_store!);
        }
    }

    private StoreBackup BuildStoreBackup() => new(_settings.StoreFile, _settings.BackupsDir, _settings.BackupExtension);

    private void PruneExpired_locked(DateTimeOffset now)
    {
        var expired = _sessions
            .Where(kv => now - kv.Value.LastSeenUtc > SessionTtl
                         || now - kv.Value.CreatedUtc > AbsoluteSessionTtl)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var token in expired)
        {
            _sessions.TryRemove(token, out _);

            // Only the expired token's clients care: the vault itself may still be
            // unlocked for everybody else (an idle phone must not kick the browser).
            _notifier.NotifySessionExpired(token);
        }

        RelockIfIdle_locked();
    }

    /// <summary>When no sessions remain, drop the unlocked store (relock) and tell every hub group.</summary>
    private void RelockIfIdle_locked()
    {
        if (_store is null || _sessions.Count > 0)
        {
            return;
        }

        _store = null;
        _notifier.NotifyVaultLocked(string.Empty);
    }

    private ApiSession NewSession_locked(string token)
    {
        var now = Clock();
        return new ApiSession(token) { CreatedUtc = now, LastSeenUtc = now };
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
