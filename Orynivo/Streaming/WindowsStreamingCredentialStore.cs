using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Orynivo.Streaming;

public sealed class WindowsStreamingCredentialStore : IStreamingCredentialStore
{
    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Orynivo.StreamingCredentials.v1"));

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowsStreamingCredentialStore()
    {
        Directory.CreateDirectory(AppPaths.DataRoot);
        _filePath = AppPaths.GetDataPath("streaming-credentials.dat");
    }

    public async Task<StreamingCredential?> LoadAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var credentials = await LoadAllAsync(cancellationToken);
            return credentials.GetValueOrDefault(provider);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        StreamingProvider provider,
        StreamingCredential credential,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var credentials = await LoadAllAsync(cancellationToken);
            credentials[provider] = credential;
            await SaveAllAsync(credentials, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(
        StreamingProvider provider,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var credentials = await LoadAllAsync(cancellationToken);
            if (credentials.Remove(provider))
                await SaveAllAsync(credentials, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<StreamingProvider, StreamingCredential>> LoadAllAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
            return [];

        var protectedData = await File.ReadAllBytesAsync(_filePath, cancellationToken);
        var json = ProtectedData.Unprotect(protectedData, Entropy, DataProtectionScope.CurrentUser);
        return JsonSerializer.Deserialize<Dictionary<StreamingProvider, StreamingCredential>>(json)
            ?? [];
    }

    private async Task SaveAllAsync(
        Dictionary<StreamingProvider, StreamingCredential> credentials,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
        var protectedData = ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser);
        var temporaryPath = _filePath + ".tmp";

        await File.WriteAllBytesAsync(temporaryPath, protectedData, cancellationToken);
        File.Move(temporaryPath, _filePath, true);
    }
}
