using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Orynivo.Library;

if (args.Contains("--live"))
{
    var watch = Stopwatch.StartNew();
    long bytes = 0;
    var count = 0;
    var results = await MusicBrainzCoverSearch.SearchByAlbumTitleAsync("Lovers Rock", "Sade",
        onResult: result =>
        {
            Interlocked.Add(ref bytes, result.ImageData.Length);
            Console.WriteLine($"preview={Interlocked.Increment(ref count)} elapsed_ms={watch.ElapsedMilliseconds} bytes={result.ImageData.Length}");
            return Task.CompletedTask;
        });
    Console.WriteLine($"complete count={results.Count} elapsed_ms={watch.ElapsedMilliseconds} bytes={bytes}");
    return;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static HttpResponseMessage Image(byte value = 1) => new(HttpStatusCode.OK)
{
    Content = new ByteArrayContent([value, 2, 3])
};
static HttpResponseMessage Releases(int count) => new(HttpStatusCode.OK)
{
    Content = new StringContent("{\"releases\":[" + string.Join(",", Enumerable.Range(0, count)
        .Select(i => $"{{\"id\":\"{i}\",\"title\":\"Album\",\"artist-credit\":[]}}")) + "]}", Encoding.UTF8, "application/json")
};

var calls = new ConcurrentDictionary<string, int>();
var active = 0;
var peak = 0;
var reported = 0;
using var handler = new FakeHandler(async (request, token) =>
{
    var uri = request.RequestUri!;
    if (uri.Host == "musicbrainz.org") return Releases(12);
    Check(uri.AbsolutePath.EndsWith("/front-250"), "Search downloaded an original");
    var attempt = calls.AddOrUpdate(uri.AbsolutePath, 1, (_, n) => n + 1);
    var concurrent = Interlocked.Increment(ref active);
    lock (calls) peak = Math.Max(peak, concurrent);
    try
    {
        await Task.Delay(30, token);
        if (uri.AbsolutePath.Contains("/0/")) return new(HttpStatusCode.NotFound);
        if (uri.AbsolutePath.Contains("/1/")) return new(HttpStatusCode.InternalServerError);
        if (uri.AbsolutePath.Contains("/2/") && attempt == 1) return new(HttpStatusCode.ServiceUnavailable);
        return Image();
    }
    finally { Interlocked.Decrement(ref active); }
});
using var client = new HttpClient(handler);
var timer = Stopwatch.StartNew();
long first = -1;
var found = await MusicBrainzCoverSearch.SearchAsync(client, "Album", "Artist", result =>
{
    Interlocked.CompareExchange(ref first, timer.ElapsedMilliseconds, -1);
    Interlocked.Increment(ref reported);
    return Task.CompletedTask;
}, default);
Check(found.Count == 10 && reported == 10, "Partial results were lost");
Check(peak == 3, "Concurrency is not bounded to three");
Check(first < 800 && timer.ElapsedMilliseconds >= 1000, "Results were not incremental");
Check(calls["/release/0/front-250"] == 1, "404 was retried");
Check(calls["/release/1/front-250"] == 2, "Permanent 500 retry count wrong");
Check(calls["/release/2/front-250"] == 2, "Transient failure was not retried");
Console.WriteLine("PASS: previews, concurrency, incremental delivery, partial failure, 404 and retry");

using var originalClient = new HttpClient(new FakeHandler((request, _) =>
{
    Check(request.RequestUri!.AbsolutePath.EndsWith("/front"), "Original endpoint not used");
    return Task.FromResult(Image(9));
}));
var original = await MusicBrainzCoverSearch.DownloadOriginalAsync(originalClient, found[0], default);
Check(original.ImageData[0] == 9 && found[0].ImageData[0] == 1, "Original replaced preview incorrectly");
Console.WriteLine("PASS: explicit original download");

using var cancel = new CancellationTokenSource(80);
using var slowClient = new HttpClient(new FakeHandler(async (request, token) =>
{
    if (request.RequestUri!.Host == "musicbrainz.org") return Releases(12);
    await Task.Delay(10000, token);
    return Image();
}));
try
{
    await MusicBrainzCoverSearch.SearchAsync(slowClient, "Album", null, null, cancel.Token);
    throw new Exception("Cancellation swallowed");
}
catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
Console.WriteLine("PASS: cancellation");

using var oversizedClient = new HttpClient(new FakeHandler((request, _) =>
{
    if (request.RequestUri!.Host == "musicbrainz.org") return Task.FromResult(Releases(1));
    var response = Image();
    response.Content.Headers.ContentLength = 3 * 1024 * 1024;
    return Task.FromResult(response);
}));
try
{
    await MusicBrainzCoverSearch.SearchAsync(oversizedClient, "Album", null, null, default);
    throw new Exception("Oversized preview accepted");
}
catch (HttpRequestException) { }
Console.WriteLine("PASS: size bound and all-failed distinction");

var queries = new List<string>();
using var fallbackClient = new HttpClient(new FakeHandler((request, _) =>
{
    if (request.RequestUri!.Host != "musicbrainz.org") return Task.FromResult(Image());
    queries.Add(Uri.UnescapeDataString(request.RequestUri.Query));
    return Task.FromResult(Releases(queries.Count == 1 ? 0 : 1));
}));
var fallback = await MusicBrainzCoverSearch.SearchAsync(fallbackClient, "M!ssundaztood", null, null, default);
Check(fallback.Count == 1 && queries.Count == 2 && queries[0].Contains("M!ssundaztood") && queries[1].Contains("Mssundaztood"), "Punctuation fallback broken");
Console.WriteLine("PASS: punctuation fallback");

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
}
