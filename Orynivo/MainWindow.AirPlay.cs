using Orynivo.Audio;
using Orynivo.Localization;

namespace Orynivo;

public partial class MainWindow
{
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
        return await AirPlayAudioPlayer.CreateAsync(
            item,
            host,
            port,
            _settings.SelectedAirPlayDeviceName,
            _settings.SelectedAirPlayDeviceId,
            (float)VolumeSlider.Value,
            GetReplayGainFactor(item.FilePath),
            cancellationToken);
    }
}
