using System.Windows;
using System.Windows.Controls;
using Player.Audio;

namespace Player;

public partial class DeviceInfoWindow : Window
{
    public DeviceInfoWindow(AsioDeviceInfo info)
    {
        InitializeComponent();

        DriverNameTextBlock.Text = info.DriverName;
        SummaryTextBlock.Text =
            $"{info.OutputChannels} Ausgangskanal/Kanäle · {info.InputChannels} Eingangskanal/Kanäle\n" +
            $"Buffer: min {info.MinBufferSize}, bevorzugt {info.PreferredBufferSize}, max {info.MaxBufferSize}, Granularität {info.BufferGranularity}";

        PcmSampleRatesTextBlock.Text = info.SupportedPcmSampleRates.Count == 0
            ? "Keine Angaben vom Treiber."
            : string.Join(" · ", info.SupportedPcmSampleRates.Select(FormatPcmRate));

        foreach (var dsdRate in new[] { 64, 128, 256, 512, 1024 })
        {
            var sampleRate = dsdRate * 44_100;
            var supported = info.SupportedDsdSampleRates.Contains(sampleRate);
            DsdRatesPanel.Children.Add(new TextBlock
            {
                Text = dsdRate.ToString(),
                Margin = new Thickness(0, 0, 12, 0),
                Foreground = supported ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.LightGray
            });
        }

        PcmFormatsTextBlock.Text = info.PcmOutputFormats.Count == 0
            ? "Keine Angaben vom Treiber."
            : string.Join(Environment.NewLine, info.PcmOutputFormats.Distinct().Select(DescribeFormat));

        DsdFormatsTextBlock.Text = info.SupportsDsd
            ? info.DsdOutputFormats.Count == 0
                ? "DSD-Modus unterstützt; keine konkreten Kanalformate gemeldet."
                : string.Join(Environment.NewLine, info.DsdOutputFormats.Distinct().Select(DescribeFormat))
            : info.DsdProbeWasConclusive
                ? "Nicht unterstützt."
                : "Konnte nicht eindeutig geprüft werden — Gerät wird möglicherweise von einer anderen Anwendung verwendet.";
    }

    public DeviceInfoWindow(WasapiDeviceCapabilities info)
    {
        InitializeComponent();

        DriverNameTextBlock.Text = info.Name;
        SummaryTextBlock.Text =
            $"WASAPI-Endpunkt · {info.MixFormatChannels} Kanal/Kanäle\n" +
            $"Mix-Format: {FormatPcmRate(info.MixFormatSampleRate)} · {info.MixFormatBitsPerSample} Bit";

        PcmSampleRatesTextBlock.Text = info.ExclusivePcmSampleRates.Count == 0
            ? "Keine exklusiven PCM-Formate erkannt."
            : string.Join(" · ", info.ExclusivePcmSampleRates.Select(FormatPcmRate));

        DsdRatesPanel.Children.Add(new TextBlock
        {
            Text = "Nicht relevant für WASAPI in diesem Player.",
            Foreground = System.Windows.Media.Brushes.Gray
        });

        PcmFormatsTextBlock.Text = info.ExclusivePcmFormats.Count == 0
            ? "Keine exklusiven PCM-Formate erkannt."
            : string.Join(Environment.NewLine, info.ExclusivePcmFormats);

        DsdFormatsTextBlock.Text = "Native DSD-Wiedergabe läuft in diesem Player über ASIO.";
    }

    private static string DescribeFormat(string format) =>
        format switch
        {
            "Int16LSB" => "16-Bit PCM, Little Endian (Int16LSB)",
            "Int24LSB" => "24-Bit PCM, Little Endian (Int24LSB)",
            "Int32LSB" => "32-Bit PCM, Little Endian (Int32LSB)",
            "Int32LSB16" => "16-Bit PCM in 32-Bit-Container, Little Endian (Int32LSB16)",
            "Int32LSB18" => "18-Bit PCM in 32-Bit-Container, Little Endian (Int32LSB18)",
            "Int32LSB20" => "20-Bit PCM in 32-Bit-Container, Little Endian (Int32LSB20)",
            "Int32LSB24" => "24-Bit PCM in 32-Bit-Container, Little Endian (Int32LSB24)",
            "Float32LSB" => "32-Bit Float PCM, Little Endian (Float32LSB)",
            "Float64LSB" => "64-Bit Float PCM, Little Endian (Float64LSB)",
            "DSDInt8LSB1" => "Natives DSD, 1-Bit-Daten, erste Probe im niederwertigsten Bit (DSDInt8LSB1)",
            "DSDInt8MSB1" => "Natives DSD, 1-Bit-Daten, erste Probe im höchstwertigen Bit (DSDInt8MSB1)",
            "DSDInt8NER8" => "Natives DSD, 8-Bit-Wörter ohne Endian-Relevanz (DSDInt8NER8)",
            _ => format
        };

    private static string FormatPcmRate(int rate) =>
        rate % 1000 == 0
            ? $"{rate / 1000:N0} kHz"
            : $"{rate / 1000d:N1} kHz";
}
