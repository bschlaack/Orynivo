using System.Windows;
using System.Windows.Controls;
using Orynivo.Audio;
using Orynivo.Localization;
using System.Runtime.InteropServices;
using WpfApplication = System.Windows.Application;

namespace Orynivo;

public partial class DeviceInfoWindow : Window
{
    public DeviceInfoWindow(AsioDeviceInfo info)
    {
        InitializeComponent();

        DriverNameTextBlock.Text = info.DriverName;
        SummaryTextBlock.Text =
            string.Format(LocalizationManager.Current.DeviceChannelSummary, info.OutputChannels, info.InputChannels) + "\n" +
            string.Format(LocalizationManager.Current.DeviceBufferSummary, info.MinBufferSize, info.PreferredBufferSize, info.MaxBufferSize, info.BufferGranularity);

        PcmSampleRatesTextBlock.Text = info.SupportedPcmSampleRates.Count == 0
            ? LocalizationManager.Current.DriverProvidedNoInformation
            : string.Join(" · ", info.SupportedPcmSampleRates.Select(FormatPcmRate));

        foreach (var dsdRate in new[] { 64, 128, 256, 512, 1024 })
        {
            var sampleRate = dsdRate * 44_100;
            var supported = info.SupportedDsdSampleRates.Contains(sampleRate);
            DsdRatesPanel.Children.Add(new TextBlock
            {
                Text = dsdRate.ToString(),
                Margin = new Thickness(0, 0, 12, 0),
                Foreground = (System.Windows.Media.Brush)FindResource(
                    supported ? "AppDsdSupportedBrush" : "AppDsdUnsupportedBrush")
            });
        }

        PcmFormatsTextBlock.Text = info.PcmOutputFormats.Count == 0
            ? LocalizationManager.Current.DriverProvidedNoInformation
            : string.Join(Environment.NewLine, info.PcmOutputFormats.Distinct().Select(DescribeFormat));

        DsdFormatsTextBlock.Text = info.SupportsDsd
            ? info.DsdOutputFormats.Count == 0
                ? LocalizationManager.Current.DsdSupportedWithoutFormats
                : string.Join(Environment.NewLine, info.DsdOutputFormats.Distinct().Select(DescribeFormat))
            : info.DsdProbeWasConclusive
                ? LocalizationManager.Current.Unsupported
                : LocalizationManager.Current.DeviceProbeInconclusive;
    }

    private void DeviceInfoWindow_OnSourceInitialized(object sender, EventArgs e)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var dark = WpfApplication.Current.Resources["AppHeaderBrush"] is System.Windows.Media.SolidColorBrush b &&
                       b.Color == System.Windows.Media.Color.FromRgb(0x13, 0x14, 0x2A);
            var captionColor = dark ? ColorRef(0x13, 0x14, 0x2A) : ColorRef(0xEA, 0xEA, 0xF5);
            var textColor = dark ? ColorRef(0xFF, 0xFF, 0xFF) : ColorRef(0x13, 0x14, 0x2A);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch { }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    public DeviceInfoWindow(WasapiDeviceCapabilities info)
    {
        InitializeComponent();

        DriverNameTextBlock.Text = info.Name;
        SummaryTextBlock.Text = string.Format(
            LocalizationManager.Current.WasapiEndpointSummary,
            info.MixFormatChannels,
            FormatPcmRate(info.MixFormatSampleRate),
            info.MixFormatBitsPerSample);

        PcmSampleRatesTextBlock.Text = info.ExclusivePcmSampleRates.Count == 0
            ? LocalizationManager.Current.WasapiNoExclusiveFormats
            : string.Join(" · ", info.ExclusivePcmSampleRates.Select(FormatPcmRate));

        DsdRatesPanel.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.WasapiDsdNotRelevant,
            Foreground = (System.Windows.Media.Brush)FindResource("AppDsdUnsupportedBrush")
        });

        PcmFormatsTextBlock.Text = info.ExclusivePcmFormats.Count == 0
            ? LocalizationManager.Current.WasapiNoExclusiveFormats
            : string.Join(Environment.NewLine, info.ExclusivePcmFormats);

        DsdFormatsTextBlock.Text = LocalizationManager.Current.NativeDsdUsesAsio;
    }

    private static string DescribeFormat(string format) =>
        format switch
        {
            "Int16LSB" => string.Format(LocalizationManager.Current.PcmIntegerFormat, 16, format),
            "Int24LSB" => string.Format(LocalizationManager.Current.PcmIntegerFormat, 24, format),
            "Int32LSB" => string.Format(LocalizationManager.Current.PcmIntegerFormat, 32, format),
            "Int32LSB16" => string.Format(LocalizationManager.Current.PcmContainerFormat, 16, 32, format),
            "Int32LSB18" => string.Format(LocalizationManager.Current.PcmContainerFormat, 18, 32, format),
            "Int32LSB20" => string.Format(LocalizationManager.Current.PcmContainerFormat, 20, 32, format),
            "Int32LSB24" => string.Format(LocalizationManager.Current.PcmContainerFormat, 24, 32, format),
            "Float32LSB" => string.Format(LocalizationManager.Current.PcmFloatFormat, 32, format),
            "Float64LSB" => string.Format(LocalizationManager.Current.PcmFloatFormat, 64, format),
            "DSDInt8LSB1" => string.Format(LocalizationManager.Current.NativeDsdLsbFormat, format),
            "DSDInt8MSB1" => string.Format(LocalizationManager.Current.NativeDsdMsbFormat, format),
            "DSDInt8NER8" => string.Format(LocalizationManager.Current.NativeDsdWordFormat, format),
            _ => format
        };

    private static string FormatPcmRate(int rate) =>
        rate % 1000 == 0
            ? $"{rate / 1000:N0} kHz"
            : $"{rate / 1000d:N1} kHz";
}
