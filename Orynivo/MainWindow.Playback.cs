using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Playback engine (start/stop, gapless sessions, and shuffle), transport UI and
/// waveform, lyrics, now-playing metadata, and playback history for
/// <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Wiedergabe
    // ------------------------------------------------------------------

    private void ConfigureWindowsMediaTransport()
    {
        _windowsMediaTransport = WindowsMediaTransportService.TryCreate();
        if (_windowsMediaTransport is null)
            return;

        _windowsMediaTransport.PlayRequested += () =>
            Dispatcher.UIThread.Post(async () => await ResumeOrStartPlaybackAsync());
        _windowsMediaTransport.PauseRequested += () =>
            Dispatcher.UIThread.Post(PausePlayback);
        _windowsMediaTransport.PreviousRequested += () =>
            Dispatcher.UIThread.Post(async () => await PlayPreviousAsync());
        _windowsMediaTransport.NextRequested += () =>
            Dispatcher.UIThread.Post(async () => await PlayNextAsync());
        _windowsMediaTransport.StopRequested += () =>
            Dispatcher.UIThread.Post(StopPlayback);
        _windowsMediaTransport.PositionChangeRequested += position =>
            Dispatcher.UIThread.Post(async () => await SeekFromSystemAsync(position));
        RefreshQueueNavigationButtons();
    }

    private async void PlayButton_OnClick(object? sender, RoutedEventArgs e) =>
        await TogglePlaybackAsync();

    private async Task TogglePlaybackAsync()
    {
        if (_player is not null)
        {
            if (_player.IsPaused)
            {
                _player.Resume();
                SetPlayPauseIcon(isPlaying: true);
                _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
            }
            else
            {
                _player.Pause();
                SetPlayPauseIcon(isPlaying: false);
                _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Paused);
            }
            return;
        }

        await ResumeOrStartPlaybackAsync();
    }

    private async Task ResumeOrStartPlaybackAsync()
    {
        if (_player is not null)
        {
            if (_player.IsPaused)
            {
                _player.Resume();
                SetPlayPauseIcon(isPlaying: true);
                _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            StatusTextBlock.Text = LocalizationManager.Current.SelectTrackFirst;
            return;
        }
        try { await StartPlaybackAsync(_currentFilePath, _currentRadioStation, _currentPodcastPlayback); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private void PausePlayback()
    {
        if (_player is null || _player.IsPaused)
            return;
        _player.Pause();
        SetPlayPauseIcon(isPlaying: false);
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Paused);
    }

    private async Task SeekFromSystemAsync(TimeSpan position)
    {
        if (_player?.CanSeek != true)
            return;
        try
        {
            await _player.SeekAsync(position);
            RefreshTransport();
            _windowsMediaTransport?.UpdateTimeline(
                _player.Position,
                _player.Duration,
                force: true);
        }
        catch
        {
            // A rejected system seek request must not interrupt playback.
        }
    }

    private async Task StartPlaybackAsync(
        string filePath,
        RadioStationRecord? radioStation = null,
        PodcastPlayback? podcastPlayback = null,
        TimeSpan initialPosition = default)
    {
        if (filePath.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedPath = await ResolveRemoteMcpTrackAsync(filePath);
            if (string.IsNullOrWhiteSpace(resolvedPath))
                throw new InvalidOperationException(LocalizationManager.Current.PlaybackStopped);
            filePath = resolvedPath;
        }
        await StopPlaybackAsync();
        _currentFilePath = filePath;
        _currentRadioStation = radioStation;
        _currentPodcastPlayback = podcastPlayback;
        if (radioStation is null && podcastPlayback is null)
            _playedQueuePaths.Add(filePath);
        _playbackCts     = new CancellationTokenSource();

        var playbackTrack = ResolveGaplessPlaybackItem(filePath);
        var ext = Path.GetExtension(playbackTrack.PlaybackPath);
        IAudioPlayer player;
        AudioFileInfo info;
        var gaplessItems = BuildGaplessPlaybackItems(
            filePath,
            radioStation is null && podcastPlayback is null);

        if (_settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio)
        {
            if (!SteinbergAsioStream.IsBackendAvailable(_settings.OutputBackend))
            {
                StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
                return;
            }
            if (string.IsNullOrWhiteSpace(_settings.SelectedDriverName))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectAsioDevice;
                return;
            }
            if (!_settings.AlwaysConvertDsdToPcm &&
                ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DsfAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
            else if (!_settings.AlwaysConvertDsdToPcm &&
                     IsRemoteOrynivoDsdCandidate(filePath))
                (player, info) = await CreateRemoteOrynivoDsdOrPcmPlayerAsync(filePath, playbackTrack);
            else if (!_settings.AlwaysConvertDsdToPcm &&
                     ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                (player, info) = await DffAudioPlayer.CreateAsync(
                    filePath,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _playbackCts.Token);
            else
                (player, info) = await FfmpegAudioPlayer.CreateAsync(
                    gaplessItems,
                    _settings.OutputBackend,
                    _settings.SelectedDriverName,
                    _settings.EqualizerEnabled,
                    _settings.EqualizerProfile,
                    _playbackCts.Token);
        }
        else if (_settings.OutputBackend == OutputBackend.AirPlay)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedAirPlayDeviceId))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectAirPlayDevice;
                return;
            }
            (player, info) = await CreateAirPlayPlayerAsync(playbackTrack, _playbackCts.Token);
        }
        else if (_settings.OutputBackend == OutputBackend.Wasapi)
        {
            if (string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
            {
                StatusTextBlock.Text = LocalizationManager.Current.SelectWasapiDevice;
                return;
            }
#if !WINDOWS
            if (OperatingSystem.IsLinux() &&
                _settings.DsdOverPcmEnabled &&
                (ext.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                 ext.Equals(".dff", StringComparison.OrdinalIgnoreCase) ||
                 IsRemoteOrynivoDsdCandidate(filePath)))
            {
                if (Uri.TryCreate(filePath, UriKind.Absolute, out var dopUri) &&
                    dopUri.Scheme is "http" or "https")
                {
                    if (IsRemoteOrynivoDffCandidate(filePath))
                        (player, info) = await DffDopAudioPlayer.CreateRemoteAsync(
                            filePath,
                            _settings.SelectedWasapiDeviceId,
                            _playbackCts.Token);
                    else
                        (player, info) = await RemoteDsfDopAudioPlayer.CreateAsync(
                            filePath,
                            _settings.SelectedWasapiDeviceId,
                            _playbackCts.Token);
                }
                else if (ext.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                    (player, info) = await DffDopAudioPlayer.CreateLocalAsync(
                        playbackTrack.PlaybackPath,
                        _settings.SelectedWasapiDeviceId,
                        _playbackCts.Token);
                else
                {
                    (player, info) = await DsfDopAudioPlayer.CreateAsync(
                        playbackTrack.PlaybackPath,
                        _settings.SelectedWasapiDeviceId,
                        _playbackCts.Token);
                }
            }
            else
#endif
            (player, info) = await WasapiAudioPlayer.CreateAsync(
                gaplessItems,
                _settings.SelectedWasapiDeviceId,
                _settings.EqualizerEnabled,
                _settings.EqualizerProfile,
                _playbackCts.Token);
        }
        else
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.NotImplemented, _settings.OutputBackend);
            return;
        }

        _player        = player;
        ApplyCrossfeedSettings(_player);
        _audioDeviceExplicitlyReleased = false;
        ClearReleasedOutputResumeState();
        UpdateOutputDeviceLockButton();
        if (player is IGaplessAudioPlayer gaplessPlayer)
            gaplessPlayer.TrackChanged += GaplessPlayer_OnTrackChanged;
        if (initialPosition > TimeSpan.Zero && player.CanSeek)
        {
            try { await player.SeekAsync(initialPosition); }
            catch { /* seek failure must not prevent playback */ }
        }
        UpdateNowPlayingRowHighlights();
        _player.Volume = _settings.OutputBackend == OutputBackend.Wasapi &&
                         _endpointVolumeSynchronizer is not null
            ? 1.0f
            : (float)VolumeSlider.Value;
        _player.ReplayGainFactor = GetReplayGainFactor(filePath);
        _currentPlaybackDuration = player.Duration;
        if (podcastPlayback is not null &&
            podcastPlayback.Podcast.Id > 0 &&
            player.CanSeek)
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var progress = db.GetPodcastEpisodeProgress(
                    podcastPlayback.Podcast.Id,
                    podcastPlayback.Episode.EpisodeKey);
                if (progress is { IsCompleted: false, PositionSeconds: > 5 } &&
                    progress.PositionSeconds < Math.Max(0, player.Duration.TotalSeconds - 10))
                {
                    await player.SeekAsync(TimeSpan.FromSeconds(progress.PositionSeconds));
                }
            }
            catch
            {
                // A corrupt or unavailable resume point must not prevent playback.
            }
        }
        _lastPodcastProgressSave = DateTimeOffset.UtcNow;
        PlayButton.IsEnabled   = false;
        PlayButton.IsEnabled   = true;
        SetPlayPauseIcon(isPlaying: true);
        RefreshQueueNavigationButtons();
        PositionSlider.IsEnabled = player.CanSeek;
        DurationTextBlock.Text = FormatTime(player.Duration);
        _ = LoadTransportWaveformAsync(filePath, player.Duration);
        _transportTimer.Start();
        StartMusicBrainzBackgroundEnrichment();

        // Now-playing anzeigen
        var queueMetadata = GetPlaylistMetadata(filePath);
        var filename = podcastPlayback?.Episode.Title ??
                       radioStation?.Name ??
                       queueMetadata?.DisplayTitle ??
                       Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text  = filename;
        NowPlayingArtistBlock.Text = podcastPlayback?.Podcast.Name ??
                                     (radioStation is null
                                         ? SelectedDriverTextBlock.Text
                                         : LocalizationManager.Current.InternetRadio);
        ClearNowPlayingAlbum();
        var usesNativeDsd = player is DsfAudioPlayer or DffAudioPlayer or RemoteDsfAudioPlayer or RemoteDffAudioPlayer
