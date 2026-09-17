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

    private static readonly Color DefaultTransportAccent = Color.Parse("#20D9E8");

    /// <summary>
    /// Updates the cover-derived transport accent brush (progress fill, slider
    /// thumb, play button) from the current now-playing artwork. Falls back to the
    /// app accent when there is no artwork or extraction fails.
    /// </summary>
    /// <param name="source">The current now-playing artwork image, or <see langword="null"/>.</param>
    private void UpdateTransportAccentFromArtwork(IImage? source)
    {
        var fallback = GetThemeAccentColor();
        var color = source is Bitmap bitmap
            ? ArtworkAccentColor.ExtractAccentColor(bitmap) ?? fallback
            : fallback;

        if (this.TryFindResource("AppTransportAccentBrush", out var resource) &&
            resource is SolidColorBrush brush)
        {
            brush.Color = color;
        }
        if (this.TryFindResource("AppTransportAccentTextBrush", out var textResource) &&
            textResource is SolidColorBrush textBrush)
        {
            textBrush.Color = ArtworkAccentColor.GetReadableTextColor(color);
        }
    }

    /// <summary>
    /// Resolves the current theme accent colour used when no artwork accent is available.
    /// </summary>
    /// <returns>The theme accent colour, or the built-in default when the resource is unavailable.</returns>
    private Color GetThemeAccentColor()
    {
        return this.TryFindResource("AppAccentBrush", out var resource) &&
            resource is SolidColorBrush brush
            ? brush.Color
            : DefaultTransportAccent;
    }

    /// <summary>
    /// Resolves the genre to store with a play-history entry for non-local tracks
    /// (remote Orynivo Server and Plex), so genre statistics include them. Local
    /// tracks return <see langword="null"/> because their genre is resolved through
    /// the <c>play_history.track_id</c> join.
    /// </summary>
    /// <param name="filePath">Playing file path or stream URL.</param>
    /// <returns>The captured genre, or <see langword="null"/> when none applies.</returns>
    private string? ResolveNowPlayingGenre(string filePath)
    {
        if (_currentOrynivoTrackRow?.Genre is { } orynivoGenre && !string.IsNullOrWhiteSpace(orynivoGenre))
            return orynivoGenre;
        if (_plexTracksByUrl.TryGetValue(filePath, out var plexRow) && !string.IsNullOrWhiteSpace(plexRow.Genre))
            return plexRow.Genre;
        return null;
    }

    /// <summary>
    /// Resolves a stable source identifier to store with playback history for
    /// remote tracks whose local history row has no database track ID.
    /// </summary>
    /// <param name="filePath">Playing file path or stream URL.</param>
    /// <returns>A source-specific identifier, or <see langword="null"/> for local tracks.</returns>
    private string? ResolveNowPlayingExternalId(string filePath)
    {
        if (_currentOrynivoTrackRow is { OrynivoServer: { } server, Id: long trackId } &&
            string.Equals(_currentOrynivoTrackRow.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return BuildOrynivoHistoryExternalId(server, trackId);
        }

        if (_orynivoTracksByUrl.TryGetValue(filePath, out var row) &&
            row is { OrynivoServer: { } rowServer, Id: long rowTrackId })
        {
            return BuildOrynivoHistoryExternalId(rowServer, rowTrackId);
        }

        if (_plexTracksByUrl.TryGetValue(filePath, out var plexRow) &&
            plexRow is { PlexServerId: { } plexServerId, ExternalId: { } plexRatingKey } &&
            !string.IsNullOrWhiteSpace(plexServerId) && !string.IsNullOrWhiteSpace(plexRatingKey))
        {
            return BuildPlexHistoryExternalId(
                plexServerId, plexRatingKey, plexRow.PlexAlbumRatingKey, plexRow.PlexArtistRatingKey);
        }

        return null;
    }

    /// <summary>Builds the playback-history external ID for a remote Orynivo Server track.</summary>
    /// <param name="server">The remote server owning the track.</param>
    /// <param name="trackId">Server-side track identifier.</param>
    /// <returns>A compact, parseable history identifier.</returns>
    private static string BuildOrynivoHistoryExternalId(OrynivoServerSettings server, long trackId) =>
        $"orynivo:{server.Id}:track:{trackId}";

    /// <summary>
    /// Builds a stable Plex playback-history external ID carrying the server, track,
    /// album, and artist rating keys so a Plex history entry stays resolvable to its
    /// in-library album and artist. Rating keys are numeric and contain no colons.
    /// </summary>
    /// <param name="serverId">Plex server identifier.</param>
    /// <param name="ratingKey">Plex track rating key.</param>
    /// <param name="albumRatingKey">Plex album (parent) rating key, or <see langword="null"/>.</param>
    /// <param name="artistRatingKey">Plex artist (grandparent) rating key, or <see langword="null"/>.</param>
    /// <returns>A compact, parseable history identifier of the form <c>plex:server:track:album:artist</c>.</returns>
    private static string BuildPlexHistoryExternalId(
        string serverId,
        string ratingKey,
        string? albumRatingKey,
        string? artistRatingKey) =>
        $"plex:{serverId}:{ratingKey}:{albumRatingKey ?? string.Empty}:{artistRatingKey ?? string.Empty}";

    private void StartLocalPlaybackHistory(string filePath)
    {
        try
        {
            using var db = AudioDatabase.OpenDefault();
            _currentPlayHistoryId = db.RecordPlaybackStart(
                filePath,
                db.GetTrackIdByPath(filePath),
                _currentPlaybackDuration.TotalSeconds > 0
                    ? _currentPlaybackDuration.TotalSeconds
                    : null,
                title: NowPlayingTitleBlock.Text,
                subtitle: NowPlayingArtistBlock.Text,
                album: _currentAlbumTitle,
                externalId: ResolveNowPlayingExternalId(filePath),
                genre: ResolveNowPlayingGenre(filePath));
        }
        catch
        {
            _currentPlayHistoryId = null;
        }
    }

    private async void PreviousButton_OnClick(object? sender, RoutedEventArgs e) =>
        await PlayPreviousAsync();

    private async Task PlayPreviousAsync()
    {
        if (!TryMoveToPreviousQueueIndex())
            return;

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private async void NextButton_OnClick(object? sender, RoutedEventArgs e) =>
        await PlayNextAsync();

    private async Task PlayNextAsync()
    {
        if (!TryMoveToNextQueueIndex())
            return;

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private void StopPlayback()
    {
        _audioDeviceExplicitlyReleased = false;
        ClearReleasedOutputResumeState();
        var player = StopPlaybackCore();
        player?.Dispose();
    }

    private async Task StopPlaybackAsync(bool waitForCompleteDisposal = false)
    {
        var player = StopPlaybackCore();
        if (player is not null)
        {
            var disposalTask = Task.Run(() =>
            {
                try
                {
                    player.Dispose();
                }
                catch (Exception ex)
                {
                    CrashLogger.Log(ex, "Background audio-player disposal");
                }
            });
            if (waitForCompleteDisposal)
                await disposalTask;
            else
                await Task.WhenAny(disposalTask, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }

    private IAudioPlayer? StopPlaybackCore()
    {
        RecordPlaybackEnd(completed: false);
        SavePodcastProgress(completed: false);
        CancelAndDispose(ref _radioMetadataCts);
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;
        var player = _player;
        _player = null;
        _currentPlaybackDuration = TimeSpan.Zero;
        UpdateNowPlayingRowHighlights();

        PlayButton.IsEnabled   = true;
        SetPlayPauseIcon(isPlaying: false);
        RefreshQueueNavigationButtons();
        _isSeekingWithSlider = false;
        PositionSlider.IsEnabled = false;
        ClearTransportWaveform();
        _transportTimer.Stop();
        CancelLyricsLoad();
        CancelArtistProfileLoad();
        ClearLyrics();

        NowPlayingTitleBlock.Text  = "";
        NowPlayingArtistBlock.Text = "";
        ClearNowPlayingAlbum();
        FileInfoTextBlock.Text     = "";
        ReplayGainBadgeBorder.IsVisible = false;
        NowPlayingArtworkImage.Source = null;
        LyricsBackgroundImage.Source = null;
        _currentTrackId = null;
        _currentArtistId = null;
        _currentArtistName = null;
        _currentTrackIsFavorite = false;
        _currentRadioStation = null;
        _currentRadioArtworkPath = null;
        _currentPodcastPlayback = null;
        _windowsMediaTransport?.Clear();
        LyricsButton.IsEnabled = false;
        ArtistInfoButton.IsEnabled = false;
        ToolTip.SetTip(ArtistInfoButton, LocalizationManager.Current.ShowArtistInfo);
        PodcastInfoView.IsVisible = false;
        ArtistInfoView.IsVisible = false;
        ClearRadioNowPlaying();
        UpdateNowPlayingFavoriteButton();
        UpdateOutputDeviceLockButton();
        return player;
    }

    private static void CancelAndDispose(ref CancellationTokenSource? source)
    {
        var current = Interlocked.Exchange(ref source, null);
        if (current is null)
            return;
        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        current.Dispose();
    }

    private void RecordPlaybackEnd(bool completed, double? positionSeconds = null)
    {
        if (_currentPlayHistoryId is not long historyId)
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.RecordPlaybackEnd(
                historyId,
                positionSeconds ?? _player?.Position.TotalSeconds ?? 0,
                completed);
        }
        catch { }
        finally
        {
            _currentPlayHistoryId = null;
            InvalidateDashboardCatalogCache();
            _ = SyncProfileHistoryInBackgroundAsync();
        }
    }

    private void SavePodcastProgress(bool completed)
    {
        if (_currentPodcastPlayback is not { Podcast.Id: > 0 } playback ||
            _player is null)
            return;

        var duration = _player.Duration.TotalSeconds > 0
            ? _player.Duration.TotalSeconds
            : playback.Episode.FeedDuration?.TotalSeconds;
        var position = completed && duration is > 0
            ? duration.Value
            : _player.Position.TotalSeconds;
        var isCompleted = completed ||
                          (duration is > 0 && position / duration.Value >= 0.95);
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SavePodcastEpisodeProgress(
                playback.Podcast.Id,
                playback.Episode.EpisodeKey,
                position,
                duration,
                isCompleted);
            UpdateVisiblePodcastEpisodeProgress(
                playback.Episode.EpisodeKey,
                position,
                duration,
                isCompleted);
        }
        catch
        {
        }

        _lastPodcastProgressSave = DateTimeOffset.UtcNow;
        if (isCompleted)
            _currentPodcastPlayback = null;
    }

    private void UpdateVisiblePodcastEpisodeProgress(
        string episodeKey,
        double positionSeconds,
        double? durationSeconds,
        bool completed)
    {
        if (PodcastEpisodesDataGrid.ItemsSource is not IEnumerable<PodcastEpisodeViewModel> rows)
            return;
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.Episode.EpisodeKey, episodeKey, StringComparison.Ordinal));
        if (row is null)
            return;

        var replacement = CreatePodcastEpisodeRow(
            row.Episode,
            new Dictionary<string, PodcastEpisodeProgress>(StringComparer.Ordinal)
            {
                [episodeKey] = new(
                    episodeKey,
                    positionSeconds,
                    durationSeconds,
                    completed)
            });
        if (PodcastEpisodesDataGrid.ItemsSource is IList<PodcastEpisodeViewModel> list)
        {
            var index = list.IndexOf(row);
            if (index >= 0)
            {
                list[index] = replacement;
                { var _tmp = PodcastEpisodesDataGrid.ItemsSource; PodcastEpisodesDataGrid.ItemsSource = null; PodcastEpisodesDataGrid.ItemsSource = _tmp; };
                UpdateVisiblePodcastStatistics(list);
            }
        }
    }

    private void UpdateVisiblePodcastStatistics(IEnumerable<PodcastEpisodeViewModel> source)
    {
        var rows = source.ToList();
        var completed = rows.Count(row =>
            string.Equals(row.Status, LocalizationManager.Current.PodcastPlayed, StringComparison.Ordinal));
        var started = rows.Count(row =>
            string.Equals(row.Status, LocalizationManager.Current.PodcastInProgress, StringComparison.Ordinal));
        PodcastEpisodesStatistics.Text = string.Join(
            "  ·  ",
            string.Format(LocalizationManager.Current.PodcastEpisodeTotal, rows.Count),
            string.Format(LocalizationManager.Current.PodcastEpisodeUnheard, rows.Count - completed),
            string.Format(LocalizationManager.Current.PodcastEpisodeStarted, started));
    }

    private async Task<bool> TryPlayNextAsync()
    {
        if (!TryMoveToNextQueueIndex())
            return false;

        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(_queue[_queueIndex].FilePath); return true; }
        catch { return false; }
    }

    private void MaybeStartNonGaplessFadeTransition(TimeSpan visiblePosition)
    {
        if (_nonGaplessFadeTransitionInProgress ||
            _settings.NonGaplessCrossfadeSeconds <= 0 ||
            _currentRadioStation is not null ||
            _currentPodcastPlayback is not null ||
            _player is null ||
            _player.Duration <= TimeSpan.Zero ||
            IsContinuousGaplessQueueActive() ||
            !HasNextQueueItem())
        {
            return;
        }

        var fade = TimeSpan.FromSeconds(Math.Clamp(_settings.NonGaplessCrossfadeSeconds, 0.5, 10));
        if (_player.Duration <= fade + TimeSpan.FromSeconds(1))
            return;
        if (_player.Duration - visiblePosition > fade)
            return;

        _ = RunNonGaplessFadeTransitionAsync(fade);
    }

    private bool IsContinuousGaplessQueueActive() =>
        _player is IGaplessAudioPlayer &&
        !_shuffleEnabled &&
        _queueIndex >= 0 &&
        _queueIndex + 1 < _queue.Count &&
        _queueIndex < _queue.Count &&
        string.Equals(_queue[_queueIndex].FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase);

    private bool HasNextQueueItem()
    {
        if (_shuffleEnabled)
            return HasUnplayedShuffleCandidate();
        return _queueIndex == -1
            ? _queue.Count > 0
            : _queueIndex + 1 < _queue.Count;
    }

    private async Task RunNonGaplessFadeTransitionAsync(TimeSpan fade)
    {
        if (_nonGaplessFadeTransitionInProgress || _player is null)
            return;

        _nonGaplessFadeTransitionInProgress = true;
        var outgoing = _player;
        var outgoingVolume = outgoing.Volume;
        try
        {
            await FadePlayerVolumeAsync(outgoing, outgoingVolume, 0f, fade);
            if (!ReferenceEquals(_player, outgoing) || !TryMoveToNextQueueIndex())
                return;

            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();

            var nextPath = _queue[_queueIndex].FilePath;
            var playbackTask = StartPlaybackAsync(nextPath);
            _ = playbackTask.ContinueWith(task =>
            {
                if (task.Exception is { } exception)
                    CrashLogger.Log(exception.GetBaseException(), "Non-gapless fade playback");
            }, TaskContinuationOptions.OnlyOnFaulted);

            var incoming = await WaitForCurrentPlayerReplacementAsync(outgoing, TimeSpan.FromSeconds(3));
            if (incoming is null)
                return;
            var targetVolume = GetNormalPlayerVolume();
            incoming.Volume = 0f;
            await FadePlayerVolumeAsync(incoming, 0f, targetVolume, fade);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Non-gapless fade transition");
        }
        finally
        {
            if (ReferenceEquals(_player, outgoing))
                outgoing.Volume = outgoingVolume;
            _nonGaplessFadeTransitionInProgress = false;
        }
    }

    private async Task<IAudioPlayer?> WaitForCurrentPlayerReplacementAsync(
        IAudioPlayer outgoing,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_player is { } current && !ReferenceEquals(current, outgoing))
                return current;
            await Task.Delay(25);
        }
        return _player is { } player && !ReferenceEquals(player, outgoing) ? player : null;
    }

    private async Task FadePlayerVolumeAsync(
        IAudioPlayer player,
        float from,
        float to,
        TimeSpan duration)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / 50d));
        for (var step = 1; step <= steps; step++)
        {
            if (!ReferenceEquals(_player, player) && step > 1)
                return;
            var ratio = (float)step / steps;
            player.Volume = from + ((to - from) * ratio);
            await Task.Delay(TimeSpan.FromMilliseconds(duration.TotalMilliseconds / steps));
        }
        player.Volume = to;
    }

    private float GetNormalPlayerVolume() =>
        _settings.OutputBackend == OutputBackend.Wasapi && _endpointVolumeSynchronizer is not null
            ? 1.0f
            : (float)VolumeSlider.Value;

    private void RefreshQueueNavigationButtons()
    {
        EnsureInfiniteMixQueue();
        if (_shuffleEnabled)
        {
            PreviousButton.IsEnabled = _shuffleHistoryPosition > 0;
            NextButton.IsEnabled =
                _shuffleHistoryPosition + 1 < _shuffleHistory.Count ||
                HasUnplayedShuffleCandidate();
            _windowsMediaTransport?.SetNavigationCapabilities(
                PreviousButton.IsEnabled,
                NextButton.IsEnabled);
            return;
        }

        PreviousButton.IsEnabled = _queueIndex > 0 && _queueIndex < _queue.Count;
        NextButton.IsEnabled =
            (_queueIndex == -1 && _queue.Count > 0) ||
            (_queueIndex >= 0 && _queueIndex + 1 < _queue.Count);
        _windowsMediaTransport?.SetNavigationCapabilities(
            PreviousButton.IsEnabled,
            NextButton.IsEnabled);
    }

    private void ShuffleButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _shuffleEnabled = !_shuffleEnabled;
        ResetShuffleHistory();
        UpdateShuffleButton();
        RefreshQueueNavigationButtons();
    }

    private void ResetQueuePlaybackState()
    {
        _playedQueuePaths.Clear();
        ResetShuffleHistory();
    }

    private void ResetShuffleHistory()
    {
        _shuffleHistory.Clear();
        _shuffleHistoryPosition = -1;
        if (_queueIndex < 0 || _queueIndex >= _queue.Count)
            return;

        _shuffleHistory.Add(_queueIndex);
        _shuffleHistoryPosition = 0;
    }

    private bool TryMoveToPreviousQueueIndex()
    {
        if (!_shuffleEnabled)
        {
            if (_queueIndex <= 0 || _queueIndex >= _queue.Count)
                return false;
            _queueIndex--;
            return true;
        }

        if (_shuffleHistoryPosition <= 0)
            return false;
        _shuffleHistoryPosition--;
        _queueIndex = _shuffleHistory[_shuffleHistoryPosition];
        return true;
    }

    private bool TryMoveToNextQueueIndex()
    {
        if (!_shuffleEnabled)
        {
            if (_queueIndex == -1 && _queue.Count > 0)
            {
                _queueIndex = 0;
                return true;
            }
            if (_queueIndex < 0 || _queueIndex + 1 >= _queue.Count)
                return false;
            _queueIndex++;
            return true;
        }

        if (_shuffleHistoryPosition + 1 < _shuffleHistory.Count)
        {
            _shuffleHistoryPosition++;
            _queueIndex = _shuffleHistory[_shuffleHistoryPosition];
            return true;
        }

        var candidates = Enumerable.Range(0, _queue.Count)
            .Where(index =>
                index != _queueIndex &&
                !_playedQueuePaths.Contains(_queue[index].FilePath))
            .ToList();
        if (candidates.Count == 0)
            return false;

        _queueIndex = candidates[Random.Shared.Next(candidates.Count)];
        _shuffleHistory.Add(_queueIndex);
        _shuffleHistoryPosition = _shuffleHistory.Count - 1;
        return true;
    }

    private bool HasUnplayedShuffleCandidate() =>
        Enumerable.Range(0, _queue.Count).Any(index =>
            index != _queueIndex &&
            !_playedQueuePaths.Contains(_queue[index].FilePath));

    private void UpdateShuffleButton()
    {
        ShuffleButton.Background = new SolidColorBrush(
            _shuffleEnabled
                ? Color.FromRgb(0x6C, 0x63, 0xFF)
                : Color.FromRgb(0x25, 0x26, 0x40));
        ShuffleButton.Foreground = _shuffleEnabled
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xEE));
    }

    private void TransportControlsHeaderGrid_OnLayoutChanged(object? sender, RoutedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateResponsivePlaybackControls);

    private void TransportControlsHeaderGrid_OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        Dispatcher.UIThread.Post(UpdateResponsivePlaybackControls);

    private void UpdateResponsivePlaybackControls()
    {
        const double gap = 12;
        var centeredPlaybackLeft =
            (TransportControlsHeaderGrid.Bounds.Width - PlaybackControlsPanel.Bounds.Width) / 2;
        var requiredLeft = TransportActionPanel.Bounds.Width + gap;
        var shift = Math.Max(0, requiredLeft - centeredPlaybackLeft);
        var maximumShift = Math.Max(
            0,
            TransportControlsHeaderGrid.Bounds.Width -
            PlaybackControlsPanel.Bounds.Width -
            centeredPlaybackLeft);

        if (PlaybackControlsPanel.RenderTransform is Avalonia.Media.TranslateTransform translate)
            translate.X = Math.Min(shift, maximumShift);
    }

    private void SetPlayPauseIcon(bool isPlaying)
    {
        PlayPauseIcon.Data = Geometry.Parse(isPlaying
            ? "M 6 4 H 9 V 16 H 6 Z M 11 4 H 14 V 16 H 11 Z"
            : "M 8 4 L 17 10 L 8 16 Z");
    }

    // ------------------------------------------------------------------
    // Pause / Seek / Volume
    // ------------------------------------------------------------------

    private async void PositionSlider_OnPreviewMouseLeftButtonUp(object? sender, PointerReleasedEventArgs e) =>
        await CommitPositionSliderSeekAsync(e.Pointer);

    private void PositionSlider_OnPreviewMouseLeftButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        if (_player?.CanSeek != true || PositionSlider.Bounds.Width <= 0)
            return;

        _isSeekingWithSlider = true;
        _positionSliderSeekStartedAt = DateTimeOffset.UtcNow;
        e.Pointer.Capture(PositionSlider);
        PositionSlider.SetValueFromPoint(e.GetPosition(PositionSlider));
        e.Handled = true;
    }

    private async void PositionSlider_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isSeekingWithSlider || _player?.CanSeek != true)
        {
            return;
        }
        if (!e.GetCurrentPoint(PositionSlider).Properties.IsLeftButtonPressed)
        {
            await CommitPositionSliderSeekAsync(e.Pointer);
            return;
        }

        PositionSlider.SetValueFromPoint(e.GetPosition(PositionSlider));
        e.Handled = true;
    }

    private void PositionSlider_OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isSeekingWithSlider)
            return;

        _isSeekingWithSlider = false;
        RefreshTransport();
    }

    private void PositionSlider_OnValueChanged(object? sender, EventArgs e)
    {
        if (_isSeekingWithSlider)
            CurrentTimeTextBlock.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
    }

    private void RefreshTransport()
    {
        if (_player is null) return;
        if (_isSeekingWithSlider &&
            DateTimeOffset.UtcNow - _positionSliderSeekStartedAt > TimeSpan.FromSeconds(5))
        {
            _isSeekingWithSlider = false;
        }

        var visiblePosition = _pendingTransportSeekPosition ?? _player.Position;
        CurrentTimeTextBlock.Text = FormatTime(visiblePosition);
        DurationTextBlock.Text    = FormatTime(_player.Duration);
        PositionSlider.Maximum    = Math.Max(1, _player.Duration.TotalSeconds);
        if (!_isSeekingWithSlider)
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, visiblePosition.TotalSeconds);
        _windowsMediaTransport?.UpdateTimeline(visiblePosition, _player.Duration);
        if (_currentPodcastPlayback is not null &&
            DateTimeOffset.UtcNow - _lastPodcastProgressSave >= TimeSpan.FromSeconds(5))
        {
            SavePodcastProgress(completed: false);
        }
        UpdateActiveLyric(_player.Position);
        EnsureInfiniteMixQueue();
        MaybeStartNonGaplessFadeTransition(visiblePosition);
    }

    /// <summary>Commits the pending waveform-progress seek and leaves preview mode.</summary>
    /// <param name="pointer">Pointer that owns capture, or <see langword="null"/>.</param>
    private async Task CommitPositionSliderSeekAsync(IPointer? pointer)
    {
        if (!_isSeekingWithSlider)
            return;

        var target = TimeSpan.FromSeconds(PositionSlider.Value);
        var seekVersion = Interlocked.Increment(ref _transportSeekVersion);
        var stopwatch = Stopwatch.StartNew();
        _pendingTransportSeekPosition = target;
        _isSeekingWithSlider = false;
        pointer?.Capture(null);
        CurrentTimeTextBlock.Text = FormatTime(target);
        PositionSlider.Value = Math.Min(PositionSlider.Maximum, target.TotalSeconds);
        var durationForLog = _player?.Duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) ?? "none";
        SeekDiagnostics.Log(
            "transport-ui",
            $"seek-request version={seekVersion} target={target.TotalSeconds:F3}s duration={durationForLog}s canSeek={_player?.CanSeek} path={SeekDiagnostics.SanitizeUrl(_currentFilePath)}");
        try
        {
            if (seekVersion != Volatile.Read(ref _transportSeekVersion))
            {
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-skipped-stale version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
                return;
            }
            if (_player is not null && _player.CanSeek)
            {
                await _player.SeekAsync(target);
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-complete version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds} playerPosition={_player.Position.TotalSeconds:F3}s");
            }
            else
            {
                SeekDiagnostics.Log(
                    "transport-ui",
                    $"seek-skipped-unavailable version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
            }
        }
        catch (OperationCanceledException)
        {
            // Track changes and stop requests can cancel an in-flight seek.
            SeekDiagnostics.Log(
                "transport-ui",
                $"seek-canceled version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            SeekDiagnostics.Log(
                "transport-ui",
                $"seek-failed version={seekVersion} elapsedMs={stopwatch.ElapsedMilliseconds}",
                ex);
            CrashLogger.Log(ex, "Transport seek");
            StatusTextBlock.Text = ex.Message;
        }
        finally
        {
            if (seekVersion == Volatile.Read(ref _transportSeekVersion))
                _pendingTransportSeekPosition = null;
            RefreshTransport();
            if (_player is not null)
            {
                _windowsMediaTransport?.UpdateTimeline(
                    _player.Position,
                    _player.Duration,
                    force: true);
            }
        }
    }

    /// <summary>Loads compact waveform data for the current transport item.</summary>
    /// <param name="filePath">Playback path for the current item.</param>
    /// <param name="duration">Known playback duration.</param>
    private async Task LoadTransportWaveformAsync(string filePath, TimeSpan duration)
    {
        CancelAndDispose(ref _waveformCts);
        PositionSlider.SetWaveform(null);
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var source = new CancellationTokenSource();
        _waveformCts = source;
        try
        {
            var samples = await LoadWaveformPeaksAsync(filePath, duration, source.Token);
            if (!source.IsCancellationRequested &&
                string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase) &&
                samples.Count > 0)
            {
                PositionSlider.SetWaveform(samples);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Transport waveform analysis");
        }
    }

    /// <summary>Loads waveform peak data for a local or remote Orynivo track.</summary>
    /// <param name="filePath">Playback path for the current item.</param>
    /// <param name="duration">Known playback duration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Normalized waveform peaks, or an empty list when unavailable.</returns>
    private async Task<IReadOnlyList<float>> LoadWaveformPeaksAsync(
        string filePath,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        if (_orynivoTracksByUrl.TryGetValue(filePath, out var remoteTrack) &&
            remoteTrack.OrynivoServer is { } server &&
            remoteTrack.Id is long trackId)
        {
            var waveform = await _orynivoClient.GetTrackWaveformAsync(
                server,
                trackId,
                cancellationToken);
            if (waveform?.Peaks is { Length: > 0 } peaks)
                return peaks;

            var fallback = await WaveformCache.GetOrCreateStreamAsync(
                $"orynivo-server:{server.Id}:{trackId}",
                filePath,
                duration,
                900,
                cancellationToken);
            return fallback?.Peaks ?? [];
        }

        if (CueSheetParser.IsVirtualPath(filePath))
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                var track = db.GetByPath(filePath);
                if (track is { Duration: > 0 })
                {
                    var cueData = await WaveformCache.GetOrCreateAsync(
                        track.Path,
                        track.SourcePath,
                        TimeSpan.FromSeconds(track.Duration.Value),
                        900,
                        track.SegmentStart is double start ? TimeSpan.FromSeconds(start) : null,
                        track.SegmentEnd is double end ? TimeSpan.FromSeconds(end) : null,
                        cancellationToken);
                    return cueData?.Peaks ?? [];
                }
            }
            catch
            {
                return [];
            }
        }

        if (!File.Exists(filePath))
        {
            return [];
        }

        var data = await WaveformCache.GetOrCreateAsync(
            filePath,
            null,
            duration,
            900,
            cancellationToken: cancellationToken);
        return data?.Peaks ?? [];
    }

    /// <summary>Cancels pending waveform analysis and clears the transport waveform.</summary>
    private void ClearTransportWaveform()
    {
        CancelAndDispose(ref _waveformCts);
        PositionSlider.SetWaveform(null);
    }

    private void LyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ArtistInfoView.IsVisible = false;
        PodcastInfoView.IsVisible = false;
        LyricsView.IsVisible = !(LyricsView.IsVisible);
        UpdateBackButtonForDetailView();
        if (LyricsView.IsVisible &&
            _lyricLines.Count == 0 &&
            !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            _ = LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: false);
        }
    }

    private void CloseLyricsButton_OnClick(object? sender, RoutedEventArgs e)
        => CloseNowPlayingDetailViews();

    private async void RefreshLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
            return;
        await LoadLyricsForTrackAsync(_currentFilePath, forceRefresh: true);
    }

    private async void SearchLyricsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var filePath = _currentFilePath;
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        // Seed the search from the current track's metadata. Remote tracks have
        // no local database row, so read the title/artist from the remote row
        // instead of GetByPath (which fails on a stream URL and previously
        // suppressed the dialog entirely for remote tracks).
        var remoteRow = _currentOrynivoTrackRow;
        string? seedTitle;
        string? seedArtist;
        TrackRecord? localTrack = null;
        if (remoteRow is not null)
        {
            seedTitle = remoteRow.Title;
            seedArtist = remoteRow.Artist;
        }
        else
        {
            using (var db = AudioDatabase.OpenDefault())
                localTrack = db.GetByPath(filePath);
            if (localTrack is null)
                return;
            seedTitle = localTrack.Title;
            seedArtist = localTrack.Artist;
        }

        var dialog = new LyricsSearchWindow(seedTitle, seedArtist);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedResult is not { } selected)
            return;

        // Persist the chosen lyrics on the owning store: the remote server for
        // remote tracks, the local database otherwise.
        if (remoteRow is { OrynivoServer: { } server, Id: long trackId })
        {
            await _orynivoClient.UploadTrackLyricsAsync(
                server,
                trackId,
                selected.PlainLyrics,
                selected.SyncedLyrics);
        }
        else
        {
            using var db = AudioDatabase.OpenDefault();
            db.UpdateDownloadedLyrics(
                filePath,
                selected.PlainLyrics,
                selected.SyncedLyrics,
                "LRCLIB manual");
        }

        // Only reflect the change in the view if the same track is still shown.
        if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (localTrack is not null)
        {
            localTrack.DownloadedLyrics = selected.PlainLyrics;
            localTrack.SyncedLyrics = selected.SyncedLyrics;
            localTrack.LyricsSource = "LRCLIB manual";
            localTrack.LyricsFetchedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ApplyLyrics(localTrack);
        }
        else
        {
            ApplyLyricsContent(selected.PlainLyrics, selected.SyncedLyrics);
        }
    }

    /// <summary>Builds the now-playing track context for the active metadata provider.</summary>
    /// <param name="filePath">Local path or remote stream URL of the current track.</param>
    /// <returns>Context populated from the remote track row when remote, otherwise just the path.</returns>
    private NowPlayingTrackContext BuildNowPlayingTrackContext(string filePath)
    {
        if (_currentOrynivoTrackRow is { } row &&
            string.Equals(row.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            return new NowPlayingTrackContext(
                row.Id,
                filePath,
                row.Title,
                row.Artist,
                row.Album,
                row.KnownDuration?.TotalSeconds);
        }

        return new NowPlayingTrackContext(null, filePath, null, null, null, null);
    }

    /// <summary>
    /// Loads the transport cover and lyrics background for the current track through the
    /// active now-playing metadata provider, so local and remote tracks behave the same.
    /// </summary>
    /// <param name="filePath">Local path or remote stream URL of the current track.</param>
    /// <param name="provider">Provider for the current track's source.</param>
    /// <param name="context">Now-playing track context.</param>
    private async Task LoadNowPlayingArtworkAsync(
        string filePath,
        INowPlayingMetadataProvider provider,
        NowPlayingTrackContext context)
    {
        var token = _playbackCts?.Token ?? CancellationToken.None;
        try
        {
            var artwork = await provider.GetArtworkAsync(context, token);
            if (token.IsCancellationRequested ||
                !string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var thumbnailPath = artwork?.ThumbnailPath ?? artwork?.LargePath;
            var largePath = artwork?.LargePath ?? artwork?.ThumbnailPath;

            // Clear the cover only when the provider authoritatively returned no artwork
            // (local). For remote, keep any list thumbnail already shown if the fetch failed.
            if (thumbnailPath is not null || provider is LocalNowPlayingMetadataProvider)
                NowPlayingArtworkImage.Source = CreateArtworkImage(thumbnailPath, 96);
            if (largePath is not null || provider is LocalNowPlayingMetadataProvider)
                LyricsBackgroundImage.Source = CreateArtworkImage(largePath, 900);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task LoadLyricsForTrackAsync(string filePath, bool forceRefresh)
    {
        CancelLyricsLoad();
        var cts = new CancellationTokenSource();
        _lyricsCts = cts;
        _activeLyricIndex = -1;
        RefreshLyricsButton.IsEnabled = false;
        ShowLyricsStatus(LocalizationManager.Current.LyricsLoading);

        try
        {
            var provider = _currentNowPlayingProvider ?? _localNowPlayingProvider;
            var context = BuildNowPlayingTrackContext(filePath);

            var cached = await provider.GetCachedLyricsAsync(context, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (cached is null && provider is LocalNowPlayingMetadataProvider)
            {
                // A local track with no database row cannot be looked up at all.
                ClearLyrics();
                ShowLyricsStatus(LocalizationManager.Current.LyricsUnavailable);
                return;
            }

            var hasLocalLyrics = cached is { HasLyrics: true }
                && ApplyLyricsContent(cached.Plain, cached.Synced);
            if (cached is null || !cached.HasLyrics)
                ClearLyrics();

            var fetchedAt = cached?.FetchedAt is long timestamp
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : (DateTimeOffset?)null;
            var lookupExpired = fetchedAt is null ||
                fetchedAt < DateTimeOffset.UtcNow.AddDays(-30);
            var shouldDownload = forceRefresh ||
                (string.IsNullOrWhiteSpace(cached?.Synced) && lookupExpired);
            if (!shouldDownload)
            {
                if (!hasLocalLyrics)
                    ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
                return;
            }

            if (!hasLocalLyrics)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloading);

            var result = await provider.DownloadLyricsAsync(context, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            if (!string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
                return;

            if (result is not { HasLyrics: true } || !ApplyLyricsContent(result.Plain, result.Synced))
                ShowLyricsStatus(LocalizationManager.Current.LyricsNotFound);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (_lyricLines.Count == 0)
                ShowLyricsStatus(LocalizationManager.Current.LyricsDownloadFailed);
        }
        finally
        {
            if (_lyricsCts == cts)
            {
                RefreshLyricsButton.IsEnabled = true;
                _lyricsCts = null;
            }
            cts.Dispose();
        }
    }

    private bool ApplyLyrics(TrackRecord track)
        => ApplyLyricsContent(track.DownloadedLyrics ?? track.Lyrics, track.SyncedLyrics);

    /// <summary>Renders the lyrics view from plain and synchronised lyrics text.</summary>
    /// <param name="plainLyrics">Unsynchronised plain-text lyrics, or <see langword="null"/>.</param>
    /// <param name="syncedLyrics">LRC-formatted synchronised lyrics, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when at least one lyric line was rendered.</returns>
    private bool ApplyLyricsContent(string? plainLyrics, string? syncedLyrics)
    {
        ClearLyrics();
        var timedLines = LyricsService.ParseLrc(syncedLyrics);
        if (timedLines.Count > 0)
        {
            foreach (var line in timedLines)
                _lyricLines.Add(new LyricLineViewModel(line.Text, line.Time));
        }
        else if (!string.IsNullOrWhiteSpace(plainLyrics))
        {
            foreach (var line in plainLyrics.Replace("\r\n", "\n").Split('\n'))
                _lyricLines.Add(new LyricLineViewModel(line.Trim(), null));
        }

        var hasLyrics = _lyricLines.Count > 0;
        LyricsStatusTextBlock.IsVisible = !(hasLyrics);
        if (hasLyrics && _player is not null)
            UpdateActiveLyric(_player.Position);
        return hasLyrics;
    }

    private void UpdateActiveLyric(TimeSpan position)
    {
        if (_lyricLines.Count == 0 || _lyricLines[0].Time is null)
            return;

        var nextIndex = -1;
        var low = 0;
        var high = _lyricLines.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (_lyricLines[middle].Time <= position)
            {
                nextIndex = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (nextIndex == _activeLyricIndex)
            return;
        if (_activeLyricIndex >= 0 && _activeLyricIndex < _lyricLines.Count)
            _lyricLines[_activeLyricIndex].IsActive = false;
        _activeLyricIndex = nextIndex;
        if (_activeLyricIndex >= 0)
        {
            var activeLine = _lyricLines[_activeLyricIndex];
            activeLine.IsActive = true;
            LyricsListBox.SelectedItem = activeLine;
            LyricsListBox.ScrollIntoView(activeLine);
        }
        else
        {
            LyricsListBox.SelectedItem = null;
        }
    }

    private void ShowLyricsStatus(string text)
    {
        LyricsStatusTextBlock.Text = text;
        LyricsStatusTextBlock.IsVisible = true;
    }

    private void ClearLyrics()
    {
        LyricsListBox.SelectedItem = null;
        _lyricLines.Clear();
        _activeLyricIndex = -1;
    }

    private void CancelLyricsLoad()
    {
        _lyricsCts?.Cancel();
    }
}
