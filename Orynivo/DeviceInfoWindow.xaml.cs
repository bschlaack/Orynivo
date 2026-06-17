using Avalonia.Controls;
using Avalonia.Media;
using Orynivo.Audio;
using Orynivo.Localization;
using AvaloniaApp = Avalonia.Application;

namespace Orynivo;

public partial class DeviceInfoWindow : Window
{
    /// <summary>
    /// Initializes a runtime-loader instance without device data.
    /// </summary>
    public DeviceInfoWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }

    public DeviceInfoWindow(AsioDeviceInfo info)
    {
        InitializeComponent();
        Opened += (_, _) => WindowChrome.ApplyTheme(this);

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
            var brushKey = supported ? "AppDsdSupportedBrush" : "AppDsdUnsupportedBrush";
            DsdRatesPanel.Children.Add(new TextBlock
            {
                Text = dsdRate.ToString(),
                Margin = new Avalonia.Thickness(0, 0, 12, 0),
                Foreground = AvaloniaApp.Current!.Resources[brushKey] as IBrush
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

    public DeviceInfoWindow(WasapiDeviceCapabilities info)
    {
        InitializeComponent();
        Opened += (_, _) => WindowChrome.ApplyTheme(this);

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
            Foreground = AvaloniaApp.Current!.Resources["AppDsdUnsupportedBrush"] as IBrush
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
