using Orynivo.Library;

namespace Orynivo.Remote;

/// <summary>Keeps bounded, process-local radio and podcast identities out of browser URLs.</summary>
internal sealed class MobileRemoteMediaCatalog
{
    private readonly Dictionary<string, object> _entries = new();
    private readonly Queue<string> _order = new();

    /// <summary>Registers a source using an opaque identity, retaining at most 2,048 sources.</summary>
    /// <param name="source">Desktop-owned source metadata.</param>
    /// <returns>An identity containing no playback or feed URL.</returns>
    internal string Register(object source)
    {
        var existing = _entries.FirstOrDefault(entry => Equals(entry.Value, source));
        if (existing.Key is not null) return existing.Key;
        var id = Guid.NewGuid().ToString("N");
        _entries.Add(id, source);
        _order.Enqueue(id);
        while (_entries.Count > 2048) _entries.Remove(_order.Dequeue());
        return id;
    }

    /// <summary>Resolves only a known source of the requested kind.</summary>
    /// <typeparam name="T">Expected source type.</typeparam>
    /// <param name="id">Opaque browser identity.</param>
    /// <returns>The known source, or <see langword="null"/> for expired or mismatched identities.</returns>
    internal T? Resolve<T>(string id) where T : class =>
        _entries.TryGetValue(id, out var value) ? value as T : null;
}

/// <summary>Retains an episode together with its original podcast for progress and downloads.</summary>
/// <param name="Podcast">Owning podcast.</param>
/// <param name="Episode">Playable episode.</param>
internal sealed record MobileRemotePodcastEpisode(PodcastRecord Podcast, PodcastEpisode Episode);

/// <summary>Provides display-only radio, podcast, or episode metadata.</summary>
/// <param name="Id">Opaque process-local source identity.</param>
/// <param name="Title">Display title.</param>
/// <param name="Subtitle">Optional station or publisher metadata.</param>
/// <param name="DurationSeconds">Optional episode duration.</param>
/// <param name="PublishedAt">Optional episode publication date.</param>
public sealed record MobileRemoteMedia(string Id, string Title, string? Subtitle,
    double? DurationSeconds = null, DateTimeOffset? PublishedAt = null);
