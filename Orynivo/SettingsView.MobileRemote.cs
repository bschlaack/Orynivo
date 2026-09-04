using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Media.Imaging;
using QRCoder;

namespace Orynivo;

/// <summary>Provides LAN address selection and locally rendered credential QR codes.</summary>
internal partial class SettingsView
{
    private Bitmap? _mobileRemoteQr;
    private bool _mobileRemoteClosed;

    private async void InitializeMobileRemoteAddresses()
    {
        MobileRemoteAddressComboBox.SelectionChanged += (_, _) => UpdateMobileRemoteStatus();
        MobileRemoteAccessTokenTextBox.TextChanged += (_, _) => UpdateMobileRemoteStatus();
        var addresses = await Task.Run(() =>
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
                    .OrderByDescending(n => n.GetIPProperties().GatewayAddresses
                        .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                    .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                    .Select(a => a.Address)
                    .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a) &&
                        !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    .Select(a => a.ToString()).Distinct().ToArray();
            }
            catch (NetworkInformationException) { return Array.Empty<string>(); }
        });
        if (_mobileRemoteClosed) return;
        MobileRemoteAddressComboBox.ItemsSource = addresses;
        MobileRemoteAddressComboBox.SelectedIndex = addresses.Length > 0 ? 0 : -1;
        UpdateMobileRemoteStatus();
    }

    private void UpdateMobileRemoteQr(int port, bool enabled)
    {
        var address = MobileRemoteAddressComboBox.SelectedItem as string;
        var url = $"http://{address ?? "localhost"}:{port}/remote";
        MobileRemoteAddressTextBlock.Text = url;
        MobileRemoteQrImage.Source = null;
        MobileRemoteQrImage.IsVisible = false;
        _mobileRemoteQr?.Dispose();
        _mobileRemoteQr = null;
        var token = MobileRemoteAccessTokenTextBox.Text?.Trim();
        if (_mobileRemoteClosed || !enabled || address is null || string.IsNullOrEmpty(token) || token.Length > 512)
            return;
        // Generate locally; the credential must never reach an external QR service or a file.
        var payload = url + "#token=" + Uri.EscapeDataString(token);
        try
        {
            using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            using var qr = new PngByteQRCode(data);
            using var stream = new System.IO.MemoryStream(qr.GetGraphic(6));
            _mobileRemoteQr = new Bitmap(stream);
            MobileRemoteQrImage.Source = _mobileRemoteQr;
            MobileRemoteQrImage.IsVisible = true;
        }
        catch (QRCoder.Exceptions.DataTooLongException)
        {
            // A manually entered oversized token must not crash the settings screen.
        }
    }
}
