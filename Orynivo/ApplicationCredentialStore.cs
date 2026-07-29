using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>
/// Persists all application credentials in one current-user credential container.
/// Windows uses DPAPI; Linux and macOS use AES-GCM with a separate user-only key file.
/// </summary>
internal sealed class ApplicationCredentialStore
{
    private static readonly byte[] WindowsEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Orynivo.ApplicationCredentials.v1"));
    private static readonly byte[] LegacyPlexEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Orynivo.PlexCredentials.v1"));
    private static readonly byte[] LegacyStreamingEntropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Orynivo.StreamingCredentials.v1"));
    private static readonly byte[] Header = "ORYC1"u8.ToArray();
    private static readonly object SyncRoot = new();
    private readonly string _credentialPath;
    private readonly string _keyPath;

    /// <summary>Initializes the credential store beneath the application data directory.</summary>
    internal ApplicationCredentialStore()
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _credentialPath = AppPaths.GetDataPath("credentials.dat");
        _keyPath = AppPaths.GetDataPath("credentials.key");
    }

    /// <summary>Loads and decrypts the complete credential snapshot.</summary>
    /// <returns>The stored credentials, or an empty snapshot when none exist.</returns>
    internal ApplicationCredentialSnapshot Load()
    {
        lock (SyncRoot)
        {
            return LoadCore();
        }
    }

    /// <summary>Encrypts and atomically replaces the complete credential snapshot.</summary>
    /// <param name="snapshot">Credentials to persist.</param>
    internal void Save(ApplicationCredentialSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (SyncRoot)
        {
            SaveCore(snapshot);
        }
    }

    /// <summary>Loads the Plex token map without exposing other stored credentials.</summary>
    /// <returns>A copy of the server-ID-to-token map.</returns>
    internal Dictionary<string, string> LoadPlexTokens()
    {
        lock (SyncRoot)
        {
            return new Dictionary<string, string>(
                LoadCore().PlexTokens,
                StringComparer.Ordinal);
        }
    }

    /// <summary>Replaces the Plex token map while preserving every other credential category.</summary>
    /// <param name="credentials">Plex tokens keyed by server ID.</param>
    internal void SavePlexTokens(IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        lock (SyncRoot)
        {
            var snapshot = LoadCore();
            snapshot.PlexTokens = credentials
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            SaveCore(snapshot);
        }
    }

    /// <summary>Loads one generic streaming-provider credential.</summary>
    /// <param name="provider">Provider whose credential should be returned.</param>
    /// <returns>The stored credential, or <see langword="null"/> when absent.</returns>
    internal StreamingCredential? LoadStreamingCredential(StreamingProvider provider)
    {
        lock (SyncRoot)
        {
            return LoadCore().StreamingCredentials.GetValueOrDefault(provider);
        }
    }

    /// <summary>Saves one generic streaming-provider credential.</summary>
    /// <param name="provider">Provider whose credential should be replaced.</param>
    /// <param name="credential">Credential to encrypt.</param>
    internal void SaveStreamingCredential(
        StreamingProvider provider,
        StreamingCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        lock (SyncRoot)
        {
            var snapshot = LoadCore();
            snapshot.StreamingCredentials[provider] = credential;
            SaveCore(snapshot);
        }
    }

    /// <summary>Removes one generic streaming-provider credential.</summary>
    /// <param name="provider">Provider whose credential should be removed.</param>
    internal void RemoveStreamingCredential(StreamingProvider provider)
    {
        lock (SyncRoot)
        {
            var snapshot = LoadCore();
            if (snapshot.StreamingCredentials.Remove(provider))
            {
                SaveCore(snapshot);
            }
        }
    }

    private ApplicationCredentialSnapshot LoadCore()
    {
        if (!File.Exists(_credentialPath))
        {
            return MigrateLegacyCredentialFiles();
        }

        var protectedData = File.ReadAllBytes(_credentialPath);
        var json = OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(
                protectedData,
                WindowsEntropy,
                DataProtectionScope.CurrentUser)
            : DecryptPortable(protectedData);
        return JsonSerializer.Deserialize<ApplicationCredentialSnapshot>(json)
            ?? new ApplicationCredentialSnapshot();
    }

    private ApplicationCredentialSnapshot MigrateLegacyCredentialFiles()
    {
        var snapshot = new ApplicationCredentialSnapshot();
        if (!OperatingSystem.IsWindows())
        {
            return snapshot;
        }

        var legacyPlexPath = AppPaths.GetDataPath("plex-credentials.dat");
        var legacyStreamingPath = AppPaths.GetDataPath("streaming-credentials.dat");
        var imported = false;
        if (File.Exists(legacyPlexPath))
        {
            var json = ProtectedData.Unprotect(
                File.ReadAllBytes(legacyPlexPath),
                LegacyPlexEntropy,
                DataProtectionScope.CurrentUser);
            snapshot.PlexTokens =
                JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
            imported = true;
        }

        if (File.Exists(legacyStreamingPath))
        {
            var json = ProtectedData.Unprotect(
                File.ReadAllBytes(legacyStreamingPath),
                LegacyStreamingEntropy,
                DataProtectionScope.CurrentUser);
            snapshot.StreamingCredentials =
                JsonSerializer.Deserialize<Dictionary<StreamingProvider, StreamingCredential>>(json)
                ?? [];
            imported = true;
        }

        if (imported)
        {
            SaveCore(snapshot);
            File.Delete(legacyPlexPath);
            File.Delete(legacyStreamingPath);
        }

        return snapshot;
    }

    private void SaveCore(ApplicationCredentialSnapshot snapshot)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
        var protectedData = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(json, WindowsEntropy, DataProtectionScope.CurrentUser)
            : EncryptPortable(json);
        WriteAtomically(_credentialPath, protectedData);
        RestrictToCurrentUser(_credentialPath);
    }

    private byte[] EncryptPortable(ReadOnlySpan<byte> plaintext)
    {
        var key = LoadOrCreatePortableKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        try
        {
            using var aes = new AesGcm(key, tag.Length);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, Header);

            var result = new byte[Header.Length + nonce.Length + tag.Length + ciphertext.Length];
            Header.CopyTo(result, 0);
            nonce.CopyTo(result, Header.Length);
            tag.CopyTo(result, Header.Length + nonce.Length);
            ciphertext.CopyTo(result, Header.Length + nonce.Length + tag.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] DecryptPortable(ReadOnlySpan<byte> protectedData)
    {
        const int nonceLength = 12;
        const int tagLength = 16;
        var payloadOffset = Header.Length + nonceLength + tagLength;
        if (protectedData.Length < payloadOffset ||
            !protectedData[..Header.Length].SequenceEqual(Header))
        {
            throw new CryptographicException("The Orynivo credential container is invalid.");
        }

        var key = LoadPortableKey();
        var plaintext = new byte[protectedData.Length - payloadOffset];
        try
        {
            using var aes = new AesGcm(key, tagLength);
            aes.Decrypt(
                protectedData.Slice(Header.Length, nonceLength),
                protectedData[payloadOffset..],
                protectedData.Slice(Header.Length + nonceLength, tagLength),
                plaintext,
                Header);
            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] LoadOrCreatePortableKey()
    {
        if (File.Exists(_keyPath))
        {
            return LoadPortableKey();
        }

        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            using var stream = new FileStream(
                _keyPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            stream.Write(key);
            stream.Flush(true);
            RestrictToCurrentUser(_keyPath);
            return key;
        }
        catch (IOException) when (File.Exists(_keyPath))
        {
            CryptographicOperations.ZeroMemory(key);
            return LoadPortableKey();
        }
    }

    private byte[] LoadPortableKey()
    {
        var key = File.ReadAllBytes(_keyPath);
        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("The Orynivo credential key is invalid.");
        }

        RestrictToCurrentUser(_keyPath);
        return key;
    }

    private static void WriteAtomically(string path, ReadOnlySpan<byte> data)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, data.ToArray());
            RestrictToCurrentUser(temporaryPath);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void RestrictToCurrentUser(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

/// <summary>Serializable payload stored only inside the encrypted credential container.</summary>
internal sealed class ApplicationCredentialSnapshot
{
    /// <summary>Gets or sets the Last.fm API key.</summary>
    public string LastFmApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the Fanart.tv API key.</summary>
    public string FanartTvApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the embedded AI-chat API key.</summary>
    public string AiChatApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets Orynivo Server API keys by server ID.</summary>
    public Dictionary<string, string> OrynivoServerApiKeys { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>Gets or sets Plex access tokens by server ID.</summary>
    public Dictionary<string, string> PlexTokens { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>Gets or sets generic streaming-provider credentials.</summary>
    public Dictionary<StreamingProvider, StreamingCredential> StreamingCredentials { get; set; } = [];
}
