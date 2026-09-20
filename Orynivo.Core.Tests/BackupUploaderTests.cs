using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Orynivo.Library;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies the WebDAV backup upload request and its error handling.</summary>
public sealed class BackupUploaderTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "orynivo-upload-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A successful upload sends a PUT with Basic credentials and zip content.</summary>
    [Fact]
    public async Task UploadAsync_SendsPutWithCredentials()
    {
        var handler = new StubHandler(HttpStatusCode.Created);
        var file = CreateArchive();
        var uploader = new BackupUploader(new HttpClient(handler));

        await uploader.UploadAsync("https://cloud.example.com/dav/orynivo-backup-1.zip", file, "alice", "s3cret");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://cloud.example.com/dav/orynivo-backup-1.zip", request.Uri);
        Assert.Equal("application/zip", request.ContentType);
        Assert.Equal(
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:s3cret"))),
            request.Authorization);
    }

    /// <summary>Without credentials no Authorization header is sent.</summary>
    [Fact]
    public async Task UploadAsync_OmitsAuthorizationWithoutCredentials()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var uploader = new BackupUploader(new HttpClient(handler));

        await uploader.UploadAsync("https://cloud.example.com/dav/a.zip", CreateArchive(), null, null);

        Assert.Null(Assert.Single(handler.Requests).Authorization);
    }

    /// <summary>A rejected upload throws without exposing credentials.</summary>
    [Fact]
    public async Task UploadAsync_ThrowsOnRejectedUpload()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized);
        var uploader = new BackupUploader(new HttpClient(handler));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uploader.UploadAsync("https://cloud.example.com/dav/a.zip", CreateArchive(), "alice", "s3cret"));

        Assert.Contains("401", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>An unsupported target is rejected before any request is made.</summary>
    [Fact]
    public async Task UploadAsync_RejectsUnsupportedTarget()
    {
        var handler = new StubHandler(HttpStatusCode.OK);
        var uploader = new BackupUploader(new HttpClient(handler));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            uploader.UploadAsync("ftp://cloud.example.com/a.zip", CreateArchive(), null, null));

        Assert.Empty(handler.Requests);
    }

    private string CreateArchive()
    {
        var path = Path.Combine(_directory, BackupNaming.BuildFileName(DateTimeOffset.UtcNow));
        File.WriteAllBytes(path, [0x50, 0x4b, 0x03, 0x04]);
        return path;
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Uri,
        string? ContentType,
        AuthenticationHeaderValue? Authorization);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StubHandler(HttpStatusCode statusCode) => _statusCode = statusCode;

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var length = request.Content is null ? 0 : (await request.Content.ReadAsByteArrayAsync(cancellationToken)).Length;
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                request.Headers.Authorization));
            return new HttpResponseMessage(_statusCode) { Content = new ByteArrayContent(new byte[length]) };
        }
    }
}
