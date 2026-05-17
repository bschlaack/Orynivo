using System.Windows;
using Player.Audio;

namespace Player;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        OutputBackendComboBox.ItemsSource = Enum.GetValues<OutputBackend>();
        OutputBackendComboBox.SelectedItem = settings.OutputBackend;
        LoadDrivers();
    }

    public string? SelectedDriverName => DriverComboBox.SelectedItem as string;
    public string? SelectedWasapiDeviceId =>
        DriverComboBox.SelectedItem is WasapiDeviceInfo device ? device.Id : null;
    public string? SelectedWasapiDeviceName =>
        DriverComboBox.SelectedItem is WasapiDeviceInfo device ? device.Name : null;
    public OutputBackend SelectedOutputBackend =>
        OutputBackendComboBox.SelectedItem is OutputBackend backend
            ? backend
            : OutputBackend.Asio;

    private void LoadDrivers()
    {
        try
        {
            if (SelectedOutputBackend == OutputBackend.Wasapi)
            {
                var devices = WasapiDeviceProvider.GetRenderDevices();
                DriverComboBox.ItemsSource = devices;
                DriverComboBox.DisplayMemberPath = nameof(WasapiDeviceInfo.Name);
                DriverComboBox.SelectedItem = devices.FirstOrDefault(device =>
                    string.Equals(device.Id, _settings.SelectedWasapiDeviceId, StringComparison.Ordinal))
                    ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = "WASAPI-Ausgabegerät";
                StatusTextBlock.Text = devices.Count == 0
                    ? "Keine aktiven WASAPI-Ausgabegeräte gefunden."
                    : "Gerät auswählen und speichern.";
            }
            else
            {
                var drivers = SteinbergAsioStream.GetDriverNames();
                DriverComboBox.ItemsSource = drivers;
                DriverComboBox.DisplayMemberPath = string.Empty;
                DriverComboBox.SelectedItem = drivers.FirstOrDefault(name =>
                    string.Equals(name, _settings.SelectedDriverName, StringComparison.Ordinal))
                    ?? drivers.FirstOrDefault(name =>
                        name.Contains("TOPPING", StringComparison.OrdinalIgnoreCase))
                    ?? drivers.FirstOrDefault();
                DeviceLabelTextBlock.Text = SelectedOutputBackend == OutputBackend.Asio
                    ? "ASIO-Ausgabegerät"
                    : "Ausgabegerät";
                StatusTextBlock.Text = drivers.Count == 0
                    ? "Keine ASIO-Treiber gefunden."
                    : "Gerät auswählen und speichern.";
            }
        }
        catch (DllNotFoundException)
        {
            StatusTextBlock.Text = "AsioBridge.dll fehlt. Bitte zuerst build.ps1 ausführen.";
        }
    }

    private void DriverComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        DeviceInfoButton.Visibility = DriverComboBox.SelectedItem is string or WasapiDeviceInfo
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OutputBackendComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var backend = SelectedOutputBackend;
        DriverComboBox.IsEnabled = backend != OutputBackend.KernelStreaming;
        DeviceInfoButton.IsEnabled = backend is OutputBackend.Asio or OutputBackend.Wasapi;
        LoadDrivers();
        if (backend == OutputBackend.KernelStreaming)
        {
            DriverComboBox.ItemsSource = null;
            StatusTextBlock.Text = "KernelStreaming ist auswählbar, aber noch nicht als Wiedergabe-Backend implementiert.";
        }
    }

    private void DeviceInfoButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DriverComboBox.SelectedItem is string driverName)
            {
                var info = SteinbergAsioStream.GetDeviceInfo(driverName);
                new DeviceInfoWindow(info) { Owner = this }.ShowDialog();
            }
            else if (DriverComboBox.SelectedItem is WasapiDeviceInfo wasapiDevice)
            {
                var info = WasapiDeviceProvider.GetCapabilities(wasapiDevice.Id);
                new DeviceInfoWindow(info) { Owner = this }.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Geräteinfo konnte nicht gelesen werden: {ex.Message}";
        }
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
