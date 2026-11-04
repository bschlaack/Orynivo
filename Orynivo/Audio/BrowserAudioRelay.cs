using Microsoft.AspNetCore.Http;
using System.Net;
using System.Net.Http.Headers;

namespace Orynivo.Audio;

/// <summary>Relays compatible media progressively without decoding, temporary files or browser-visible source URLs.</summary>
internal static class BrowserAudioRelay
{
    private static readonly HttpClient Client = new(new HttpClientHandler
    { UseCookies = false, AutomaticDecompression = DecompressionMethods.None }) { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Recognizes common browser media containers from a trusted source path.</summary>
    /// <param name="path">Desktop-resolved path or source URL.</param>
    /// <returns>The known media type, or null when conversion is required.</returns>
    internal static string? MimeForPath(string path)
    {
        var source = Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsolutePath : path;
        return Path.GetExtension(source).ToLowerInvariant() switch
        { ".mp3" => "audio/mpeg", ".flac" => "audio/flac", ".wav" => "audio/wav", ".aif" or ".aiff" => "audio/aiff", ".dsf" => "audio/x-dsf", ".dff" => "audio/x-dff", ".m4a" or ".mp4" => "audio/mp4", ".aac" => "audio/aac", ".ogg" or ".opus" => "audio/ogg", _ => null };
    }

    /// <summary>Recognizes radio directory codec names without probing a never-ending source.</summary>
    /// <param name="codec">Directory codec metadata.</param>
    /// <returns>A known direct-stream type, or null for header-based detection.</returns>
    internal static string? MimeForCodec(string? codec) => codec?.Trim().ToUpperInvariant() switch
    { "MP3" => "audio/mpeg", "AAC" or "AAC+" or "HE-AAC" => "audio/aac", "OGG" or "VORBIS" or "OPUS" => "audio/ogg", _ => null };

    /// <summary>Returns a technical label for the actual delivered container.</summary>
    /// <param name="mime">Delivered media type.</param>
    /// <returns>Technical format label, or a neutral placeholder before headers arrive.</returns>
    internal static string Format(string? mime) => mime switch
    { "audio/mpeg" or "audio/mp3" => "MP3", "audio/flac" or "audio/x-flac" => "FLAC", "audio/wav" or "audio/x-wav" => "WAV", "audio/x-dsf" or "audio/x-dff" => "DSD", "audio/aiff" => "AIFF", "audio/aac" or "audio/aacp" => "AAC", "audio/mp4" or "audio/x-m4a" => "AAC/MP4", "audio/ogg" or "application/ogg" => "Ogg", _ => "—" };

    /// <summary>Forwards response bytes and byte ranges while keeping source authentication on the desktop.</summary>
    /// <param name="source">Trusted source path.</param>
    /// <param name="mime">Known container, if available.</param>
    /// <param name="context">Owning browser request/response.</param>
    /// <param name="formatKnown">Receives the actual upstream content type.</param>
    /// <param name="ct">Request and player cancellation.</param>
    /// <returns>Completion after upstream disposal.</returns>
    internal static async Task WriteAsync(string source, string? mime, HttpContext context, Action<string> formatKnown, CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            var previous = context.RequestAborted;
            context.RequestAborted = ct;
            try
            {
                formatKnown(mime ?? "audio/mpeg");
                await Results.File(source, mime ?? "audio/mpeg", enableRangeProcessing: true).ExecuteAsync(context).ConfigureAwait(false);
            }
            finally { context.RequestAborted = previous; }
            return;
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Icy-MetaData", "0");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        if (RangeHeaderValue.TryParse(context.Request.Headers.Range, out var range)) request.Headers.Range = range;
        using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headersTimeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token).ConfigureAwait(false);
        headersTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (response.StatusCode != HttpStatusCode.RequestedRangeNotSatisfiable) response.EnsureSuccessStatusCode();
        context.Response.StatusCode = (int)response.StatusCode;
        var actualMime = response.Content.Headers.ContentType?.MediaType;
        if (Format(actualMime) == "—") actualMime = mime ?? "audio/mpeg";
        context.Response.ContentType = actualMime;
        formatKnown(actualMime!);
        if (response.Content.Headers.ContentLength is { } length) context.Response.ContentLength = length;
        if (response.Content.Headers.ContentRange is { } contentRange) context.Response.Headers.ContentRange = contentRange.ToString();
        if (response.Headers.AcceptRanges.Contains("bytes")) context.Response.Headers.AcceptRanges = "bytes";
        await context.Response.StartAsync(ct).ConfigureAwait(false);
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);
        }
    }
}
