using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orynivo.Streaming;

public sealed class WindowsPlexCredentialStore
{
    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Orynivo.PlexCredentials.v1"));

    private readonly string _filePath;

    public WindowsPlexCredentialStore()
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _filePath = AppPaths.GetDataPath("plex-credentials.dat");
    }

    public async Task<Dictionary<string, string>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            return [];

        var protectedData = await File.ReadAllBytesAsync(_filePath, cancellationToken)
            .ConfigureAwait(false);
        var json = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    public Dictionary<string, string> LoadAll()
    {
        if (!File.Exists(_filePath))
            return [];

        var protectedData = File.ReadAllBytes(_filePath);
        var json = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    public async Task SaveAllAsync(
        IReadOnlyDictionary<string, string> credentials,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedData = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = _filePath + ".tmp";

        await File.WriteAllBytesAsync(temporaryPath, protectedData, cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporaryPath, _filePath, true);
    }

    public void SaveAll(IReadOnlyDictionary<string, string> credentials)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedData = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = _filePath + ".tmp";

        File.WriteAllBytes(temporaryPath, protectedData);
        File.Move(temporaryPath, _filePath, true);
    }
}
