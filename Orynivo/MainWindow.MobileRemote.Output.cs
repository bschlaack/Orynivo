using Orynivo.Audio;
using Orynivo.Remote;
using Orynivo.Localization;

namespace Orynivo;

public partial class MainWindow
{
    private string? _browserAudioClientId;
    private BrowserOutputResume? _browserOutputResume;
    private OutputProfile? _browserOutputPickerProfile;
    private BrowserAudioEncoding _browserMusicEncoding = BrowserAudioEncoding.Find("mp3-192")!;

    /// <summary>Retains source context after the browser closes without retaining an audio device.</summary>
    private sealed record BrowserOutputResume(string Path, Library.RadioStationRecord? Radio,
        PodcastPlayback? Podcast, TimeSpan Position, bool WasPaused);

    /// <summary>Captures transient output ownership without source paths or session credentials.</summary>
    /// <returns>Browser output state, or null when physical output is selected.</returns>
    private BrowserAudioState? GetBrowserAudioState() => _browserAudioClientId is not { } client ? null
        : _player is BrowserAudioPlayer browser && browser.Snapshot().ClientId == client ? browser.Snapshot() with { FormatId = _browserMusicEncoding.Id }
        : new BrowserAudioState(client, null, 0, true, VolumeSlider.Value, 0, FormatId: _browserMusicEncoding.Id);

    /// <summary>Releases every previous device before handing the current source to a page.</summary>
    /// <param name="clientId">Random page identity; never persisted.</param>
    /// <returns>Whether the current selection and optional prepared player belong to this page.</returns>
    private async Task<bool> SelectBrowserOutputAsync(string clientId)
    {
        if (_playbackShuttingDown || !Guid.TryParseExact(clientId, "N", out _)) return false;
        var version = ++_outputProfileSelectionVersion;
        var path = _currentFilePath;
        var radio = _currentRadioStation;
        var podcast = _currentPodcastPlayback;
        var active = _player is not null;
        var position = _player?.Position ?? TimeSpan.Zero;
        var paused = _player is BrowserAudioPlayer currentBrowser ? currentBrowser.UserPaused : _player?.IsPaused == true;
        _browserAudioClientId = clientId;
        RefreshOutputPickerSelection();
        DisposeEndpointVolumeSynchronizationInBackground();
        var stopping = StopPlaybackAsync(waitForCompleteDisposal: true);
        var intent = _playbackStarts.Current;
        await stopping;
        bool OwnsOutput() => !_playbackShuttingDown && version == _outputProfileSelectionVersion && _browserAudioClientId == clientId;
        if (!OwnsOutput()) return false;
        // A newer Play or Stop owns the source, but does not undo this output
        // selection. Install its media session without reviving the old source.
        if (!_playbackStarts.IsCurrent(intent)) return true;
        UpdateOutputDeviceLockButton();
        if (!active || string.IsNullOrWhiteSpace(path)) return true;
        try
        {
            var preparing = StartReadyPlaybackAsync(path, radio, podcast, position, paused);
            var resumeIntent = _playbackStarts.Current;
            var player = await preparing;
            return OwnsOutput() && (!_playbackStarts.IsCurrent(resumeIntent) ||
                player is BrowserAudioPlayer && ReferenceEquals(_player, player));
        }
        catch { return false; }
    }

    /// <summary>Stops the owning browser's player and keeps native devices released.</summary>
    /// <param name="clientId">Page requesting release.</param>
    /// <returns>Completion of the current output's full cleanup.</returns>
    private async Task ReleaseBrowserOutputAsync(string clientId)
    {
        if (_browserAudioClientId != clientId) return;
        ++_outputProfileSelectionVersion;
        if (_player is BrowserAudioPlayer browser)
            _browserOutputResume = new(_currentFilePath, _currentRadioStation, _currentPodcastPlayback,
                browser.Position, browser.UserPaused);
        var stopping = StopPlaybackAsync(waitForCompleteDisposal: true);
        await stopping;
        UpdateOutputDeviceLockButton();
    }

    /// <summary>Shows transient remote output as the current choice without modifying saved profiles.</summary>
    private void RefreshOutputPickerSelection()
    {
        _outputPickerUpdating = true;
        try
        {
            var profiles = (_settings.OutputProfiles ?? []).ToList();
            _browserOutputPickerProfile = _browserAudioClientId is null ? null
                : new OutputProfile { Name = LocalizationManager.Current.BrowserAudioOutput };
            if (_browserOutputPickerProfile is not null) profiles.Insert(0, _browserOutputPickerProfile);
            OutputPickerComboBox.ItemsSource = profiles;
            OutputPickerComboBox.SelectedItem = _browserOutputPickerProfile ?? profiles.FirstOrDefault(p =>
                string.Equals(p.Name, _settings.SelectedOutputProfileName, StringComparison.OrdinalIgnoreCase));
        }
        finally { _outputPickerUpdating = false; }
    }

