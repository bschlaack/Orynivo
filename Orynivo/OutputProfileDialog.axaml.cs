using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Audio;
using Orynivo.Audio.CoreAudio;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Dialog for creating or editing a named audio output profile.</summary>
internal partial class OutputProfileDialog : Window
{
    /// <summary>Separates legacy non-Windows PCM routes sharing the WASAPI compatibility value.</summary>
    private enum LinuxDeviceKind
    {
        Any,
        OpenAl,
        DirectAlsa
    }

    /// <summary>One platform-appropriate output type in the shared profile dialog.</summary>
    /// <param name="Value">Persisted backend value.</param>
    /// <param name="Label">Localized output type label.</param>
    /// <param name="LinuxDeviceKind">Optional compatibility-route filter.</param>
    private sealed record BackendChoice(
        OutputBackend Value,
        string Label,
        LinuxDeviceKind LinuxDeviceKind = LinuxDeviceKind.Any)
    {
        /// <inheritdoc/>
        public override string ToString() => Label;
    }

    private readonly HashSet<string> _existingNames;
    private readonly bool _isMacOS;
    private readonly Func<OutputBackend, IReadOnlyList<WasapiDeviceInfo>>? _enumeratePcmDevices;
    private readonly SemaphoreSlim _deviceLoadGate = new(1, 1);
    private int _deviceLoadVersion;

    /// <summary>
    /// Initializes the dialog for creating a new output profile.
    /// </summary>
    /// <param name="existingNames">Profile names already in use.</param>
    internal OutputProfileDialog(IEnumerable<string> existingNames)
        : this(existingNames, null)
    {
    }

    /// <summary>
    /// Initializes the dialog pre-populated with an existing profile for editing.
    /// </summary>
    /// <param name="existingNames">Profile names already in use (must exclude the profile being edited).</param>
    /// <param name="profile">Existing profile to edit, or <see langword="null"/> for a new profile.</param>
    internal OutputProfileDialog(IEnumerable<string> existingNames, OutputProfile? profile)
        : this(existingNames, profile, OperatingSystem.IsMacOS(), null)
    {
    }

