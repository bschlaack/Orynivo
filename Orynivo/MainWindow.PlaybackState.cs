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
/// Playback history, now-playing identity, queue navigation, shuffle, and fade transitions.
/// </summary>
public partial class MainWindow : Window
{
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

        if (BuildLastFmTrack() is { } lastFmTrack)
            _lastFmScrobbler.SetNowPlaying(lastFmTrack, DateTimeOffset.Now);
    }

    /// <summary>
    /// Builds the Last.fm metadata for the audible item. Last.fm requires a
    /// non-empty artist and title, so an untagged item yields
    /// <see langword="null"/> instead of a rejected request.
    /// </summary>
    /// <returns>The track metadata, or <see langword="null"/> without a title and artist.</returns>
    private Scrobbling.LastFmTrack? BuildLastFmTrack()
    {
        var title = NowPlayingTitleBlock.Text;
        var artist = NowPlayingArtistBlock.Text;
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist))
            return null;

        return new Scrobbling.LastFmTrack(
            artist,
            title,
            _currentAlbumTitle,
            _currentPlaybackDuration > TimeSpan.Zero
                ? (int)_currentPlaybackDuration.TotalSeconds
                : null);
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
        _lastFmScrobbler.Complete(
            TimeSpan.FromSeconds(positionSeconds ?? _player?.Position.TotalSeconds ?? 0),
            _currentPlaybackDuration);

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
        if (PodcastEpisodesDataGrid.ItemsSource is not IEnumerable<PodcastEpisodeViewModel> rows ||
            _activePodcast is not { } podcast)
        {
            return;
        }

        var row = rows.FirstOrDefault(item =>
            string.Equals(item.Episode.EpisodeKey, episodeKey, StringComparison.Ordinal));
        if (row is null)
            return;

        var replacement = CreatePodcastEpisodeRow(
            podcast,
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
}