    /// <summary>Captures an active output or the detached browser source for an explicit device change.</summary>
    /// <param name="browserWasSelected">Whether the previous output was a browser session.</param>
    /// <param name="outputChanged">Whether a real output handoff is requested.</param>
    /// <returns>The source to resume, or null when playback must remain stopped.</returns>
    private BrowserOutputResume? GetOutputResumeForSelection(bool browserWasSelected, bool outputChanged) => !outputChanged ? null
        : _player is { } player ? new(_currentFilePath, _currentRadioStation, _currentPodcastPlayback, player.Position,
            player is BrowserAudioPlayer browser ? browser.UserPaused : player.IsPaused)
        : browserWasSelected ? _browserOutputResume : null;

    /// <summary>Prepares device-free browser playback through the normal readiness owner.</summary>
    /// <param name="item">Desktop-resolved source and segment metadata.</param>
    /// <param name="clientId">Captured output owner.</param>
    /// <param name="radio">Optional live radio metadata.</param>
    /// <param name="podcast">Optional podcast metadata.</param>
    /// <param name="ct">Preparation cancellation.</param>
    /// <returns>The browser player and probed source information.</returns>
    private async Task<(IAudioPlayer Player, AudioFileInfo Info)> CreateBrowserAudioPlayerAsync(
        GaplessPlaybackItem item, string clientId, Library.RadioStationRecord? radio, PodcastPlayback? podcast, CancellationToken ct)
    {
        try
        {
            var encoding = _browserMusicEncoding;
            var media = radio is not null || podcast is not null;
            if (!media && _orynivoTracksByUrl.TryGetValue(item.FilePath, out var remote) &&
                remote.OrynivoServer is { } server && remote.Id is { } remoteId)
            {
                // Browser delivery owns conversion. Bypass a native profile's
                // server transcode preference without mutating that profile.
                var source = Streaming.OrynivoServerClient.GetStreamUrl(new Streaming.OrynivoServerSettings
                    { BaseUrl = server.BaseUrl, ApiKey = server.ApiKey }, remoteId);
                var format = remote.Format?.ToLowerInvariant() ?? "audio";
                var remoteDsd = format is "dsf" or "dff" or "dsdiff";
                item = item with { SourcePath = source, KnownDuration = remote.KnownDuration ?? item.KnownDuration,
                    KnownInfo = remote.SampleRateHz is > 0 ? new KnownAudioInfo(remote.SampleRateHz.Value,
                        remote.ChannelCount ?? 2, format, remoteDsd, format) : item.KnownInfo };
            }
            if (!media && item.KnownInfo is null)
            {
                var originalItem = item;
                item = await Task.Run(() =>
                {
                    using var db = Library.AudioDatabase.OpenDefault();
                    var track = db.GetByPath(originalItem.FilePath);
                    return track is null ? originalItem : originalItem with {
                        KnownDuration = track.Duration is > 0 ? TimeSpan.FromSeconds(track.Duration.Value) : originalItem.KnownDuration,
                        KnownInfo = track.SampleRate is > 0 ? new KnownAudioInfo(track.SampleRate.Value, track.Channels ?? 2,
                            track.Format ?? "audio", track.IsDsd, track.Format) : null };
                }, ct);
            }
            var dsd = item.KnownInfo?.IsDsd == true || Path.GetExtension(item.PlaybackPath).ToLowerInvariant() is ".dsf" or ".dff";
            var direct = media || (encoding.Id == "original" || encoding.Id == "dsd-pcm" && !dsd)
                && item.SegmentStart is null && item.PlaybackPaths.Count == 1;
            if (!media && encoding.Id == "original" && !direct)
                throw new InvalidOperationException(LocalizationManager.Current.BrowserMusicFormatHint);
            var mime = radio is not null ? BrowserAudioRelay.MimeForCodec(radio.Codec)
                : BrowserAudioRelay.MimeForPath(item.PlaybackPath) ?? BrowserAudioRelay.MimeForPath("file." + item.KnownInfo?.ContainerName)
                    ?? (podcast is null ? null : BrowserAudioRelay.MimeForPath(podcast.Episode.AudioUrl));
            var info = media
                ? new AudioFileInfo(radio?.Codec ?? "audio", 0, 2, 0, false, "audio",
                    podcast?.Episode.FeedDuration ?? item.KnownDuration ?? TimeSpan.Zero)
                : item.TryCreateKnownAudioInfo() is { } known ? known with { Duration = item.SegmentDuration ?? item.KnownDuration ?? TimeSpan.Zero }
                : await BrowserAudioTranscoder.ProbeAsync(item, ct);
            ct.ThrowIfCancellationRequested();
            return (new BrowserAudioPlayer(clientId, item, info.Duration, direct: direct, directMime: mime, live: radio is not null,
                encoding: media ? null : encoding, allowFallback: media), info);
        }
        catch (OperationCanceledException) { throw; }
        catch { throw new InvalidOperationException(LocalizationManager.Current.PlaybackStopped); }
    }
}
