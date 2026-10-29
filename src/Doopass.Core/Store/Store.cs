using System.Security.Cryptography;
using System.Text;
using Doopass.Core.Crypto;
using Doopass.Core.Diagnostics;

namespace Doopass.Core.Storage;

/// <summary>
/// An encrypted key/value store. The on-disk format is fully compatible with
/// the original Doopass: <c>base64url( salt[16] + iterations[4] + fernet_token )</c>,
/// where the plaintext is a list of <c>key = value</c> lines.
/// </summary>
public sealed class Store
{
    /// <summary>Default PBKDF2 iteration count written into new store containers.</summary>
    public const int DefaultIterations = 100_000;

    private readonly List<Entry> _entries = new();

    /// <summary>
    /// Opens the store at <paramref name="storePath"/> with
    /// <paramref name="password"/> and loads all entries. When the file does not
    /// exist, an empty encrypted store is created first.
    /// </summary>
    /// <param name="password">The master password used to decrypt the file.</param>
    /// <param name="storePath">Full path of the <c>store.enc</c> file.</param>
    /// <exception cref="IncorrectPasswordError">The file cannot be decrypted with the password.</exception>
    public Store(string password, string storePath)
    {
        Password = password;
        StorePath = storePath;

        if (!File.Exists(storePath))
        {
            var directory = Path.GetDirectoryName(storePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(storePath, Encrypt(password, []), TextFile.Utf8NoBom);
            AppLogger.Info($"Store initialized: {storePath}");
        }

        Load();
        AppLogger.Info($"Store opened: {storePath} ({_entries.Count} entries)");
    }

    /// <summary>The master password for this store (kept in memory only).</summary>
    public string Password { get; }

    /// <summary>The path to the encrypted store file.</summary>
    public string StorePath { get; }

    /// <summary>Returns all keys in insertion order.</summary>
    public IReadOnlyList<string> GetKeys() => _entries.Select(e => e.Key).ToList();

    /// <summary>Returns true when a property with the given name exists.</summary>
    public bool DoesPropertyExist(string name)
    {
        return _entries.Any(e => e.Key == name);
    }

    /// <summary>Returns the value for the given key.</summary>
    public string GetValue(string name)
    {
        var index = FindIndex(name);

        if (index < 0)
        {
            throw new PropertyDoesNotExistError($"The property '{name}' is not set in the store!");
        }

        return _entries[index].Value;
    }

    /// <summary>Adds a new key/value pair to the store.</summary>
    public void AddProperty(string name, string value)
    {
        if (DoesPropertyExist(name))
        {
            throw new PropertyAlreadyExistError($"This property ({name}) already exists!");
        }

        _entries.Add(new Entry(name, value));
        Save();
        AppLogger.Info($"Property added: {name}");
    }

    /// <summary>Updates the value of an existing key.</summary>
    public void SetValue(string name, string value)
    {
        var index = FindIndex(name);

        if (index < 0)
        {
            throw new PropertyDoesNotExistError($"The property ({name}) does not exist!");
        }

        _entries[index] = new Entry(name, value);
        Save();
        AppLogger.Info($"Property updated: {name}");
    }

    /// <summary>Removes an existing key.</summary>
    public void RemoveProperty(string name)
    {
        var index = FindIndex(name);

        if (index < 0)
        {
            throw new PropertyDoesNotExistError($"The property ({name}) does not exist!");
        }

        _entries.RemoveAt(index);
        Save();
        AppLogger.Info($"Property removed: {name}");
    }

    /// <summary>Encrypts plaintext bytes with the store password.</summary>
    public string Encrypt(byte[] source) => Encrypt(Password, source, DefaultIterations);

    /// <summary>Encrypts plaintext bytes with an explicit password.</summary>
    public static string Encrypt(string password, byte[] source, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Pbkdf2.DeriveKey(password, salt, iterations);

        var token = Fernet.Encrypt(key, source);
        var tokenBytes = Base64Url.Decode(token);

        var result = new byte[16 + 4 + tokenBytes.Length];
        salt.CopyTo(result.AsSpan(0, 16));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16, 4), iterations);
        tokenBytes.CopyTo(result.AsSpan(20));

        return Base64Url.Encode(result);
    }

    /// <summary>Decrypts a full store payload (salt + iterations + token).</summary>
    public static byte[] Decrypt(string password, string source)
    {
        var decoded = Base64Url.Decode(source.Trim());

        var salt = decoded.AsSpan(0, 16).ToArray();
        var iterations = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(decoded.AsSpan(16, 4));
        var token = Base64Url.Encode(decoded.AsSpan(20).ToArray());

        var key = Pbkdf2.DeriveKey(password, salt, iterations);

        return Fernet.Decrypt(key, token);
    }

    /// <summary>Tries to initialize a store; returns <c>null</c> if the password is wrong.</summary>
    public static Store? TryInitializeStore(string password, string storePath)
    {
        try
        {
            return new Store(password, storePath);
        }
        catch (IncorrectPasswordError)
        {
            AppLogger.Debug($"Store open failed (incorrect password): {storePath}");
            return null;
        }
    }

    /// <summary>
    /// Initializes a store that must already exist on disk.
    /// Throws <see cref="StoreIsNotInitializedError"/> when it does not.
    /// </summary>
    public static Store TryInitializeExistingStore(string password, string storePath)
    {
        if (!File.Exists(storePath))
        {
            throw new StoreIsNotInitializedError("Store isn't initialized!");
        }

        return new Store(password, storePath);
    }

    /// <summary>Returns true when the store file exists on disk.</summary>
    public static bool DoesStoreFileExist(string storePath) => File.Exists(storePath);

    private void Load()
    {
        _entries.Clear();

        var content = File.ReadAllText(StorePath, Encoding.UTF8);
        var plaintext = Encoding.UTF8.GetString(Decrypt(Password, content));

        foreach (var rawLine in plaintext.Split('\n'))
        {
            var line = rawLine.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            var key = separator < 0 ? line : line[..separator].Trim();
            var value = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

            _entries.Add(new Entry(key, value));
        }
    }

    private void Save()
    {
        var plaintext = string.Join("\n", _entries.Select(e => $"{e.Key} = {e.Value}"));
        var encrypted = Encrypt(Password, Encoding.UTF8.GetBytes(plaintext));
        File.WriteAllText(StorePath, encrypted, TextFile.Utf8NoBom);
    }

    private int FindIndex(string name) => _entries.FindIndex(e => e.Key == name);

    private sealed record Entry(string Key, string Value);
}
