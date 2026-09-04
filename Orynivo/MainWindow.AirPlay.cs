using Orynivo.Audio;
using Orynivo.Library;
using Orynivo.Localization;
using Avalonia.Threading;
using SkiaSharp;
using System.Security.Cryptography;

namespace Orynivo;

public partial class MainWindow
{
    private const int MaximumAirPlayArtworkBytes = 8 * 1024 * 1024;
    /// <summary>
    /// Resolves the selected receiver again when possible and creates the
    /// native AirPlay 2 playback session with the profile's last address as a fallback.
    /// </summary>
    /// <param name="item">Track to decode and stream.</param>
    /// <param name="cancellationToken">Cancels discovery and playback startup.</param>
    /// <returns>The started player and decoded stream information.</returns>
    private async Task<(AirPlayAudioPlayer Player, AudioFileInfo Info)> CreateAirPlayPlayerAsync(
        GaplessPlaybackItem item,
        CancellationToken cancellationToken)
    {
        var host = _settings.SelectedAirPlayHost;
        var port = _settings.SelectedAirPlayPort;
        try
        {
            var devices = await AirPlayDeviceDiscovery.DiscoverAsync(
                cancellationToken: cancellationToken);
            var device = devices.FirstOrDefault(candidate => string.Equals(
                candidate.Id,
                _settings.SelectedAirPlayDeviceId,
                StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                host = device.Host;
                port = device.Port;
                _settings.SelectedAirPlayDeviceName = device.Name;
                _settings.SelectedAirPlayHost = device.Host;
                _settings.SelectedAirPlayPort = device.Port;
                var profile = _settings.OutputProfiles.FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    _settings.SelectedOutputProfileName,
                    StringComparison.OrdinalIgnoreCase));
                if (profile is not null)
                {
                    profile.SelectedAirPlayDeviceName = device.Name;
                    profile.SelectedAirPlayHost = device.Host;
                    profile.SelectedAirPlayPort = device.Port;
                }
                _ = Task.Run(() => _settingsStore.Save(_settings));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A temporarily blocked multicast lookup may use the last resolved endpoint.
        }

        if (string.IsNullOrWhiteSpace(host) || port <= 0)
            throw new InvalidOperationException(LocalizationManager.Current.NoAirPlayDevices);
        var metadata = await ResolveAirPlayTrackMetadataAsync(item.FilePath, cancellationToken);
        return await AirPlayAudioPlayer.CreateAsync(
            item,
            host,
            port,
            _settings.SelectedAirPlayDeviceName,
            _settings.SelectedAirPlayDeviceId,
            (float)VolumeSlider.Value,
            GetReplayGainFactor(item.FilePath),
            metadata,
            HandleAirPlayRemoteCommand,
            cancellationToken);
    }

    /// <summary>Routes an authenticated receiver command through Orynivo's shared transport.</summary>
    /// <param name="command">Receiver-originated AirPlay transport command.</param>
    private void HandleAirPlayRemoteCommand(AirPlayRemoteCommand command)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            if (_player is not AirPlayAudioPlayer)
                return;

            switch (command)
            {
                case AirPlayRemoteCommand.Play:
                    await ResumeAirPlayFromReceiverAsync();
                    break;
                case AirPlayRemoteCommand.Pause:
                    PausePlayback();
                    break;
                case AirPlayRemoteCommand.Next:
                    await PlayNextAsync();
                    break;
                case AirPlayRemoteCommand.Previous:
                    await PlayPreviousAsync();
                    break;
            }
        });
    }

    /// <summary>
    /// Recreates a receiver-paused AirPlay stream at its audible position before resuming it.
    /// </summary>
    private async Task ResumeAirPlayFromReceiverAsync()
    {
        if (_player is not AirPlayAudioPlayer player || !player.IsPaused)
            return;

        try
        {
            var position = player.Position;
            await player.SeekAsync(position);
            if (!ReferenceEquals(_player, player))
                return;
            await ResumeOrStartPlaybackAsync();
        }
        catch (OperationCanceledException)
        {
            // A newer playback or transport request replaced this resume.
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_player, player))
                StatusTextBlock.Text = ex.Message;
        }
    }

    /// <summary>Resolves bounded receiver metadata for a local, Plex, or Orynivo Server track.</summary>
    /// <param name="filePath">Playable track path or registered stream URL.</param>
    /// <param name="cancellationToken">Cancels artwork loading.</param>
    /// <returns>Text metadata and optional JPEG or PNG artwork.</returns>
    private async Task<AirPlayTrackMetadata> ResolveAirPlayTrackMetadataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var playlist = GetPlaylistMetadata(filePath);
        var title = playlist?.DisplayTitle;
        var artist = playlist?.Artist;
        var album = playlist?.Album;
        string? artworkPath = null;

        if (_orynivoTracksByUrl.TryGetValue(filePath, out var remoteRow))
            artworkPath = remoteRow.ArtworkPath ?? remoteRow.ThumbnailPath;
        else if (_plexTracksByUrl.TryGetValue(filePath, out var plexRow))
            artworkPath = plexRow.ArtworkPath ?? plexRow.ThumbnailPath;
        else
        {
            var local = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                var artwork = db.GetArtworkPathsByTrackPath(filePath);
                return (track, artwork);
            }, cancellationToken);
            title ??= local.track?.Title;
            artist ??= local.track?.Artist;
            album ??= local.track?.Album;
            artworkPath = local.artwork?.OriginalPath ??
                          local.artwork?.Thumb320Path ??
                          local.artwork?.Thumb96Path;
        }

        title = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(filePath)
            : title;
        var artworkBytes = await TryReadArtworkBytesAsync(artworkPath, cancellationToken);
        if (artworkBytes is { Data.Length: > MaximumAirPlayArtworkBytes } or
            { MimeType: not ("image/jpeg" or "image/png") })
            artworkBytes = null;
        return new AirPlayTrackMetadata(
            title,
            artist,
            album,
            artworkBytes?.Data,
            artworkBytes?.MimeType);
    }

    /// <summary>Creates a bounded JPEG thumbnail for the currently playing track.</summary>
    /// <returns>Remote-safe artwork, or <see langword="null"/> when no current image exists.</returns>
    private async Task<Mcp.RemoteArtwork?> ResolveMobileRemoteArtworkAsync()
    {
        var path = _currentFilePath;
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var metadata = await ResolveAirPlayTrackMetadataAsync(path, CancellationToken.None);
        if (metadata.Artwork is not { Length: > 0 } bytes)
            return null;
        return await Task.Run(() =>
        {
            using var source = SKBitmap.Decode(bytes);
            if (source is null)
                return null;
            const int maximumEdge = 640;
            var scale = Math.Min(1d, maximumEdge / (double)Math.Max(source.Width, source.Height));
            var info = new SKImageInfo(
                Math.Max(1, (int)Math.Round(source.Width * scale)),
                Math.Max(1, (int)Math.Round(source.Height * scale)));
            using var resized = scale < 1d
                ? source.Resize(info, SKFilterQuality.Medium)
                : source.Copy();
            using var encoded = resized?.Encode(SKEncodedImageFormat.Jpeg, 85);
            var data = encoded?.ToArray();
            return data is { Length: > 0 }
                ? new Mcp.RemoteArtwork(data, "image/jpeg", Convert.ToHexString(SHA256.HashData(data)))
                : null;
        });
    }
}
