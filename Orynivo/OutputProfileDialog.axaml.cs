using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Audio;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Dialog for creating or editing a named audio output profile.</summary>
internal partial class OutputProfileDialog : Window
{
    private sealed record BackendChoice(OutputBackend Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly HashSet<string> _existingNames;
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
    {
        InitializeComponent();
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var isEditing = profile is not null;
        Title = isEditing
            ? LocalizationManager.Current.OutputProfileConfigureTitle
            : LocalizationManager.Current.OutputProfileCreateTitle;

        var availableBackends = new[]
        {
            new BackendChoice(OutputBackend.Asio, LocalizationManager.Current.SteinbergAsio),
            new BackendChoice(OutputBackend.CwAsio, LocalizationManager.Current.CwAsio),
            new BackendChoice(OutputBackend.Wasapi, "WASAPI")
        }
            .Where(c => c.Value != OutputBackend.Asio || SteinbergAsioStream.IsAvailable)
            .Where(c => c.Value != OutputBackend.CwAsio || SteinbergAsioStream.IsCwAsioAvailable)
            .ToArray();
        BackendComboBox.ItemsSource = availableBackends;

        if (profile is not null)
        {
            NameTextBox.Text = profile.Name;
            BackendComboBox.SelectedItem =
                availableBackends.FirstOrDefault(c => c.Value == profile.Backend)
                ?? availableBackends.FirstOrDefault(c => c.Value == OutputBackend.Wasapi)
                ?? availableBackends.FirstOrDefault();
            _initialProfile = profile;
        }
        else
        {
            BackendComboBox.SelectedItem =
                availableBackends.FirstOrDefault(c => c.Value == OutputBackend.Wasapi)
                ?? availableBackends.FirstOrDefault();
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

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateSaveButton();

    private void NameTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && CanSave())
            Confirm();
    }

    private void BackendComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        _ = LoadDevicesAsync();
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
                var info = WasapiDeviceProvider.GetCapabilities(wasapiDevice.Id);
                await new DeviceInfoWindow(info).ShowDialog(this);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.DeviceInfoFailed, ex.Message);
        }
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e) => Confirm();

    private async Task LoadDevicesAsync()
    {
        var loadVersion = Interlocked.Increment(ref _deviceLoadVersion);
        var backend = SelectedBackend;
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

            if (backend == OutputBackend.Wasapi)
            {
                var devices = await Task.Run(WasapiDeviceProvider.GetRenderDevices);
                if (loadVersion != Volatile.Read(ref _deviceLoadVersion))
                    return;
                DeviceComboBox.ItemsSource = devices;
                DeviceComboBox.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(WasapiDeviceInfo.Name));
                DeviceComboBox.SelectedItem = devices.FirstOrDefault(d =>
                    string.Equals(d.Id, _initialProfile?.SelectedWasapiDeviceId, StringComparison.Ordinal))
                    ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = LocalizationManager.Current.WasapiOutputDevice;
                StatusTextBlock.Text = devices.Count == 0
                    ? LocalizationManager.Current.NoWasapiDevices
                    : string.Empty;
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
                StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
        }
        catch
        {
            if (loadVersion == Volatile.Read(ref _deviceLoadVersion))
                StatusTextBlock.Text = backend == OutputBackend.Wasapi
                    ? LocalizationManager.Current.NoWasapiDevices
                    : LocalizationManager.Current.NoAsioDrivers;
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
        Result = new OutputProfile
        {
            Name = NameTextBox.Text!.Trim(),
            Backend = backend,
            SelectedDriverName = DeviceComboBox.SelectedItem as string,
            SelectedWasapiDeviceId = DeviceComboBox.SelectedItem is WasapiDeviceInfo w ? w.Id : null,
            SelectedWasapiDeviceName = DeviceComboBox.SelectedItem is WasapiDeviceInfo wd ? wd.Name : null
        };
        Close(true);
    }
}
