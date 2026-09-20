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
/// Transport controls, position slider, waveform, lyrics, and now-playing artwork.
/// </summary>
public partial class MainWindow : Window
{
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
        _karaokeWindow?.UpdatePosition(_player.Position);
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
            RefreshWindowsMediaMetadata();
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
                _lyricLines.Add(new LyricLineViewModel(line.Text, line.Time, line.Words));
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
        if (_lyricLines.Count == 0)
            return;

        var nextIndex = LyricLineSelector.FindActiveIndex(
            _lyricLines,
            line => line.Time,
            position);
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

    /// <summary>Opens or closes the fullscreen karaoke view for synchronized lyrics.</summary>
    /// <param name="sender">The karaoke action.</param>
    /// <param name="e">Click details.</param>
    private void KaraokeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_karaokeWindow is { } open)
        {
            open.Close();
            return;
        }

        if (_lyricLines.Count == 0 || _lyricLines.All(line => line.Time is null))
        {
            ShowLyricsStatus(LocalizationManager.Current.KaraokeRequiresSyncedLyrics);
            return;
        }

        var window = new KaraokeWindow(
            [.. _lyricLines.Select(line => new KaraokeWindow.KaraokeLine(line.Text, line.Time) { Words = line.Words })],
            LyricsBackgroundImage.Source);
        window.SetTrack(NowPlayingTitleBlock.Text, NowPlayingArtistBlock.Text);
        window.Closed += (_, _) => _karaokeWindow = null;
        _karaokeWindow = window;
        if (_player is not null)
            window.UpdatePosition(_player.Position);
        window.Show(this);
    }

    private void ShowLyricsStatus(string text)
    {
        LyricsStatusTextBlock.Text = text;
        LyricsStatusTextBlock.IsVisible = true;
    }

    private void ClearLyrics()
    {
        _karaokeWindow?.Close();
        _karaokeWindow = null;
        LyricsListBox.SelectedItem = null;
        _lyricLines.Clear();
        _activeLyricIndex = -1;
    }

    private void CancelLyricsLoad()
    {
        _lyricsCts?.Cancel();
    }
}
