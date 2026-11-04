using Orynivo.Mcp;
using Orynivo.Library;
using Orynivo.Remote;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
using System.Security.Cryptography;

namespace Orynivo;

public partial class MainWindow
{
    private readonly MobileRemoteArtworkCache _mobileArtworkCache = new();

    /// <summary>Captures mobile playback details from the active source rather than a retained queue entry.</summary>
    /// <returns>Path-free metadata and the current opaque artwork revision.</returns>
    private MobileRemotePlaybackInfo GetMobilePlaybackInfo()
    {
        _mobileArtworkCache.Update($"{_playbackStarts.Current}\n{_currentFilePath}", NowPlayingArtworkImage.Source);
        var radio = _currentRadioStation;
        var podcast = _currentPodcastPlayback;
        return new MobileRemotePlaybackInfo(radio is not null ? "radio" : podcast is not null ? "podcasts" : "music",
            radio is null && podcast is null ? _currentAlbumTitle : null,
            radio?.Name ?? podcast?.Podcast.Author,
            radio is not null ? RadioNowPlayingDescription.Text : podcast?.Podcast.Genre,
            _player is Audio.BrowserAudioPlayer browser ? browser.OutputFormat : _player is not null ? FileInfoTextBlock.Text : null,
            podcast?.Episode.PublishedAt, _player?.CanSeek == true, _mobileArtworkCache.Key);
    }

    /// <summary>Returns a shared bounded cover, station logo or podcast image for current playback.</summary>
    /// <returns>The current JPEG thumbnail, or null for missing or obsolete artwork.</returns>
    private async Task<RemoteArtwork?> ResolveMobileRemoteArtworkAsync()
    {
        var key = GetMobilePlaybackInfo().ArtworkKey;
        var path = _currentFilePath;
        var radio = _currentRadioStation;
        var radioPath = _currentRadioArtworkPath;
        var podcast = _currentPodcastPlayback;
        var result = await _mobileArtworkCache.GetAsync(async () =>
        {
            try
            {
                byte[]? bytes = null;
                // Clone the published image before yielding so later replacement/disposal
                // cannot affect the private copy encoded by the worker.
                if (NowPlayingArtworkImage.Source is Bitmap desktopImage)
                {
                    var size = desktopImage.PixelSize;
                    var scale = Math.Min(1d, 1280d / Math.Max(size.Width, size.Height));
                    using var copy = desktopImage.CreateScaledBitmap(new PixelSize(
                        Math.Max(1, (int)Math.Round(size.Width * scale)),
                        Math.Max(1, (int)Math.Round(size.Height * scale))));
                    bytes = await Task.Run(() =>
                    {
                        using var stream = new System.IO.MemoryStream();
                        copy.Save(stream, PngBitmapEncoderOptions.Default);
                        return stream.ToArray();
                    });
                }
                var fallback = bytes;
                {
                    string? location;
                    if (radio is not null || podcast is not null)
                        location = radio is not null ? radioPath ?? radio.Favicon : podcast?.Podcast.ArtworkUrl;
                    else if (_orynivoTracksByUrl.TryGetValue(path, out var remote))
                        location = remote.OrynivoServer is { } server && remote.Id is { } trackId
                            ? Streaming.OrynivoServerClient.GetTrackArtworkUrl(server, trackId)
                            : remote.ArtworkPath ?? remote.ThumbnailPath;
                    else if (_plexTracksByUrl.TryGetValue(path, out var plex))
                        location = plex.ArtworkPath ?? plex.ThumbnailPath;
                    else
                        location = string.IsNullOrWhiteSpace(path) ? null : await Task.Run(() =>
                        {
                            using var db = AudioDatabase.OpenDefault();
                            var artwork = db.GetArtworkPathsByTrackPath(path);
                            return artwork?.OriginalPath ?? artwork?.Thumb320Path ?? artwork?.Thumb96Path;
                        });
                    var image = await Task.Run(() => TryReadArtworkBytesAsync(location, CancellationToken.None));
                    bytes = image?.Data ?? fallback;
                }
                if (bytes is not { Length: > 0 }) return null;
                return await Task.Run(() =>
                {
                    using var source = SKBitmap.Decode(bytes);
                    if (source is null) return null;
                    const int maximumEdge = 1280;
                    var scale = Math.Min(1d, maximumEdge / (double)Math.Max(source.Width, source.Height));
                    var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)),
                        Math.Max(1, (int)Math.Round(source.Height * scale)));
                    using var resized = scale < 1d
                        ? source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : source.Copy();
                    using var encoded = resized?.Encode(SKEncodedImageFormat.Jpeg, 94);
                    var data = encoded?.ToArray();
                    return data is { Length: > 0 }
                        ? new RemoteArtwork(data, "image/jpeg", Convert.ToHexString(SHA256.HashData(data))) : null;
                });
            }
            catch { return null; }
        });
        return GetMobilePlaybackInfo().ArtworkKey == key ? result : null;
    }
}