    /// <summary>Constructs the shared dialog with injectable platform/device boundaries for isolated layout checks.</summary>
    /// <param name="existingNames">Names already in use.</param>
    /// <param name="profile">The existing output, if any.</param>
    /// <param name="isMacOS">Whether the CoreAudio platform choice is available.</param>
    /// <param name="enumeratePcmDevices">Optional synthetic PCM device source; production leaves it unset.</param>
    internal OutputProfileDialog(IEnumerable<string> existingNames, OutputProfile? profile, bool isMacOS,
        Func<OutputBackend, IReadOnlyList<WasapiDeviceInfo>>? enumeratePcmDevices)
    {
        _isMacOS = isMacOS;
        _enumeratePcmDevices = enumeratePcmDevices;
        _initialProfile = profile;
        InitializeComponent();
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        CoreAudioMatchRateCheckBox.IsChecked = profile?.CoreAudioMatchRate ?? true;
        CoreAudioExclusiveCheckBox.IsChecked = profile?.CoreAudioExclusive ?? false;
        CoreAudioRestoreRateCheckBox.IsChecked = profile?.CoreAudioRestoreRate ?? true;
        CoreAudioBypassCheckBox.IsChecked = profile?.CoreAudioBypass ?? false;

        var isEditing = profile is not null;
        Title = isEditing
            ? LocalizationManager.Current.OutputProfileConfigureTitle
            : LocalizationManager.Current.OutputProfileCreateTitle;

        var availableBackends = new List<BackendChoice>
        {
            new(OutputBackend.Asio, LocalizationManager.Current.SteinbergAsio),
            new(OutputBackend.CwAsio, LocalizationManager.Current.CwAsio),
            new(OutputBackend.AirPlay, LocalizationManager.Current.AirPlay)
        };
        if (!OperatingSystem.IsWindows() || _isMacOS)
        {
            if (_isMacOS)
                availableBackends.Add(new(OutputBackend.CoreAudio, LocalizationManager.Current.CoreAudio));
            availableBackends.Add(
                new(OutputBackend.Wasapi, LocalizationManager.Current.OpenAl, LinuxDeviceKind.OpenAl));
            if (OperatingSystem.IsLinux())
            {
                availableBackends.Add(
                    new(OutputBackend.Wasapi, LocalizationManager.Current.DirectAlsa, LinuxDeviceKind.DirectAlsa));
            }
        }
        else
        {
            availableBackends.Add(new(OutputBackend.Wasapi, "WASAPI"));
        }
        var filteredBackends = availableBackends
            .Where(c => c.Value != OutputBackend.Asio || SteinbergAsioStream.IsAvailable)
            .Where(c => c.Value != OutputBackend.CwAsio || SteinbergAsioStream.IsCwAsioAvailable)
            .ToArray();
        BackendComboBox.ItemsSource = filteredBackends;

        if (profile is not null)
        {
            NameTextBox.Text = profile.Name;
            BackendComboBox.SelectedItem =
                filteredBackends.FirstOrDefault(c =>
                    c.Value == profile.Backend &&
                    (profile.Backend == OutputBackend.AirPlay ||
                     OperatingSystem.IsWindows() ||
                     profile.Backend != OutputBackend.Wasapi ||
                     c.LinuxDeviceKind == ResolveLinuxDeviceKind(profile.SelectedWasapiDeviceId)))
                ?? filteredBackends.FirstOrDefault(c => c.Value == OutputBackend.Wasapi)
                ?? filteredBackends.FirstOrDefault();
        }
        else
        {
            BackendComboBox.SelectedItem =
                filteredBackends.FirstOrDefault(c =>
                    c.Value == OutputBackend.Wasapi &&
                    c.LinuxDeviceKind != LinuxDeviceKind.DirectAlsa)
                ?? filteredBackends.FirstOrDefault();
        }

        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
            _ = LoadDevicesAsync();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    private readonly OutputProfile? _initialProfile;

    /// <summary>Gets the confirmed output profile after a successful save.</summary>
    internal OutputProfile? Result { get; private set; }

    private OutputBackend SelectedBackend =>
        BackendComboBox.SelectedItem is BackendChoice choice ? choice.Value : OutputBackend.Wasapi;

    private LinuxDeviceKind SelectedLinuxDeviceKind =>
        BackendComboBox.SelectedItem is BackendChoice choice
            ? choice.LinuxDeviceKind
            : LinuxDeviceKind.Any;

    private static LinuxDeviceKind ResolveLinuxDeviceKind(string? deviceId) =>
        deviceId?.StartsWith("alsa:", StringComparison.Ordinal) == true
            ? LinuxDeviceKind.DirectAlsa
            : LinuxDeviceKind.OpenAl;

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateSaveButton();

    private void NameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CanSave())
            Confirm();
    }

    private void BackendComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CoreAudioOptionsPanel.IsVisible = SelectedBackend == OutputBackend.CoreAudio;
        _ = LoadDevicesAsync();
    }

    private void CoreAudioBypassCheckBox_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (CoreAudioMatchRateCheckBox is null || CoreAudioExclusiveCheckBox is null) return;
        var bypass = CoreAudioBypassCheckBox.IsChecked == true;
        if (bypass)
        {
            CoreAudioMatchRateCheckBox.IsChecked = true;
            CoreAudioExclusiveCheckBox.IsChecked = true;
        }
        CoreAudioMatchRateCheckBox.IsEnabled = !bypass;
        CoreAudioExclusiveCheckBox.IsEnabled = !bypass;
    }

    private void DeviceComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        DeviceInfoButton.IsVisible = DeviceComboBox.SelectedItem is string or WasapiDeviceInfo;
        UpdateSaveButton();
    }

    private async void DeviceInfoButton_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DeviceComboBox.SelectedItem is string driverName)
            {
                var info = SteinbergAsioStream.GetDeviceInfo(SelectedBackend, driverName);
                await new DeviceInfoWindow(info).ShowDialog(this);
            }
            else if (DeviceComboBox.SelectedItem is WasapiDeviceInfo wasapiDevice)
            {
                var backend = SelectedBackend;
                var info = await Task.Run(() => backend == OutputBackend.CoreAudio
                    ? CoreAudioNativeDevice.Resolve(wasapiDevice.Id).GetCapabilities()
                    : WasapiDeviceProvider.GetCapabilities(wasapiDevice.Id));
                await new DeviceInfoWindow(info).ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = SelectedBackend == OutputBackend.CoreAudio
                ? LocalizationManager.Current.CoreAudioFailed
                : string.Format(LocalizationManager.Current.DeviceInfoFailed, ex.Message);
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Confirm();

    private async Task LoadDevicesAsync()
    {
        var loadVersion = Interlocked.Increment(ref _deviceLoadVersion);
        var backend = SelectedBackend;
        CoreAudioOptionsPanel.IsVisible = backend == OutputBackend.CoreAudio;
        DeviceComboBox.IsEnabled = false;
        DeviceInfoButton.IsEnabled = false;
        DeviceInfoButton.IsVisible = false;
        StatusTextBlock.Text = LocalizationManager.Current.OutputDevicesLoading;
        UpdateSaveButton();
        try
        {
            await _deviceLoadGate.WaitAsync();
            if (loadVersion != Volatile.Read(ref _deviceLoadVersion))
                return;

            if (backend == OutputBackend.CoreAudio)
            {
                var devices = (await EnumeratePcmDevicesAsync(backend)).ToArray();
                if (loadVersion != Volatile.Read(ref _deviceLoadVersion)) return;
                DeviceComboBox.ItemsSource = devices;
                DeviceComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(WasapiDeviceInfo.Name));
                DeviceComboBox.SelectedItem = devices.FirstOrDefault(device =>
                    device.Id == _initialProfile?.SelectedWasapiDeviceId) ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = LocalizationManager.Current.CoreAudioOutputDevice;
                StatusTextBlock.Text = devices.Length == 0 ? LocalizationManager.Current.CoreAudioNoDevices : string.Empty;
            }
            else if (backend == OutputBackend.Wasapi)
            {
                var devices = (await EnumeratePcmDevicesAsync(backend))
                    .Where(device =>
                        SelectedLinuxDeviceKind == LinuxDeviceKind.Any ||
                        ResolveLinuxDeviceKind(device.Id) == SelectedLinuxDeviceKind)
                    .ToArray();
                if (loadVersion != Volatile.Read(ref _deviceLoadVersion))
                    return;
                DeviceComboBox.ItemsSource = devices;
                DeviceComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(WasapiDeviceInfo.Name));
                DeviceComboBox.SelectedItem = devices.FirstOrDefault(d =>
                    string.Equals(d.Id, _initialProfile?.SelectedWasapiDeviceId, StringComparison.Ordinal))
                    ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = !OperatingSystem.IsWindows() || _isMacOS
                    ? SelectedLinuxDeviceKind == LinuxDeviceKind.DirectAlsa
                        ? LocalizationManager.Current.AlsaOutputDevice
                        : LocalizationManager.Current.OpenAlOutputDevice
                    : LocalizationManager.Current.WasapiOutputDevice;
                StatusTextBlock.Text = devices.Length == 0
                    ? LocalizationManager.Current.NoWasapiDevices
                    : string.Empty;
            }
            else if (backend == OutputBackend.AirPlay)
            {
                var devices = (await AirPlayDeviceDiscovery.DiscoverAsync(forceRefresh: true)).ToList();
                if (devices.Count == 0 &&
                    !string.IsNullOrWhiteSpace(_initialProfile?.SelectedAirPlayDeviceId) &&
                    !string.IsNullOrWhiteSpace(_initialProfile.SelectedAirPlayHost) &&
                    _initialProfile.SelectedAirPlayPort > 0)
                {
                    devices.Add(new AirPlayDeviceInfo(
                        _initialProfile.SelectedAirPlayDeviceId,
                        _initialProfile.SelectedAirPlayDeviceName ?? _initialProfile.SelectedAirPlayDeviceId,
                        _initialProfile.SelectedAirPlayHost,
                        _initialProfile.SelectedAirPlayPort,
                        new Dictionary<string, string>()));
                }
                if (loadVersion != Volatile.Read(ref _deviceLoadVersion))
                    return;
                DeviceComboBox.ItemsSource = devices;
                DeviceComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(AirPlayDeviceInfo.Name));
                DeviceComboBox.SelectedItem = devices.FirstOrDefault(device =>
                    string.Equals(device.Id, _initialProfile?.SelectedAirPlayDeviceId, StringComparison.OrdinalIgnoreCase))
                    ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = LocalizationManager.Current.AirPlayOutputDevice;
                StatusTextBlock.Text = devices.Count == 0
                    ? LocalizationManager.Current.NoAirPlayDevices
                    : AirPlayAudioPlayer.IsSenderAvailable
                        ? string.Empty
                        : LocalizationManager.Current.AirPlaySenderMissing;
            }
            else if (backend is OutputBackend.Asio or OutputBackend.CwAsio)
            {
                var drivers = await Task.Run(() => SteinbergAsioStream.GetDriverNames(backend));
                if (loadVersion != Volatile.Read(ref _deviceLoadVersion))
                    return;
                DeviceComboBox.ItemsSource = drivers;
                DeviceComboBox.DisplayMemberBinding = null;
                DeviceComboBox.SelectedItem = drivers.FirstOrDefault(n =>
                    string.Equals(n, _initialProfile?.SelectedDriverName, StringComparison.Ordinal))
                    ?? drivers.FirstOrDefault();
                DeviceLabelTextBlock.Text = backend == OutputBackend.CwAsio
                    ? LocalizationManager.Current.CwAsioOutputDevice
                    : LocalizationManager.Current.AsioOutputDevice;
                StatusTextBlock.Text = drivers.Count == 0
                    ? LocalizationManager.Current.NoAsioDrivers
                    : string.Empty;
            }
            else
            {
                DeviceComboBox.ItemsSource = null;
                DeviceComboBox.DisplayMemberBinding = null;
                DeviceLabelTextBlock.Text = LocalizationManager.Current.OutputDevice;
                StatusTextBlock.Text = LocalizationManager.Current.KernelStreamingUnavailable;
            }
        }
        catch (DllNotFoundException)
        {
            if (loadVersion == Volatile.Read(ref _deviceLoadVersion))
                StatusTextBlock.Text = backend == OutputBackend.CoreAudio
                    ? LocalizationManager.Current.CoreAudioFailed : LocalizationManager.Current.AsioBridgeMissing;
        }
        catch
        {
            if (loadVersion == Volatile.Read(ref _deviceLoadVersion))
                StatusTextBlock.Text = backend switch
                {
                    OutputBackend.Wasapi => LocalizationManager.Current.NoWasapiDevices,
                    OutputBackend.AirPlay => LocalizationManager.Current.NoAirPlayDevices,
                    OutputBackend.CoreAudio => LocalizationManager.Current.CoreAudioFailed,
                    _ => LocalizationManager.Current.NoAsioDrivers
                };
        }
        finally
        {
            if (_deviceLoadGate.CurrentCount == 0)
                _deviceLoadGate.Release();
            if (loadVersion == Volatile.Read(ref _deviceLoadVersion))
            {
                DeviceComboBox.IsEnabled = true;
                DeviceInfoButton.IsEnabled = DeviceComboBox.SelectedItem is string or WasapiDeviceInfo;
                DeviceInfoButton.IsVisible = DeviceInfoButton.IsEnabled;
                UpdateSaveButton();
            }
        }
    }

    private bool CanSave()
    {
        var name = NameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
            return false;
        if (_existingNames.Contains(name))
            return false;
        if (DeviceComboBox.SelectedItem is null &&
            SelectedBackend != OutputBackend.KernelStreaming)
            return false;
        return true;
    }

    private Task<IReadOnlyList<WasapiDeviceInfo>> EnumeratePcmDevicesAsync(OutputBackend backend) => Task.Run(() =>
        _enumeratePcmDevices is not null ? _enumeratePcmDevices(backend) : backend == OutputBackend.CoreAudio
            ? (IReadOnlyList<WasapiDeviceInfo>)CoreAudioNativeDevice.GetOutputDevices()
                .Select(device => new WasapiDeviceInfo(device.Uid, device.Name)).ToArray()
            : WasapiDeviceProvider.GetRenderDevices());

    private void UpdateSaveButton()
    {
        var name = NameTextBox.Text?.Trim();
        var duplicate = !string.IsNullOrEmpty(name) && _existingNames.Contains(name);
        ValidationTextBlock.Text = duplicate ? LocalizationManager.Current.OutputProfileNameExists : string.Empty;
        SaveButton.IsEnabled = CanSave();
    }

    private void Confirm()
    {
        if (!CanSave())
            return;
        var backend = SelectedBackend;
        var airPlayDevice = DeviceComboBox.SelectedItem as AirPlayDeviceInfo;
        Result = new OutputProfile
        {
            Name = NameTextBox.Text!.Trim(),
            Backend = backend,
            SelectedDriverName = DeviceComboBox.SelectedItem as string,
            SelectedWasapiDeviceId = DeviceComboBox.SelectedItem is WasapiDeviceInfo w ? w.Id : null,
            SelectedWasapiDeviceName = DeviceComboBox.SelectedItem is WasapiDeviceInfo wd ? wd.Name : null,
            CoreAudioMatchRate = CoreAudioMatchRateCheckBox.IsChecked == true,
            CoreAudioExclusive = CoreAudioExclusiveCheckBox.IsChecked == true,
            CoreAudioRestoreRate = CoreAudioRestoreRateCheckBox.IsChecked == true,
            CoreAudioBypass = CoreAudioBypassCheckBox.IsChecked == true,
            SelectedAirPlayDeviceId = airPlayDevice?.Id,
            SelectedAirPlayDeviceName = airPlayDevice?.Name,
            SelectedAirPlayHost = airPlayDevice?.Host,
            SelectedAirPlayPort = airPlayDevice?.Port ?? 0
        };
        Close(true);
    }
}
