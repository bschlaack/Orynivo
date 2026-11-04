using Orynivo.Library;
using Orynivo.Remote;

namespace Orynivo;

public partial class MainWindow
{
    private readonly MobileRemoteMediaCatalog _mobileMedia = new();

    /// <summary>Browses saved stations or searches Radio Browser without returning stream URLs.</summary>
    /// <param name="query">Optional station search; empty selects saved stations.</param>
    /// <param name="ct">Request cancellation.</param>
    /// <returns>At most 100 safe station summaries.</returns>
    private async Task<IReadOnlyList<MobileRemoteMedia>> BrowseMobileRadioAsync(string? query, CancellationToken ct)
    {
        IReadOnlyList<RadioStationRecord> stations;
        if (string.IsNullOrWhiteSpace(query))
            stations = await Task.Run(() => { using var db = AudioDatabase.OpenDefault(); return db.GetRadioStations(); }, ct);
        else
            stations = (await _radioBrowserService.SearchAsync(query, cancellationToken: ct))
                .Select(s => new RadioStationRecord(0, s.StationUuid, s.Name, s.StreamUrl,
                    s.Homepage, s.Favicon, s.CountryCode, s.Codec, s.Bitrate, s.Tags)).ToArray();
        ct.ThrowIfCancellationRequested();
        return stations.Take(100).Select(s => new MobileRemoteMedia(_mobileMedia.Register(s), s.Name,
            string.Join(" · ", new[] { s.CountryCode, s.Codec, s.Tags }.Where(v => !string.IsNullOrWhiteSpace(v))))).ToArray();
    }

    /// <summary>Browses pinned podcasts or searches the existing Apple Podcasts catalog.</summary>
    /// <param name="query">Optional podcast search; empty selects pinned podcasts.</param>
    /// <param name="ct">Request cancellation.</param>
    /// <returns>At most 100 safe podcast summaries.</returns>
    private async Task<IReadOnlyList<MobileRemoteMedia>> BrowseMobilePodcastsAsync(string? query, CancellationToken ct)
    {
        var saved = await Task.Run(() => { using var db = AudioDatabase.OpenDefault(); return db.GetPodcasts(); }, ct);
        IReadOnlyList<PodcastRecord> podcasts;
        if (string.IsNullOrWhiteSpace(query))
            podcasts = saved;
        else
            podcasts = (await _podcastService.SearchAsync(query, cancellationToken: ct))
                .Select(p => saved.FirstOrDefault(s => s.FeedUrl == p.FeedUrl)
                    ?? new PodcastRecord(0, p.CollectionId, p.Name, p.Author, p.FeedUrl, p.ArtworkUrl, p.Genre)).ToArray();
        ct.ThrowIfCancellationRequested();
        return podcasts.Take(100).Select(p => new MobileRemoteMedia(_mobileMedia.Register(p), p.Name, p.Author)).ToArray();
    }

    /// <summary>Loads episodes for the original opaque podcast identity without exposing enclosure URLs.</summary>
    /// <param name="id">Known podcast identity.</param>
    /// <param name="ct">Request cancellation.</param>
    /// <returns>At most 500 episodes, newest first, or null for an expired identity.</returns>
    private async Task<IReadOnlyList<MobileRemoteMedia>?> BrowseMobileEpisodesAsync(string id, CancellationToken ct)
    {
        var podcast = _mobileMedia.Resolve<PodcastRecord>(id);
        if (podcast is null) return null;
        var feed = await _podcastService.GetFeedAsync(podcast.FeedUrl, ct);
        ct.ThrowIfCancellationRequested();
        return feed.Episodes.Take(500).Select(e => new MobileRemoteMedia(
            _mobileMedia.Register(new MobileRemotePodcastEpisode(podcast, e)), e.Title, podcast.Name,
            e.FeedDuration?.TotalSeconds, e.PublishedAt)).ToArray();
    }

    /// <summary>Starts a known radio station or episode through ordinary playback readiness.</summary>
    /// <param name="kind">Radio or podcast playback kind.</param>
    /// <param name="id">Opaque source identity.</param>
    /// <returns>Whether this request still owns a successfully prepared player.</returns>
    private async Task<bool> PlayMobileMediaAsync(string kind, string id)
    {
        if (_playbackShuttingDown) return false;
        try
        {
            Audio.IAudioPlayer? player;
            if (kind == "radio" && _mobileMedia.Resolve<RadioStationRecord>(id) is { } station)
            {
                _ = _radioBrowserService.RegisterClickAsync(station.StationUuid);
                player = await StartReadyPlaybackAsync(station.StreamUrl, station);
            }
            else if (kind == "podcasts" && _mobileMedia.Resolve<MobileRemotePodcastEpisode>(id) is { } item)
            {
                var downloaded = PodcastDownloadService.GetDownloadedPath(item.Podcast, item.Episode);
                if (downloaded is not null) PodcastDownloadService.MarkUsed(item.Podcast, item.Episode);
                player = await StartReadyPlaybackAsync(downloaded ?? item.Episode.AudioUrl,
                    podcastPlayback: new PodcastPlayback(item.Podcast, item.Episode));
            }
            else return false;
            return player is not null && ReferenceEquals(_player, player);
        }
        catch { return false; }
    }
}