#if !WINDOWS
                            || player is DsfDopAudioPlayer { UsesNativeDsd: true }
                            || player is RemoteDsfDopAudioPlayer { UsesNativeDsd: true }
                            || player is DffDopAudioPlayer { UsesNativeDsd: true }
#endif
            ;
#if !WINDOWS
        var usesDop = player is DsfDopAudioPlayer { UsesNativeDsd: false }
                      or RemoteDsfDopAudioPlayer { UsesNativeDsd: false }
                      or DffDopAudioPlayer { UsesNativeDsd: false };
#else
        const bool usesDop = false;
#endif
        FileInfoTextBlock.Text = usesDop
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {LocalizationManager.Current.DopOutput} ({info.OutputSampleRate:N0} Hz)"
            : usesNativeDsd
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {LocalizationManager.Current.NativeDsdOutput}"
            : info.IsDsd
                ? $"{info.ContainerName.ToUpperInvariant()}  ·  {LocalizationManager.Current.DsdToPcmOutput}  ·  {info.OutputSampleRate:N0} Hz"
                : info.SourceSampleRate != info.OutputSampleRate
                    ? $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz → {info.OutputSampleRate:N0} Hz  ·  {info.Channels} ch"
                    : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";
        if (radioStation is null && podcastPlayback is null)
        {
            _currentNowPlayingProvider = _localNowPlayingProvider;
            _currentOrynivoTrackRow = null;
            var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
            var isOrynivoTrack = _orynivoTracksByUrl.TryGetValue(filePath, out var orynivoTrack);
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                var artist = db.GetArtistByTrackPath(filePath);
                var trackInfo = db.GetTrackIdAndFavorite(filePath);
                var navigationIds = db.GetTrackNavigationIds(filePath);
                _currentTrackId = trackInfo?.Id;
                _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
                _currentArtistId = artist?.Id;
                _currentArtistName = artist?.Artist;
                _currentAlbumId = navigationIds.AlbumId;
                NowPlayingArtistButton.IsEnabled = artist is not null;
                LyricsButton.IsEnabled = track is not null;
                ArtistInfoButton.IsEnabled = artist is not null;
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (track is not null)
                {
                    NowPlayingTitleBlock.Text = track.Title ?? filename;
                    NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                    SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
                }
            }
            catch
            {
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = null;
                ClearNowPlayingAlbum();
                NowPlayingArtistButton.IsEnabled = false;
                _currentTrackIsFavorite = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            if (isPlexTrack && plexTrack is not null)
            {
                NowPlayingTitleBlock.Text = plexTrack.Title ?? filename;
                NowPlayingArtistBlock.Text = plexTrack.Artist ?? string.Empty;
                NowPlayingArtworkImage.Source = null;
                LyricsBackgroundImage.Source = null;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = plexTrack.Artist;
                SetNowPlayingAlbum(plexTrack.Album, null, canNavigate: false);
                _currentTrackIsFavorite = false;
                NowPlayingArtistButton.IsEnabled = false;
                LyricsButton.IsEnabled = false;
                ArtistInfoButton.IsEnabled = false;
            }
            else if (isOrynivoTrack && orynivoTrack is not null)
            {
                NowPlayingTitleBlock.Text = orynivoTrack.Title ?? filename;
                NowPlayingArtistBlock.Text = orynivoTrack.Artist ?? string.Empty;
                // Show the list thumbnail immediately if already hydrated; the provider
                // then loads full artwork below so it appears even when the row is not.
                NowPlayingArtworkImage.Source = orynivoTrack.Thumbnail ?? orynivoTrack.Artwork;
                LyricsBackgroundImage.Source = orynivoTrack.Artwork ?? orynivoTrack.Thumbnail;
                _currentTrackId = null;
                _currentArtistId = null;
                _currentArtistName = orynivoTrack.Artist;
                SetNowPlayingAlbum(
                    orynivoTrack.Album,
                    orynivoTrack.AlbumId,
                    orynivoTrack.OrynivoServer is not null && orynivoTrack.AlbumId is not null);
                _currentTrackIsFavorite = false;
                _currentOrynivoTrackRow = orynivoTrack;
                if (orynivoTrack.OrynivoServer is { } trackServer)
                {
                    _currentNowPlayingProvider = CreateOrynivoNowPlayingProvider(trackServer);
                    if (orynivoTrack.Id is long favoriteTrackId)
                        _currentTrackIsFavorite = IsOrynivoFavorite(trackServer, "Track", favoriteTrackId);
                }
                // The now-playing artist button navigates within the remote library
                // using the row's server and artist ID (see NowPlayingArtistButton_OnClick).
                NowPlayingArtistButton.IsEnabled = orynivoTrack.ArtistId is not null
                    && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
                LyricsButton.IsEnabled = !string.IsNullOrWhiteSpace(orynivoTrack.Title)
                    && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
                ArtistInfoButton.IsEnabled = orynivoTrack.ArtistId is not null
                    && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
                ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
                if (LyricsButton.IsEnabled)
                    _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }
            else
            {
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
            }

            if (!isPlexTrack)
                _ = LoadNowPlayingArtworkAsync(
                    filePath,
                    _currentNowPlayingProvider ?? _localNowPlayingProvider,
                    BuildNowPlayingTrackContext(filePath));
        }
        else if (podcastPlayback is not null)
        {
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = true;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowPodcastInfo);
            _ = LoadPodcastArtworkAsync(
                podcastPlayback.Podcast.ArtworkUrl,
                _playbackCts?.Token ?? CancellationToken.None);
        }
        else if (radioStation is not null)
        {
            ShowRadioNowPlaying(radioStation, info);
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            NowPlayingArtistButton.IsEnabled = false;
            _currentTrackIsFavorite = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
            ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
            StartRadioMetadataMonitor(radioStation);
        }
        UpdateNowPlayingFavoriteButton();
        UpdateReplayGainBadge(usesNativeDsd);
        RefreshWindowsMediaMetadata();
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
        _windowsMediaTransport?.UpdateTimeline(
            player.Position,
            player.Duration,
            force: true);

        var outputName = _settings.OutputBackend switch
        {
            OutputBackend.Asio or OutputBackend.CwAsio => _settings.SelectedDriverName,
            OutputBackend.AirPlay => _settings.SelectedAirPlayDeviceName,
            _ => _settings.SelectedWasapiDeviceName
        };
        StatusTextBlock.Text = info.IsDsd && !usesNativeDsd
            ? string.Format(
                LocalizationManager.Current.PlaybackThroughWithDsdConversion,
                outputName,
                info.OutputSampleRate)
            : string.Format(LocalizationManager.Current.PlaybackThrough, outputName);

        try
        {
            using var db = AudioDatabase.OpenDefault();
            if (radioStation is not null)
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    null,
                    null,
                    mediaType: "radio",
                    title: radioStation.Name,
                    subtitle: LocalizationManager.Current.InternetRadio,
                    externalId: radioStation.StationUuid);
            }
            else if (podcastPlayback is not null)
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    null,
                    player.Duration.TotalSeconds > 0
                        ? player.Duration.TotalSeconds
                        : podcastPlayback.Episode.FeedDuration?.TotalSeconds,
                    mediaType: "podcast",
                    title: podcastPlayback.Episode.Title,
                    subtitle: podcastPlayback.Podcast.Name,
                    album: podcastPlayback.Podcast.Name,
                    externalId: podcastPlayback.Episode.EpisodeKey);
            }
            else
            {
                _currentPlayHistoryId = db.RecordPlaybackStart(
                    filePath,
                    db.GetTrackIdByPath(filePath),
                    player.Duration.TotalSeconds > 0 ? player.Duration.TotalSeconds : null,
                    title: NowPlayingTitleBlock.Text,
                    subtitle: NowPlayingArtistBlock.Text,
                    album: _currentAlbumTitle,
                    externalId: ResolveNowPlayingExternalId(filePath),
                    genre: ResolveNowPlayingGenre(filePath));
            }
        }
        catch
        {
            _currentPlayHistoryId = null;
        }

        try
        {
            await player.WaitForCompletionAsync();
        }
        catch (OperationCanceledException) when (!ReferenceEquals(_player, player))
        {
            return;
        }

        if (_player == player)
        {
            RecordPlaybackEnd(completed: true);
            SavePodcastProgress(completed: true);
            if (radioStation is not null || podcastPlayback is not null || !await TryPlayNextAsync())
            {
                StopPlayback();
                StatusTextBlock.Text = LocalizationManager.Current.PlaybackFinished;
            }
        }
    }

    private IReadOnlyList<GaplessPlaybackItem> BuildGaplessPlaybackItems(
        string currentFilePath,
        bool allowQueuedTracks)
    {
        if (!allowQueuedTracks ||
            _shuffleEnabled ||
            _queueIndex < 0 ||
            _queueIndex >= _queue.Count ||
            !string.Equals(
                _queue[_queueIndex].FilePath,
                currentFilePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return [ResolveGaplessPlaybackItem(currentFilePath)];
        }

        var items = new List<GaplessPlaybackItem>();
        for (var index = _queueIndex; index < _queue.Count; index++)
        {
            var path = _queue[index].FilePath;
            if (!_settings.AlwaysConvertDsdToPcm &&
                _settings.OutputBackend is OutputBackend.Asio or OutputBackend.CwAsio &&
                Path.GetExtension(path) is string extension &&
                (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase) ||
                 extension.Equals(".dff", StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }

            items.Add(ResolveGaplessPlaybackItem(path));
        }

        return items.Count == 0
            ? [ResolveGaplessPlaybackItem(currentFilePath)]
            : items;
    }

    /// <summary>
    /// Builds pre-known stream characteristics for a remote server track so the
    /// player can skip the FFmpeg probe. Returns <see langword="null"/> for DSD
    /// sources (which take the native remote players) and when the server did not
    /// report a usable sample rate, so those tracks probe as before.
    /// </summary>
    /// <param name="row">Cached remote track row carrying server metadata.</param>
    /// <returns>The pre-known audio info, or <see langword="null"/> to probe.</returns>
    private static KnownAudioInfo? BuildRemotePcmKnownInfo(ContentRow row)
    {
        if (row.SampleRateHz is not > 0)
            return null;

        var format = row.Format?.Trim();
        var sourceExtension = Path.GetExtension(row.SourcePath);
        var fileNameExtension = Path.GetExtension(row.FileName);
        var isDsd =
            string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dff", StringComparison.OrdinalIgnoreCase);
        if (isDsd)
            return null;

        return new KnownAudioInfo(
            row.SampleRateHz.Value,
            row.ChannelCount ?? 2,
            string.IsNullOrWhiteSpace(format) ? "pcm" : format.ToLowerInvariant(),
            IsDsd: false,
            format?.ToLowerInvariant());
    }

    private GaplessPlaybackItem ResolveGaplessPlaybackItem(string path)
    {
        if (_plexTracksByUrl.TryGetValue(path, out var plexTrack))
        {
            return new GaplessPlaybackItem(
                path,
                GetPcmOutputGainFactor(),
                SourcePaths: plexTrack.PlexPartUrls,
                KnownDuration: plexTrack.KnownDuration);
        }

        if (_orynivoTracksByUrl.TryGetValue(path, out var orynivoTrack))
            return new GaplessPlaybackItem(
                path,
                GetPcmOutputGainFactor(),
                KnownDuration: orynivoTrack.KnownDuration,
                KnownInfo: BuildRemotePcmKnownInfo(orynivoTrack));

        if (!CueSheetParser.IsVirtualPath(path))
            return new GaplessPlaybackItem(path, GetReplayGainFactor(path));

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            if (track is not null)
            {
                return new GaplessPlaybackItem(
                    path,
                    GetReplayGainFactor(path),
                    track.SourcePath,
                    track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
                    track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null);
            }
        }
        catch
        {
        }

        return new GaplessPlaybackItem(path, GetReplayGainFactor(path));
    }

    private async Task<(IAudioPlayer Player, AudioFileInfo Info)> CreateRemoteOrynivoDsdOrPcmPlayerAsync(
        string filePath,
        GaplessPlaybackItem playbackTrack)
    {
        var driverName = _settings.SelectedDriverName ?? string.Empty;
        try
        {
            var (dsfPlayer, dsfInfo) = await RemoteDsfAudioPlayer.CreateAsync(
                filePath,
                _settings.OutputBackend,
                driverName,
                _playbackCts?.Token ?? CancellationToken.None);
            return (dsfPlayer, dsfInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SeekDiagnostics.Log(
                "remote-dsf-player",
                $"native-skip reason={ex.GetType().Name} message={ex.Message}");
        }

        try
        {
            var (dffPlayer, dffInfo) = await RemoteDffAudioPlayer.CreateAsync(
                filePath,
                _settings.OutputBackend,
                driverName,
                _playbackCts?.Token ?? CancellationToken.None);
            return (dffPlayer, dffInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            SeekDiagnostics.Log(
                "remote-dff-player",
                $"native-fallback reason={ex.GetType().Name} message={ex.Message}");
            return await FfmpegAudioPlayer.CreateAsync(
                [playbackTrack],
                _settings.OutputBackend,
                driverName,
                _settings.EqualizerEnabled,
                _settings.EqualizerProfile,
                _playbackCts?.Token ?? CancellationToken.None);
        }
    }

    /// <summary>
    /// Determines whether a remote Orynivo Server stream should be routed through
    /// the native DSD players. When the cached server metadata identifies a
    /// concrete non-DSD format it is treated as authoritative so PCM tracks skip
    /// the native DSF/DFF probes and go straight to the gapless FFmpeg PCM path;
    /// only tracks with no usable format metadata fall back to the conservative
    /// stream-URL check.
    /// </summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns>
    /// <see langword="true"/> when the track may be native DSD; otherwise
    /// <see langword="false"/>.
    /// </returns>
    private bool IsRemoteOrynivoDsdCandidate(string path)
    {
        if (!_orynivoTracksByUrl.TryGetValue(path, out var row))
            return IsConfiguredOrynivoStreamUrl(path);

        var format = row.Format?.Trim();
        var sourceExtension = Path.GetExtension(row.SourcePath);
        var fileNameExtension = Path.GetExtension(row.FileName);

        if (string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourceExtension, ".dff", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dsf", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileNameExtension, ".dff", StringComparison.OrdinalIgnoreCase))
            return true;

        // Any concrete server-supplied format/extension identifies the track as
        // PCM here, so it must not probe the native DSD players. Each of those
        // probes reads remote header byte ranges, adding two wasted HTTP
        // round-trips before the FFmpeg fallback — the main reason remote
        // playback took several seconds to start on ASIO/cwASIO. Routing PCM
        // tracks through the normal FFmpeg branch also restores gapless playback
        // for them, because that branch uses the full gapless item list instead
        // of a single item. Only genuinely unknown tracks stay conservative.
        var hasKnownFormat =
            !string.IsNullOrWhiteSpace(format) ||
            !string.IsNullOrWhiteSpace(sourceExtension) ||
            !string.IsNullOrWhiteSpace(fileNameExtension);
        return !hasKnownFormat && IsConfiguredOrynivoStreamUrl(path);
    }

    /// <summary>
    /// Determines whether an Orynivo Server stream is specifically a DSF
    /// candidate for Linux DoP playback.
    /// </summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns><see langword="true"/> for known DSF or metadata-less Orynivo streams.</returns>
    private bool IsRemoteOrynivoDsfCandidate(string path)
    {
        if (!_orynivoTracksByUrl.TryGetValue(path, out var row))
            return IsConfiguredOrynivoStreamUrl(path);

        var format = row.Format?.Trim();
        return string.Equals(format, "DSF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.SourcePath), ".dsf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.FileName), ".dsf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Determines whether a remote Orynivo Server stream is a known DFF/DSDIFF track.</summary>
    /// <param name="path">Remote stream URL of the track.</param>
    /// <returns><see langword="true"/> when cached metadata identifies DFF/DSDIFF.</returns>
    private bool IsRemoteOrynivoDffCandidate(string path)
    {
        if (!_orynivoTracksByUrl.TryGetValue(path, out var row))
            return false;

        var format = row.Format?.Trim();
        return string.Equals(format, "DFF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(format, "DSDIFF", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.SourcePath), ".dff", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(row.FileName), ".dff", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsConfiguredOrynivoStreamUrl(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var baseUri))
                continue;

            if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var expectedPrefix = string.IsNullOrEmpty(basePath)
                ? "/api/stream/"
                : $"{basePath}/api/stream/";
            if (uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Resolves a history entry to its remote Orynivo Server track target.</summary>
    /// <param name="entry">The playback-history entry.</param>
    /// <param name="server">Resolved server settings.</param>
    /// <param name="trackId">Resolved server-side track identifier.</param>
    /// <returns><see langword="true"/> when the entry identifies a configured remote track.</returns>
    private bool TryGetOrynivoHistoryTarget(
        DailyHistoryEntry entry,
        out OrynivoServerSettings server,
        out long trackId)
    {
        if (TryParseOrynivoHistoryExternalId(entry.ExternalId, out var serverId, out trackId))
        {
            var matchingServer = (_settings.OrynivoServers ?? [])
                .FirstOrDefault(item => string.Equals(item.Id, serverId, StringComparison.Ordinal));
            if (matchingServer is not null)
            {
                server = matchingServer;
                return true;
            }
        }

        return TryGetOrynivoStreamUrlTarget(entry.Path, out server, out trackId);
    }

    /// <summary>Parses an Orynivo Server track identifier stored in playback history.</summary>
    /// <param name="externalId">Stored history external identifier.</param>
    /// <param name="serverId">Parsed server identifier.</param>
    /// <param name="trackId">Parsed server-side track identifier.</param>
    /// <returns><see langword="true"/> when the identifier is valid.</returns>
    private static bool TryParseOrynivoHistoryExternalId(
        string? externalId,
        out string serverId,
        out long trackId)
    {
        serverId = string.Empty;
        trackId = 0;
        if (string.IsNullOrWhiteSpace(externalId))
            return false;

        var parts = externalId.Split(':');
        if (parts.Length != 4 ||
            !string.Equals(parts[0], "orynivo", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parts[2], "track", StringComparison.OrdinalIgnoreCase) ||
            !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
        {
            return false;
        }

        serverId = parts[1];
        return !string.IsNullOrWhiteSpace(serverId);
    }

    /// <summary>Resolves a configured Orynivo Server and track ID from a stream URL.</summary>
    /// <param name="path">Potential remote stream URL.</param>
    /// <param name="server">Resolved server settings.</param>
    /// <param name="trackId">Resolved server-side track identifier.</param>
    /// <returns><see langword="true"/> when the URL points at a configured server stream.</returns>
    private bool TryGetOrynivoStreamUrlTarget(
        string path,
        out OrynivoServerSettings server,
        out long trackId)
    {
        server = null!;
        trackId = 0;
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
            return false;

        foreach (var candidate in _settings.OrynivoServers ?? [])
        {
            if (!Uri.TryCreate(candidate.BaseUrl, UriKind.Absolute, out var baseUri))
                continue;

            if (!string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var expectedPrefix = string.IsNullOrEmpty(basePath)
                ? "/api/stream/"
                : $"{basePath}/api/stream/";
            if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var idText = uri.AbsolutePath[expectedPrefix.Length..].Trim('/');
            var slashIndex = idText.IndexOf('/');
            if (slashIndex >= 0)
                idText = idText[..slashIndex];
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out trackId))
                continue;

            server = candidate;
            return true;
        }

        return false;
    }

    private void GaplessPlayer_OnTrackChanged(object? sender, GaplessTrackChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _player))
                return;

            RecordPlaybackEnd(completed: true, _currentPlaybackDuration.TotalSeconds);
            _currentFilePath = e.FilePath;
            _currentPlaybackDuration = e.Info.Duration;
            _playedQueuePaths.Add(e.FilePath);
            UpdateNowPlayingRowHighlights();

            if (_queueIndex + 1 < _queue.Count &&
                string.Equals(
                    _queue[_queueIndex + 1].FilePath,
                    e.FilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _queueIndex++;
            }
            else
            {
                var matchingIndex = Enumerable.Range(0, _queue.Count)
                    .Where(index => string.Equals(
                        _queue[index].FilePath,
                        e.FilePath,
                        StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(-1)
                    .First();
                if (matchingIndex >= 0)
                    _queueIndex = matchingIndex;
            }

            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            PositionSlider.IsEnabled = _player?.CanSeek == true;
            DurationTextBlock.Text = FormatTime(e.Info.Duration);
            UpdateGaplessNowPlaying(e.FilePath, e.Info);
            StartLocalPlaybackHistory(e.FilePath);
        });
    }

    private void UpdateGaplessNowPlaying(string filePath, AudioFileInfo info)
    {
        var metadata = GetPlaylistMetadata(filePath);
        var filename = metadata?.DisplayTitle ?? Path.GetFileNameWithoutExtension(filePath);
        NowPlayingTitleBlock.Text = filename;
        NowPlayingArtistBlock.Text = metadata?.Artist ?? SelectedDriverTextBlock.Text;
        ClearNowPlayingAlbum();
        FileInfoTextBlock.Text = info.IsDsd
            ? $"{info.ContainerName.ToUpperInvariant()}  ·  {LocalizationManager.Current.DsdToPcmOutput}  ·  {info.OutputSampleRate:N0} Hz"
            : info.SourceSampleRate != info.OutputSampleRate
                ? $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz → {info.OutputSampleRate:N0} Hz  ·  {info.Channels} ch"
                : $"{info.CodecName.ToUpperInvariant()}  ·  {info.SourceSampleRate:N0} Hz  ·  {info.Channels} ch";

        _currentNowPlayingProvider = _localNowPlayingProvider;
        _currentOrynivoTrackRow = null;
        var isPlexTrack = _plexTracksByUrl.TryGetValue(filePath, out var plexTrack);
        var isOrynivoTrack = _orynivoTracksByUrl.TryGetValue(filePath, out var orynivoTrack);
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            var artist = db.GetArtistByTrackPath(filePath);
            var trackInfo = db.GetTrackIdAndFavorite(filePath);
            var navigationIds = db.GetTrackNavigationIds(filePath);
            _currentTrackId = trackInfo?.Id;
            _currentTrackIsFavorite = trackInfo?.IsFavorite ?? false;
            _currentArtistId = artist?.Id;
            _currentArtistName = artist?.Artist;
            _currentAlbumId = navigationIds.AlbumId;
            NowPlayingArtistButton.IsEnabled = artist is not null;
            LyricsButton.IsEnabled = track is not null;
            ArtistInfoButton.IsEnabled = artist is not null;
            if (track is not null)
            {
                NowPlayingTitleBlock.Text = track.Title ?? filename;
                NowPlayingArtistBlock.Text = track.Artist ?? string.Empty;
                SetNowPlayingAlbum(track.Album, navigationIds.AlbumId, navigationIds.AlbumId is not null);
            }
        }
        catch
        {
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = null;
            ClearNowPlayingAlbum();
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }

        if (isPlexTrack && plexTrack is not null)
        {
            NowPlayingTitleBlock.Text = plexTrack.Title ?? filename;
            NowPlayingArtistBlock.Text = plexTrack.Artist ?? string.Empty;
            NowPlayingArtworkImage.Source = null;
            LyricsBackgroundImage.Source = null;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = plexTrack.Artist;
            SetNowPlayingAlbum(plexTrack.Album, null, canNavigate: false);
            _currentTrackIsFavorite = false;
            NowPlayingArtistButton.IsEnabled = false;
            LyricsButton.IsEnabled = false;
            ArtistInfoButton.IsEnabled = false;
        }
        else if (isOrynivoTrack && orynivoTrack is not null)
        {
            NowPlayingTitleBlock.Text = orynivoTrack.Title ?? filename;
            NowPlayingArtistBlock.Text = orynivoTrack.Artist ?? string.Empty;
            NowPlayingArtworkImage.Source = orynivoTrack.Thumbnail ?? orynivoTrack.Artwork;
            LyricsBackgroundImage.Source = orynivoTrack.Artwork ?? orynivoTrack.Thumbnail;
            _currentTrackId = null;
            _currentArtistId = null;
            _currentArtistName = orynivoTrack.Artist;
            SetNowPlayingAlbum(
                orynivoTrack.Album,
                orynivoTrack.AlbumId,
                orynivoTrack.OrynivoServer is not null && orynivoTrack.AlbumId is not null);
            _currentTrackIsFavorite = false;
            _currentOrynivoTrackRow = orynivoTrack;
            if (orynivoTrack.OrynivoServer is { } trackServer)
            {
                _currentNowPlayingProvider = CreateOrynivoNowPlayingProvider(trackServer);
                if (orynivoTrack.Id is long favoriteTrackId)
                    _currentTrackIsFavorite = IsOrynivoFavorite(trackServer, "Track", favoriteTrackId);
            }
            // The now-playing artist button navigates within the remote library
            // using the row's server and artist ID (see NowPlayingArtistButton_OnClick).
            NowPlayingArtistButton.IsEnabled = orynivoTrack.ArtistId is not null
                && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
            LyricsButton.IsEnabled = !string.IsNullOrWhiteSpace(orynivoTrack.Title)
                && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
            ArtistInfoButton.IsEnabled = orynivoTrack.ArtistId is not null
                && !string.IsNullOrWhiteSpace(orynivoTrack.Artist);
            if (LyricsButton.IsEnabled)
                _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }
        else
        {
            _ = LoadLyricsForTrackAsync(filePath, forceRefresh: false);
        }

        if (!isPlexTrack)
            _ = LoadNowPlayingArtworkAsync(
                filePath,
                _currentNowPlayingProvider ?? _localNowPlayingProvider,
                BuildNowPlayingTrackContext(filePath));

        UpdateNowPlayingFavoriteButton();
        UpdateReplayGainBadge(nativeDsdOutput: false);
        RefreshWindowsMediaMetadata();
        _windowsMediaTransport?.SetPlaybackStatus(MediaPlaybackStatus.Playing);
        if (_player is not null)
        {
            _windowsMediaTransport?.UpdateTimeline(
                _player.Position,
                _player.Duration,
                force: true);
        }
    }

    private void RefreshWindowsMediaMetadata()
    {
        if (_windowsMediaTransport is null)
            return;

        var title = NowPlayingTitleBlock.Text ?? string.Empty;
        var artist = NowPlayingArtistBlock.Text ?? string.Empty;
        var album = string.Empty;
        string? artworkPath = null;
        Uri? artworkUri = null;

        if (_currentPodcastPlayback is { } podcastPlayback)
        {
            album = podcastPlayback.Podcast.Name;
            if (Uri.TryCreate(
                    podcastPlayback.Podcast.ArtworkUrl,
                    UriKind.Absolute,
                    out var podcastArtworkUri))
            {
                artworkUri = podcastArtworkUri;
            }
        }
        else if (_currentRadioStation is { } radioStation)
        {
            album = radioStation.Name;
            artworkPath = _currentRadioArtworkPath;
            if (string.IsNullOrWhiteSpace(artworkPath) &&
                Uri.TryCreate(radioStation.Favicon, UriKind.Absolute, out var radioArtworkUri))
            {
                artworkUri = radioArtworkUri;
            }
        }
        else if (_plexTracksByUrl.TryGetValue(_currentFilePath, out var plexTrack))
        {
            album = plexTrack.Album ?? string.Empty;
        }
        else if (_orynivoTracksByUrl.TryGetValue(_currentFilePath, out var orynivoTrack))
        {
            album = orynivoTrack.Album ?? string.Empty;
            if (orynivoTrack.OrynivoServer is { } server &&
                orynivoTrack.Id is long trackId &&
                Uri.TryCreate(
                    OrynivoServerClient.GetTrackArtworkUrl(server, trackId, 320),
                    UriKind.Absolute,
                    out var orynivoArtworkUri))
            {
                artworkUri = orynivoArtworkUri;
            }
        }
        else
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(_currentFilePath);
                var artwork = db.GetArtworkPathsByTrackPath(_currentFilePath);
                album = track?.Album ?? string.Empty;
                artworkPath = artwork?.OriginalPath ??
                              artwork?.Thumb320Path ??
                              artwork?.Thumb96Path;
            }
            catch
            {
                // Metadata is optional and must never affect audio playback.
            }
        }

        _ = _windowsMediaTransport.UpdateMetadataAsync(new WindowsMediaMetadata(
            title,
            artist,
            album,
            artworkPath,
            artworkUri));
    }
}
