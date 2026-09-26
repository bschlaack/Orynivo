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
/// Embedded settings host, output-profile and equalizer pickers, output-device
/// lock, and PCM volume/ReplayGain handling for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void VolumeSlider_OnValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (VolumeValueTextBlock is null) return;
        VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";
        if (_settings.OutputBackend == OutputBackend.Wasapi &&
            _endpointVolumeSynchronizer is not null)
        {
            if (!_updatingVolumeFromSystem)
                _endpointVolumeSynchronizer.SetVolume((float)VolumeSlider.Value);
            if (_player is not null)
                _player.Volume = 1.0f;
        }
        else if (_player is not null)
        {
            _player.Volume = (float)VolumeSlider.Value;
        }
        _settings.Volume = VolumeSlider.Value;
        if (!_updatingVolumeFromSystem)
            _windowsMediaTransport?.SetVolume(VolumeSlider.Value);
    }

    private async Task ConfigureEndpointVolumeSynchronizationAsync()
    {
        var synchronizationVersion =
            Interlocked.Increment(ref _endpointVolumeSynchronizationVersion);
        var previous = DetachEndpointVolumeSynchronization();
        if (previous is not null)
            _ = Task.Run(() => DisposeEndpointSynchronizer(previous));
        if (_settings.OutputBackend != OutputBackend.Wasapi ||
            string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
        {
            return;
        }

        try
        {
            var deviceId = _settings.SelectedWasapiDeviceId;
            var synchronizer = await Task.Run(() => new WindowsEndpointVolumeSynchronizer(deviceId));
            if (synchronizationVersion !=
                    Volatile.Read(ref _endpointVolumeSynchronizationVersion) ||
                _settings.OutputBackend != OutputBackend.Wasapi ||
                !string.Equals(
                    _settings.SelectedWasapiDeviceId,
                    deviceId,
                    StringComparison.Ordinal))
            {
                _ = Task.Run(() => DisposeEndpointSynchronizer(synchronizer));
                return;
            }
            synchronizer.VolumeChanged += EndpointVolumeSynchronizer_OnVolumeChanged;
            _endpointVolumeSynchronizer = synchronizer;
            var volume = await Task.Run(() => synchronizer.Volume);
            ApplySystemVolume(volume);
        }
        catch
        {
            DisposeEndpointVolumeSynchronizationInBackground();
        }
    }

    private static async Task UpdateSearchIndexAfterArtistRenameAsync(long artistId)
    {
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                TrackSearchIndex.UpdateMany(db.GetTracksForArtistSearchIndex(artistId));
            });
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Artist rename search-index update");
        }
    }

    private WindowsEndpointVolumeSynchronizer? DetachEndpointVolumeSynchronization()
    {
        var synchronizer = _endpointVolumeSynchronizer;
        _endpointVolumeSynchronizer = null;
        if (synchronizer is not null)
            synchronizer.VolumeChanged -= EndpointVolumeSynchronizer_OnVolumeChanged;
        return synchronizer;
    }

    private void DisposeEndpointVolumeSynchronizationInBackground()
    {
        Interlocked.Increment(ref _endpointVolumeSynchronizationVersion);
        var synchronizer = DetachEndpointVolumeSynchronization();
        if (synchronizer is not null)
            _ = Task.Run(() => DisposeEndpointSynchronizer(synchronizer));
    }

    private static void DisposeEndpointSynchronizer(
        WindowsEndpointVolumeSynchronizer synchronizer)
    {
        try
        {
            synchronizer.Dispose();
        }
        catch
        {
        }
    }

    private void EndpointVolumeSynchronizer_OnVolumeChanged(object? sender, float volume) =>
        Dispatcher.UIThread.Post(() => ApplySystemVolume(volume));

    private void ApplySystemVolume(float volume)
    {
        _updatingVolumeFromSystem = true;
        try
        {
            VolumeSlider.Value = Math.Clamp(volume, 0.0f, 1.0f);
            VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";
            _settings.Volume = VolumeSlider.Value;
        }
        finally
        {
            _updatingVolumeFromSystem = false;
        }
    }

    private float GetReplayGainFactor(string filePath)
    {
        var pcmGain = GetPcmOutputGainFactor();
        if (_settings.ReplayGainMode == ReplayGainMode.Off)
            return pcmGain;

        if (TryGetCurrentReplayGainValues(filePath, out var trackGain, out var albumGain))
        {
            return pcmGain * ReplayGain.GetLinearFactor(
                _settings.ReplayGainMode,
                trackGain,
                albumGain);
        }

        return pcmGain;
    }

    private void UpdateReplayGainBadge(bool nativeDsdOutput)
    {
        ReplayGainBadgeBorder.IsVisible =
            !nativeDsdOutput &&
            _settings.ReplayGainMode != ReplayGainMode.Off &&
            TryGetCurrentReplayGainValues(_currentFilePath, out var trackGain, out var albumGain) &&
            HasPreferredReplayGainValue(trackGain, albumGain);
    }

    private bool HasPreferredReplayGainValue(string? trackGain, string? albumGain) =>
        _settings.ReplayGainMode switch
        {
            ReplayGainMode.Track => !string.IsNullOrWhiteSpace(trackGain) ||
                                    !string.IsNullOrWhiteSpace(albumGain),
            ReplayGainMode.Album => !string.IsNullOrWhiteSpace(albumGain) ||
                                    !string.IsNullOrWhiteSpace(trackGain),
            _ => false
        };

    private bool TryGetCurrentReplayGainValues(
        string? filePath,
        out string? trackGain,
        out string? albumGain)
    {
        trackGain = null;
        albumGain = null;
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        if (_orynivoTracksByUrl.TryGetValue(filePath, out var orynivoRow))
        {
            trackGain = orynivoRow.ReplayGainTrack;
            albumGain = orynivoRow.ReplayGainAlbum;
            return !string.IsNullOrWhiteSpace(trackGain) ||
                   !string.IsNullOrWhiteSpace(albumGain);
        }

        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(filePath);
            if (track is null)
                return false;
            trackGain = track.ReplayGainTrack;
            albumGain = track.ReplayGainAlbum;
            return !string.IsNullOrWhiteSpace(trackGain) ||
                   !string.IsNullOrWhiteSpace(albumGain);
        }
        catch
        {
            return false;
        }
    }

    private float GetPcmOutputGainFactor() =>
        _settings.PcmOutputBoostEnabled ? PcmOutputBoostFactor : 1.0f;

    // ------------------------------------------------------------------
    // Einstellungen
    // ------------------------------------------------------------------

    private void SettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SettingsViewHost.IsVisible)
            return;

        var view = new SettingsView(_settings, paths =>
        {
            _settings.LibraryPaths = paths;
            _settingsStore.Save(_settings);
            _libraryWatcher?.UpdatePaths(paths);
            var cleanupPaths = paths.ToList();
            _ = Task.Run(() =>
            {
                try { LibraryScanner.RemoveTracksOutsideRoots(cleanupPaths); }
                catch (Exception ex) { CrashLogger.Log(ex, "Library root cleanup"); }
            });
            // Show/hide the Local section and the empty-library hint immediately
            // when directories are added or removed while Settings is open.
            ApplySidebarNavigationSettings();
        }, (enabled, profile) =>
        {
            if (_player is IEqualizerAudioPlayer equalizerPlayer)
                equalizerPlayer.UpdateEqualizer(enabled, profile);
        },
        onLastFmBeginAuthorization: () => _lastFmScrobbler.BeginAuthorizationAsync(),
        onLastFmCompleteAuthorization: async () =>
        {
            var session = await _lastFmScrobbler.CompleteAuthorizationAsync();
            if (session is null)
                return null;
            _settings.LastFmSessionKey = session.SessionKey;
            _settings.LastFmUsername = session.Username;
            return session.Username;
        },
        onLastFmDisconnect: () =>
        {
            _lastFmScrobbler.Disconnect();
            _settings.LastFmSessionKey = string.Empty;
            _settings.LastFmUsername = string.Empty;
        });
        var completionHandled = false;
        view.RunScheduledBackup = async force =>
        {
            var completed = await RunScheduledBackupIfDueAsync(force);
            if (completed)
                view.SetScheduledBackupLastRun(_settings.ScheduledBackup.LastRunAtUnix);
            return completed;
        };
        view.LocalLibraryChanged += OnWatchedLibraryChanged;
        view.ProfileChanged += profileId => _ = OnUserProfileChangedAsync(profileId);
        view.DuplicateResolutionRequested += () => _ = OpenDuplicateResolutionAsync();
        view.CompletionRequested += async (_, accepted) =>
        {
            if (completionHandled)
                return;
            completionHandled = true;
            SettingsViewHost.IsEnabled = false;
            try
            {
                if (accepted)
                    await ApplySettingsAsync(view);
            }
            finally
            {
                CloseEmbeddedSettings();
                SettingsViewHost.IsEnabled = true;
            }
        };
        SettingsViewHost.Content = view;
        SettingsViewHost.IsVisible = true;
    }

    /// <summary>Applies the persisted headphone-crossfeed settings to a PCM player.</summary>
    /// <param name="player">The active player, or <see langword="null"/> when none is open.</param>
    private void ApplyCrossfeedSettings(IAudioPlayer? player)
    {
        if (player is ICrossfeedAudioPlayer crossfeedPlayer)
            crossfeedPlayer.UpdateCrossfeed(_settings.CrossfeedEnabled, _settings.CrossfeedStrength);
    }

    /// <summary>
    /// Applies the persisted streaming-loudness settings. Normalization runs only
    /// for radio and podcast streams, never for library tracks or native DSD.
    /// </summary>
    /// <param name="player">The active player, or <see langword="null"/> when none is open.</param>
    /// <param name="isStream">Whether the current item is a radio or podcast stream.</param>
    private void ApplyLoudnessNormalizationSettings(IAudioPlayer? player, bool isStream)
    {
        if (player is not ILoudnessNormalizerAudioPlayer normalizerPlayer)
            return;

        normalizerPlayer.UpdateLoudnessNormalization(
            isStream && _settings.StreamingLoudnessNormalizationEnabled,
            StreamingLoudnessNormalizer.DefaultTargetDbfs,
            StreamingLoudnessNormalizer.DefaultMaximumGainDb);
    }

    /// <summary>Applies a newly selected profile and refreshes profile-sensitive views.</summary>
    /// <param name="profileId">Stable identifier of the selected profile.</param>
    private async Task OnUserProfileChangedAsync(string profileId)
    {
        AudioDatabase.SetActiveProfile(profileId);
        ApplyServerProfileContext();
        _settingsStore.Save(_settings);
        StopInfiniteMix();
        InvalidateDashboardCatalogCache();
        InvalidateGenreCloudViewCache();
        InvalidateUnifiedLibraryViewCache();
        if (!string.IsNullOrWhiteSpace(_currentTopLevelTag))
            await ShowTopLevelViewAsync(_currentTopLevelTag);
    }

    /// <summary>Applies the active local profile's server-profile mappings to remote clients.</summary>
    private void ApplyServerProfileContext()
    {
        if (_profileManager is null)
            return;
        var profile = _profileManager.ActiveProfile;
        foreach (var server in _settings.OrynivoServers)
        {
            server.ProfileId = profile.ServerProfileIds.TryGetValue(server.Id, out var mapped)
                && !string.IsNullOrWhiteSpace(mapped)
                ? mapped
                : "standard";
        }
    }

    private async Task RefreshServerProfileMappingsAsync()
    {
        if (_profileManager is null || _settings.OrynivoServers.Count == 0)
            return;
        using var client = new OrynivoServerClient();
        var profile = _profileManager.ActiveProfile;
        foreach (var server in _settings.OrynivoServers)
        {
            var profiles = await client.GetProfilesAsync(server);
            if (profiles.Count == 0)
                continue;
            if (!profile.ServerProfileIds.TryGetValue(server.Id, out var mapped) ||
                profiles.All(p => !string.Equals(p.Id, mapped, StringComparison.OrdinalIgnoreCase)))
            {
                var sameName = profiles.FirstOrDefault(p =>
                    string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
                profile.ServerProfileIds[server.Id] = sameName?.Id
                    ?? profiles.FirstOrDefault(p => string.Equals(p.Id, "standard", StringComparison.OrdinalIgnoreCase))?.Id
                    ?? profiles[0].Id;
            }
        }
        ApplyServerProfileContext();
        _settingsStore.Save(_settings);
        _ = SyncProfileHistoryInBackgroundAsync();
    }

    /// <summary>Replicates the active profile's recent history without blocking the UI.</summary>
    private async Task SyncProfileHistoryInBackgroundAsync()
    {
        try
        {
            List<SyncedPlaybackHistoryEntry> local;
            using (var db = AudioDatabase.OpenDefault())
                local = db.GetHistoryForSync(1000);
            var payload = local.Select(entry => new OrynivoHistorySyncEntry(
                entry.SyncId, entry.Path, entry.StartedAtUnix, entry.PositionSeconds,
                entry.DurationSeconds, entry.MediaType, entry.Title, entry.Subtitle,
                entry.Album, entry.ExternalId, entry.Genre)).ToList();
            foreach (var server in _settings.OrynivoServers ?? [])
            {
                var remote = await _orynivoClient.GetHistorySyncAsync(server, 1000).ConfigureAwait(false);
                using (var db = AudioDatabase.OpenDefault())
                {
                    foreach (var entry in remote)
                        db.ImportSyncedHistory(new SyncedPlaybackHistoryEntry(
                            entry.SyncId, entry.Path, entry.StartedAtUnix, entry.PositionSeconds,
                            entry.DurationSeconds, entry.MediaType, entry.Title, entry.Subtitle,
                            entry.Album, entry.ExternalId, entry.Genre));
                }
                await _orynivoClient.PushHistorySyncAsync(server, payload).ConfigureAwait(false);
            }
            InvalidateDashboardCatalogCache();
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Background profile history synchronization");
        }
    }

    private void CloseEmbeddedSettings()
    {
        if (SettingsViewHost.Content is SettingsView settingsView)
            settingsView.Deactivate();
        SettingsViewHost.Content = null;
        SettingsViewHost.IsVisible = false;
    }

    private void OpenSettingsAt(string sectionTag, bool scrollToEqualizer = false)
    {
        if (!SettingsViewHost.IsVisible)
            SettingsButton_OnClick(null!, null!);
        if (SettingsViewHost.Content is SettingsView sv)
        {
            sv.NavigateToSection(sectionTag);
            if (scrollToEqualizer)
                sv.ScrollToEqualizerSection();
        }
    }

    // ------------------------------------------------------------------
    // EQ + Output quick-pick popups
    // ------------------------------------------------------------------

    private void EqPickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputPickerPopup.IsOpen = false;
        _eqPickerUpdating = true;
        try
        {
            EqPickerComboBox.ItemsSource = _settings.EqualizerProfiles;
            EqPickerComboBox.SelectedItem = _settings.EqualizerProfiles
                .FirstOrDefault(p => string.Equals(
                    p.Name, _settings.SelectedEqualizerProfileName,
                    StringComparison.OrdinalIgnoreCase));
            EqPickerEnabledCheckBox.IsChecked = _settings.EqualizerEnabled;
        }
        finally
        {
            _eqPickerUpdating = false;
        }
        EqPickerPopup.IsOpen = !EqPickerPopup.IsOpen;
    }

    private void EqPickerComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_eqPickerUpdating) return;
        if (EqPickerComboBox.SelectedItem is not EqualizerProfile profile) return;
        _settings.SelectedEqualizerProfileName = profile.Name;
        _settings.EqualizerProfile = profile.Clone();
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(_settings.EqualizerEnabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
    }

    private void EqPickerEnabledCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (_eqPickerUpdating) return;
        _settings.EqualizerEnabled = EqPickerEnabledCheckBox.IsChecked == true;
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(_settings.EqualizerEnabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
    }

    private void EqPickerSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        OpenSettingsAt("AudioDevice", scrollToEqualizer: true);
    }

    private void OutputPickerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        _outputPickerUpdating = true;
        try
        {
            OutputPickerComboBox.ItemsSource = _settings.OutputProfiles;
            OutputPickerComboBox.SelectedItem = _settings.OutputProfiles
                .FirstOrDefault(p => string.Equals(
                    p.Name, _settings.SelectedOutputProfileName,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _outputPickerUpdating = false;
        }
        OutputPickerPopup.IsOpen = !OutputPickerPopup.IsOpen;
    }

    private async void OutputPickerComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_outputPickerUpdating) return;
        if (OutputPickerComboBox.SelectedItem is not OutputProfile profile) return;
        await SelectOutputProfileAsync(profile);
    }

    /// <summary>Selects a configured output profile by display name.</summary>
    /// <param name="profileName">Exact profile name.</param>
    /// <returns><see langword="true"/> when the profile exists and was selected.</returns>
    private async Task<bool> SelectOutputProfileByNameAsync(string profileName)
    {
        var requestedName = profileName?.Trim() ?? string.Empty;
        const string selectedSuffix = " (selected)";
        if (requestedName.EndsWith(selectedSuffix, StringComparison.OrdinalIgnoreCase))
            requestedName = requestedName[..^selectedSuffix.Length].TrimEnd();

        var profile = (_settings.OutputProfiles ?? []).FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requestedName, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
            return false;
        await SelectOutputProfileAsync(profile);
        return true;
    }

    /// <summary>Selects an output profile and preserves active playback across device changes.</summary>
    /// <param name="profile">Profile to select.</param>
    /// <returns>A task representing the asynchronous device switch.</returns>
    private async Task SelectOutputProfileAsync(OutputProfile profile)
    {
        if (string.Equals(profile.Name, _settings.SelectedOutputProfileName, StringComparison.Ordinal)) return;

        var outputChanged =
            _settings.OutputBackend != profile.Backend ||
            !string.Equals(_settings.SelectedDriverName, profile.SelectedDriverName, StringComparison.Ordinal) ||
            !string.Equals(_settings.SelectedWasapiDeviceId, profile.SelectedWasapiDeviceId, StringComparison.Ordinal) ||
            !string.Equals(_settings.SelectedAirPlayDeviceId, profile.SelectedAirPlayDeviceId, StringComparison.Ordinal);

        // Snapshot playback state before StopPlaybackCore clears station/podcast fields.
        var resumePath     = _currentFilePath;
        var resumeStation  = _currentRadioStation;
        var resumePodcast  = _currentPodcastPlayback;
        var resumePosition = outputChanged && _player is not null ? _player.Position : TimeSpan.Zero;
        var wasPaused      = outputChanged && _player?.IsPaused == true;
        var shouldResume   = outputChanged && _player is not null && !string.IsNullOrEmpty(resumePath);

        if (outputChanged && _player is not null)
            await StopPlaybackAsync();

        _settings.SelectedOutputProfileName  = profile.Name;
        _settings.OutputBackend              = profile.Backend;
        _settings.SelectedDriverName         = profile.SelectedDriverName;
        _settings.SelectedWasapiDeviceId     = profile.SelectedWasapiDeviceId;
        _settings.SelectedWasapiDeviceName   = profile.SelectedWasapiDeviceName;
        _settings.SelectedAirPlayDeviceId    = profile.SelectedAirPlayDeviceId;
        _settings.SelectedAirPlayDeviceName  = profile.SelectedAirPlayDeviceName;
        _settings.SelectedAirPlayHost        = profile.SelectedAirPlayHost;
        _settings.SelectedAirPlayPort        = profile.SelectedAirPlayPort;
        _ = Task.Run(() => _settingsStore.Save(_settings));

        if (!shouldResume)
            return;
        try
        {
            await StartPlaybackAsync(resumePath, resumeStation, resumePodcast, resumePosition);
            if (wasPaused)
                PausePlayback();
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch (Exception ex)
        {
            StopPlayback();
            StatusTextBlock.Text = ex.Message;
        }
    }

    private void OutputPickerSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        OutputPickerPopup.IsOpen = false;
        OpenSettingsAt("AudioDevice");
    }

    /// <summary>Releases or reacquires the exclusive output device while preserving playback context.</summary>
    /// <param name="sender">Event sender.</param>
    /// <param name="e">Click event arguments.</param>
    private async void OutputDeviceLockButton_OnClick(object? sender, RoutedEventArgs e)
    {
        EqPickerPopup.IsOpen = false;
        OutputPickerPopup.IsOpen = false;

        if (_player is { } player)
        {
            _releasedOutputPath = _currentFilePath;
            _releasedOutputRadioStation = _currentRadioStation;
            _releasedOutputPodcastPlayback = _currentPodcastPlayback;
            _releasedOutputPosition = player.Position;
            _releasedOutputWasPaused = player.IsPaused;
            await StopPlaybackAsync(waitForCompleteDisposal: true);
            _audioDeviceExplicitlyReleased = true;
            UpdateOutputDeviceLockButton();
            StatusTextBlock.Text = LocalizationManager.Current.OutputDeviceReleased;
            return;
        }

        if (!_audioDeviceExplicitlyReleased || string.IsNullOrWhiteSpace(_releasedOutputPath))
            return;

        var path = _releasedOutputPath;
        var station = _releasedOutputRadioStation;
        var podcast = _releasedOutputPodcastPlayback;
        var position = _releasedOutputPosition;
        var wasPaused = _releasedOutputWasPaused;
        try
        {
            await StartPlaybackAsync(path, station, podcast, position);
            if (wasPaused)
                PausePlayback();
        }
        catch (OperationCanceledException)
        {
            _audioDeviceExplicitlyReleased = true;
            UpdateOutputDeviceLockButton();
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch (Exception ex)
        {
            _audioDeviceExplicitlyReleased = true;
            UpdateOutputDeviceLockButton();
            StatusTextBlock.Text = ex.Message;
        }
    }

    /// <summary>Selects an equalizer profile and applies the requested enabled state.</summary>
    /// <param name="profileName">Profile name, or <see langword="null"/> to retain the selection.</param>
    /// <param name="enabled">Requested enabled state.</param>
    /// <returns><see langword="true"/> when the requested profile exists.</returns>
    private Task<bool> ConfigureEqualizerAsync(string? profileName, bool enabled)
    {
        EqualizerProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(profileName))
        {
            profile = (_settings.EqualizerProfiles ?? []).FirstOrDefault(candidate =>
                string.Equals(candidate.Name, profileName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (profile is null)
                return Task.FromResult(false);
            _settings.SelectedEqualizerProfileName = profile.Name;
            _settings.EqualizerProfile = profile.Clone();
        }

        _settings.EqualizerEnabled = enabled;
        if (_player is IEqualizerAudioPlayer eqPlayer)
            eqPlayer.UpdateEqualizer(enabled, _settings.EqualizerProfile);
        _ = Task.Run(() => _settingsStore.Save(_settings));
        return Task.FromResult(true);
    }

    /// <summary>Refreshes the lock icon, tooltip, and availability from the actual player state.</summary>
    private void UpdateOutputDeviceLockButton()
    {
        if (OutputDeviceLockButton is null || OutputDeviceLockIconPath is null)
            return;
        var isHeld = _player is not null;
        OutputDeviceLockIconPath.Data = FindResource<StreamGeometry>(
            isHeld ? "IconLockClosed" : "IconLockOpen");
        OutputDeviceLockButton.IsEnabled = isHeld ||
            (_audioDeviceExplicitlyReleased && !string.IsNullOrWhiteSpace(_releasedOutputPath));
        ToolTip.SetTip(
            OutputDeviceLockButton,
            isHeld
                ? LocalizationManager.Current.ReleaseOutputDevice
                : _audioDeviceExplicitlyReleased
                    ? LocalizationManager.Current.ReacquireOutputDevice
                    : LocalizationManager.Current.OutputDeviceReleased);
    }

    /// <summary>Clears the one-shot playback snapshot after the output device was reacquired normally.</summary>
    private void ClearReleasedOutputResumeState()
    {
        _releasedOutputPath = null;
        _releasedOutputRadioStation = null;
        _releasedOutputPodcastPlayback = null;
        _releasedOutputPosition = TimeSpan.Zero;
        _releasedOutputWasPaused = false;
    }

    private async Task ApplySettingsAsync(SettingsView window)
    {
            var themeChanged = _settings.Theme != window.SelectedTheme;
            var languageChanged = _settings.Language != window.SelectedLanguage;
            var genreCloudBackgroundChanged =
                _settings.GenreCloudBackground != window.SelectedGenreCloudBackground ||
                Math.Abs(
                    _settings.GenreCloudBackgroundOpacity -
                    window.SelectedGenreCloudBackgroundOpacity) > 0.0001;
            var replayGainChanged =
                _settings.ReplayGainMode != window.SelectedReplayGainMode ||
                _settings.PcmOutputBoostEnabled != window.PcmOutputBoostEnabled;
            var artistInfoChanged =
                _settings.ArtistInfoSource != window.SelectedArtistInfoSource ||
                !string.Equals(
                    _settings.LastFmApiKey,
                    window.SelectedLastFmApiKey,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _settings.FanartTvApiKey,
                    window.SelectedFanartTvApiKey,
                    StringComparison.Ordinal);
            var sidebarChanged =
                _settings.ShowInternetRadioItem != window.ShowInternetRadioItem ||
                _settings.ShowPodcastsItem != window.ShowPodcastsItem ||
                _settings.ShowQueueItem != window.ShowQueueItem ||
                _settings.ShowAiChatItem != window.ShowAiChatItem ||
                _settings.ShowLocalLibrarySection != window.ShowLocalLibrarySection ||
                _settings.ShowOwnRadiosSection != window.ShowOwnRadiosSection ||
                _settings.ShowMyPodcastsSection != window.ShowMyPodcastsSection ||
                _settings.ShowPlexSection != window.ShowPlexSection;
            var motionChanged = _settings.ReduceMotion != window.ReduceMotionValue;
            var mcpChanged =
                _settings.McpServerEnabled != window.McpServerEnabled ||
                _settings.McpServerPort    != window.McpServerPort ||
                _settings.McpNetworkAccessEnabled != window.McpNetworkAccessEnabled ||
                !string.Equals(_settings.McpAccessToken, window.McpAccessToken, StringComparison.Ordinal);
            var mobileRemoteChanged =
                _settings.MobileRemoteEnabled != window.MobileRemoteEnabled ||
                _settings.MobileRemotePort != window.MobileRemotePort ||
                !string.Equals(
                    _settings.MobileRemoteAccessToken,
                    window.MobileRemoteAccessToken,
                    StringComparison.Ordinal);
            var plexServersChanged = !PlexServerSettingsEqual(
                _settings.PlexServers,
                window.SelectedPlexServers);
            var orynivoServersChanged = !OrynivoServerSettingsEqual(
                _settings.OrynivoServers,
                window.SelectedOrynivoServers);
            var libraryPathsChanged = !(_settings.LibraryPaths ?? []).SequenceEqual(window.SelectedLibraryPaths);
            var hadOrynivoServers = _settings.OrynivoServers.Count > 0;
            var outputChanged =
                _settings.OutputBackend != window.SelectedOutputBackend ||
                _settings.DsdOverPcmEnabled != window.DsdOverPcmEnabled ||
                !string.Equals(
                    _settings.SelectedDriverName,
                    window.SelectedDriverName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    _settings.SelectedWasapiDeviceId,
                    window.SelectedWasapiDeviceId,
                    StringComparison.Ordinal);
            outputChanged = outputChanged || !string.Equals(
                _settings.SelectedAirPlayDeviceId,
                window.SelectedAirPlayDeviceId,
                StringComparison.Ordinal);
            if (outputChanged && _player is not null)
                await StopPlaybackAsync();

            _settings.OutputProfiles             = window.SelectedOutputProfiles.ToList();
            _settings.SelectedOutputProfileName  = window.SelectedOutputProfileName;
            _settings.OutputBackend              = window.SelectedOutputBackend;
            _settings.SelectedDriverName         = window.SelectedDriverName;
            _settings.SelectedWasapiDeviceId     = window.SelectedWasapiDeviceId;
            _settings.SelectedWasapiDeviceName   = window.SelectedWasapiDeviceName;
            _settings.SelectedAirPlayDeviceId    = window.SelectedAirPlayDeviceId;
            _settings.SelectedAirPlayDeviceName  = window.SelectedAirPlayDeviceName;
            _settings.SelectedAirPlayHost        = window.SelectedAirPlayHost;
            _settings.SelectedAirPlayPort        = window.SelectedAirPlayPort;
            _settings.ReplayGainMode        = window.SelectedReplayGainMode;
            _settings.CalculateMissingReplayGainDuringScan = window.CalculateMissingReplayGainDuringScan;
            _settings.AlwaysConvertDsdToPcm = window.AlwaysConvertDsdToPcm;
            _settings.DsdOverPcmEnabled = window.DsdOverPcmEnabled;
            _settings.PcmOutputBoostEnabled = window.PcmOutputBoostEnabled;
            _settings.MaxOutputSampleRateHz  = window.MaxOutputSampleRateHz;
            _settings.VisualizerPresetDirectory = window.VisualizerPresetDirectoryValue;
            _settings.VisualizerAlwaysShowOverlay = window.VisualizerAlwaysShowOverlay;
            _settings.VisualizerRenderWidth = window.VisualizerRenderWidthValue;
            _settings.VisualizerRenderHeight = window.VisualizerRenderHeightValue;
            _settings.VisualizerFrameRate = window.VisualizerFrameRateValue;
            _settings.VisualizerAutoAdvanceEnabled = window.VisualizerAutoAdvanceEnabled;
            _settings.VisualizerAutoAdvanceSeconds = window.VisualizerAutoAdvanceSeconds;
            _settings.DisabledVisualizerPresets = window.DisabledVisualizerPresets.ToList();
            _settings.NonGaplessCrossfadeSeconds = window.NonGaplessCrossfadeSeconds;
            _settings.EqualizerEnabled      = window.EqualizerEnabled;
            _settings.EqualizerProfile      = window.SelectedEqualizerProfile;
            _settings.EqualizerProfiles     = window.SelectedEqualizerProfiles.ToList();
            _settings.SelectedEqualizerProfileName = window.SelectedEqualizerProfileName;
            _settings.LibraryPaths           = window.SelectedLibraryPaths.ToList();
            _libraryWatcher?.UpdateReplayGainAnalysis(_settings.CalculateMissingReplayGainDuringScan);
            _libraryWatcher?.UpdatePaths(_settings.LibraryPaths);
            if (libraryPathsChanged)
            {
                var cleanupPaths = _settings.LibraryPaths.ToList();
                _ = Task.Run(() =>
                {
                    try
                    {
                        LibraryScanner.RemoveTracksOutsideRoots(cleanupPaths);
                    }
                    catch (Exception ex)
                    {
                        CrashLogger.Log(ex, "Library root cleanup");
                    }
                });
            }
            _settings.Theme                  = window.SelectedTheme;
            _settings.Language               = window.SelectedLanguage;
            _settings.GenreCloudBackground   = window.SelectedGenreCloudBackground;
            _settings.GenreCloudBackgroundOpacity = window.SelectedGenreCloudBackgroundOpacity;
            _settings.ArtistInfoSource       = window.SelectedArtistInfoSource;
            _settings.LastFmApiKey           = window.SelectedLastFmApiKey;
            _settings.LastFmApiSecret        = window.SelectedLastFmApiSecret;
            _settings.LastFmScrobblingEnabled = window.SelectedLastFmScrobblingEnabled;
            _settings.StreamingLoudnessNormalizationEnabled = window.SelectedStreamingLoudnessNormalizationEnabled;
            _settings.CrossfeedEnabled       = window.SelectedCrossfeedEnabled;
            _settings.CrossfeedStrength      = window.SelectedCrossfeedStrength;
            _settings.FanartTvApiKey         = window.SelectedFanartTvApiKey;
            _settings.QobuzApplicationId      = window.SelectedQobuzApplicationId;
            _settings.PlexServers             = window.SelectedPlexServers.ToList();
            _settings.OrynivoServers          = window.SelectedOrynivoServers.ToList();
            if (!hadOrynivoServers && _settings.OrynivoServers.Count > 0)
                _settings.IsLocalLibrarySectionExpanded = true;
            _settings.McpServerEnabled        = window.McpServerEnabled;
            _settings.McpServerPort           = window.McpServerPort;
            _settings.McpNetworkAccessEnabled = window.McpNetworkAccessEnabled;
            _settings.McpAccessToken          = window.McpAccessToken;
            _settings.MobileRemoteEnabled     = window.MobileRemoteEnabled;
            _settings.MobileRemotePort        = window.MobileRemotePort;
            _settings.MobileRemoteAccessToken = window.MobileRemoteAccessToken;
            _settings.DisabledMcpTools        = window.DisabledMcpTools;
            _mcpBridge.DisabledTools          = _settings.DisabledMcpTools;
            _settings.AiChat                  = window.AiChatSettingsValue;
            _aiChatView.GetSettings           = () => _settings.AiChat;
            _settings.WebBrowsing             = window.WebBrowsingValue;
            if (_webBrowsing is not null)
                _webBrowsing.Options          = _settings.WebBrowsing;
            _settings.ScheduledBackup.Enabled        = window.ScheduledBackupEnabledValue;
            _settings.ScheduledBackup.IntervalDays   = window.ScheduledBackupIntervalValue;
            _settings.ScheduledBackup.RetentionCount = window.ScheduledBackupRetentionValue;
            _settings.ScheduledBackup.Directory      = window.ScheduledBackupDirectoryValue;
        _settings.BackupTarget ??= new BackupTargetSettings();
        _settings.BackupTarget.Enabled         = window.BackupTargetEnabledValue;
        _settings.BackupTarget.UploadUrl       = window.BackupTargetUrlValue;
        _settings.BackupTarget.RemoteDirectory = window.BackupTargetDirectoryValue;
        _settings.BackupTarget.UserName        = window.BackupTargetUserNameValue;
        _settings.BackupTarget.Password        = window.BackupTargetPasswordValue;
            _settings.PodcastDownloadLimitMb  = window.PodcastDownloadLimitMbValue;
            _settings.ShowInternetRadioItem   = window.ShowInternetRadioItem;
            _settings.ShowPodcastsItem        = window.ShowPodcastsItem;
            _settings.ShowQueueItem           = window.ShowQueueItem;
            _settings.ShowAiChatItem          = window.ShowAiChatItem;
            _settings.ReduceMotion             = window.ReduceMotionValue;
            _settings.CheckForUpdatesOnStartup = window.CheckForUpdatesOnStartup;
            _settings.StartMaximized           = window.StartMaximized;
            _settings.ShowLocalLibrarySection = window.ShowLocalLibrarySection;
            _settings.ShowOwnRadiosSection    = window.ShowOwnRadiosSection;
            _settings.ShowMyPodcastsSection   = window.ShowMyPodcastsSection;
            _settings.ShowPlexSection              = window.ShowPlexSection;
            if (window.PlexCredentialsChanged)
            {
                try
                {
                    var plexTokens = window.SelectedPlexTokens.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value);
                    await Task.Run(() => new WindowsPlexCredentialStore().SaveAll(plexTokens));
                }
                catch
                {
                }
            }
            await Task.Run(() => _settingsStore.Save(_settings));
            _lastFmScrobbler.Configure(
                _settings.LastFmScrobblingEnabled,
                _settings.LastFmApiKey,
                _settings.LastFmApiSecret,
                _settings.LastFmSessionKey,
                _settings.LastFmUsername);
            ApplyCrossfeedSettings(_player);
            ApplyLoudnessNormalizationSettings(
                _player,
                _currentRadioStation is not null || _currentPodcastPlayback is not null);
            if (mcpChanged)
            {
                if (_settings.McpServerEnabled)
                    await _mcpServer.StartAsync(
                        _settings.McpServerPort,
                        _mcpBridge,
                        _settings.McpNetworkAccessEnabled,
                        _settings.McpAccessToken);
                else
                    await _mcpServer.StopAsync();
            }
            if (mobileRemoteChanged)
            {
                if (_settings.MobileRemoteEnabled)
                    await _mobileRemoteServer.StartAsync(
                        _settings.MobileRemotePort,
                        _settings.MobileRemoteAccessToken,
                        _mcpBridge);
                else
                    await _mobileRemoteServer.StopAsync();
            }
            if (themeChanged)
            {
                ThemeManager.Apply(_settings.Theme);
                UpdateNowPlayingRowHighlights();
            }
            if (languageChanged)
            {
                LocalizationManager.Apply(_settings.Language);
                // Recreate dynamic navigation entries so headers such as the
                // local Playlists group use the newly selected language.
                LoadNavPlaylists();
                UpdateOutputDeviceLockButton();
                if (string.Equals(_currentTopLevelTag, "Dashboard", StringComparison.Ordinal))
                    await BuildDashboardAsync();
            }
            if (genreCloudBackgroundChanged)
            {
                GenreCloudBackgroundImage.Source = null;
                GenreCloudBackgroundImage.Opacity = 0;
                GenreCloudBackgroundShade.Opacity = 0;
            }
            if (motionChanged &&
                string.Equals(_currentTopLevelTag, "Dashboard", StringComparison.Ordinal))
            {
                await BuildDashboardAsync();
            }
            if (artistInfoChanged)
                ApplyArtistInfoSettings();
            if (outputChanged)
                RefreshSelectedDriverText();
            if (plexServersChanged || window.PlexCredentialsChanged)
                LoadNavPlaylists();
            else if (sidebarChanged || libraryPathsChanged)
                ApplySidebarNavigationSettings();
            if (orynivoServersChanged)
                LoadOrynivoServerNavigation();
            if (replayGainChanged && _player is not null)
            {
                var currentFilePath = _currentFilePath;
                var replayGainFactor = await Task.Run(() =>
                    GetReplayGainFactorFromDatabase(currentFilePath));
                if (_player is not null &&
                    string.Equals(
                        _currentFilePath,
                        currentFilePath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _player.ReplayGainFactor = replayGainFactor;
                    UpdateReplayGainBadge(
                        _player is DsfAudioPlayer or DffAudioPlayer or RemoteDsfAudioPlayer or RemoteDffAudioPlayer
#if !WINDOWS
                        or DsfDopAudioPlayer or RemoteDsfDopAudioPlayer or DffDopAudioPlayer
#endif
                    );
                }
            }
            if (_player is IEqualizerAudioPlayer equalizerPlayer)
                equalizerPlayer.UpdateEqualizer(
                    _settings.EqualizerEnabled,
                    _settings.EqualizerProfile);
            if (outputChanged)
                _ = ConfigureEndpointVolumeSynchronizationAsync();

            StatusTextBlock.Text = _settings.OutputBackend switch
            {
                OutputBackend.Asio or OutputBackend.CwAsio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                    LocalizationManager.Current.SelectAsioDevice,
                OutputBackend.Wasapi when string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId) =>
                    LocalizationManager.Current.SelectWasapiDevice,
                OutputBackend.AirPlay when string.IsNullOrWhiteSpace(_settings.SelectedAirPlayDeviceId) =>
                    LocalizationManager.Current.SelectAirPlayDevice,
                OutputBackend.KernelStreaming =>
                    string.Format(LocalizationManager.Current.NotImplemented, "KernelStreaming"),
                _ => LocalizationManager.Current.SettingsSaved
            };
    }

    private float GetReplayGainFactorFromDatabase(string filePath)
        => GetReplayGainFactor(filePath);
}
