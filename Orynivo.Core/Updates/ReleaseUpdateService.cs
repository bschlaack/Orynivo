using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orynivo.Updates;

/// <summary>Describes one downloadable artifact in a signed Orynivo release manifest.</summary>
/// <param name="Component">Artifact component, such as <c>desktop</c> or <c>server</c>.</param>
/// <param name="OperatingSystem">Target operating system.</param>
/// <param name="Architecture">Target architecture.</param>
/// <param name="Type">Packaging type.</param>
/// <param name="File">GitHub Release asset file name.</param>
/// <param name="Sha256">Lowercase hexadecimal SHA-256 digest.</param>
public sealed record ReleaseAssetInfo(
    string Component,
    string OperatingSystem,
    string Architecture,
    string Type,
    string File,
    string Sha256);

/// <summary>Represents the signed update manifest attached to an Orynivo GitHub Release.</summary>
/// <param name="Version">Semantic release version without a <c>v</c> prefix.</param>
/// <param name="Tag">Git tag that produced the release.</param>
/// <param name="Assets">Supported release artifacts and their digests.</param>
public sealed record ReleaseManifest(string Version, string Tag, IReadOnlyList<ReleaseAssetInfo> Assets);

/// <summary>Contains a verified manifest together with its original signed representation.</summary>
/// <param name="Manifest">Parsed manifest.</param>
/// <param name="ManifestBytes">Exact signed JSON bytes.</param>
/// <param name="SignatureBytes">ECDSA signature bytes.</param>
public sealed record VerifiedReleaseManifest(
    ReleaseManifest Manifest,
    byte[] ManifestBytes,
    byte[] SignatureBytes);

/// <summary>Provides authenticated discovery and download of official Orynivo release assets.</summary>
public sealed class ReleaseUpdateService : IDisposable
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/bschlaack/Orynivo/releases/latest";
    private readonly HttpClient _httpClient;
    private readonly string _publicKeyBase64;

    /// <summary>Initializes a release-update client.</summary>
    /// <param name="publicKeyBase64">ECDSA P-256 SubjectPublicKeyInfo encoded as Base64.</param>
    /// <param name="httpClient">Optional HTTP client owned by the caller.</param>
    public ReleaseUpdateService(string publicKeyBase64, HttpClient? httpClient = null)
    {
        _publicKeyBase64 = publicKeyBase64;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo-UpdateClient/1.0");
        OwnsHttpClient = httpClient is null;
    }

    private bool OwnsHttpClient { get; }

    /// <summary>Downloads and verifies the manifest of the latest published GitHub Release.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verified manifest.</returns>
    /// <exception cref="InvalidOperationException">Thrown when signing is not configured or verification fails.</exception>
    public async Task<ReleaseManifest> GetLatestManifestAsync(CancellationToken cancellationToken = default)
        => (await GetLatestManifestBundleAsync(cancellationToken)).Manifest;

    /// <summary>Downloads and verifies the latest manifest while retaining its signed bytes for server relay.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Verified parsed and raw manifest data.</returns>
    public async Task<VerifiedReleaseManifest> GetLatestManifestBundleAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_publicKeyBase64))
            throw new InvalidOperationException("Update signature verification is not configured.");

        var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned no release metadata.");
        var manifestAsset = release.Assets.FirstOrDefault(a => a.Name == "update-manifest.json")
            ?? throw new InvalidOperationException("The release has no update manifest.");
        var signatureAsset = release.Assets.FirstOrDefault(a => a.Name == "update-manifest.sig")
            ?? throw new InvalidOperationException("The release has no update signature.");

        var manifestBytes = await _httpClient.GetByteArrayAsync(manifestAsset.BrowserDownloadUrl, cancellationToken);
        var signature = await _httpClient.GetByteArrayAsync(signatureAsset.BrowserDownloadUrl, cancellationToken);
        VerifySignature(manifestBytes, signature);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidOperationException("The update manifest is invalid.");
        return new VerifiedReleaseManifest(manifest, manifestBytes, signature);
    }

    /// <summary>Downloads an asset to a temporary file and verifies its SHA-256 digest.</summary>
    /// <param name="asset">Manifest asset to download.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Absolute path of the verified temporary file.</returns>
    public async Task<string> DownloadAssetAsync(ReleaseAssetInfo asset, CancellationToken cancellationToken = default)
    {
        var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(LatestReleaseUrl, cancellationToken)
            ?? throw new InvalidOperationException("GitHub returned no release metadata.");
        var githubAsset = release.Assets.FirstOrDefault(a => string.Equals(a.Name, asset.File, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The requested release asset is missing.");
        var tempPath = Path.Combine(Path.GetTempPath(), $"orynivo-{Guid.NewGuid():N}-{Path.GetFileName(asset.File)}");
        await using (var destination = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var source = await _httpClient.GetStreamAsync(githubAsset.BrowserDownloadUrl, cancellationToken))
            await source.CopyToAsync(destination, cancellationToken);

        await using var verifyStream = File.OpenRead(tempPath);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(verifyStream, cancellationToken)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(digest), Encoding.ASCII.GetBytes(asset.Sha256.ToLowerInvariant())))
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("The downloaded update checksum does not match the signed manifest.");
        }
        return tempPath;
    }

    /// <summary>Returns whether <paramref name="availableVersion"/> is newer than the current build.</summary>
    /// <param name="currentVersion">Current informational version.</param>
    /// <param name="availableVersion">Published semantic version.</param>
    /// <returns><see langword="true"/> when an update is available.</returns>
    public static bool IsNewer(string currentVersion, string availableVersion)
        => Version.TryParse(currentVersion.Split('-', '+')[0], out var current)
           && Version.TryParse(availableVersion, out var available)
           && available > current;

    /// <inheritdoc />
    public void Dispose()
    {
        if (OwnsHttpClient)
            _httpClient.Dispose();
    }

    private void VerifySignature(byte[] data, byte[] signature)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKeyBase64), out _);
        if (!ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new InvalidOperationException("The update manifest signature is invalid.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed record GitHubRelease(IReadOnlyList<GitHubAsset> Assets);
    private sealed record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
