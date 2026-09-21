using System.Text.Json;
using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Renders a sample of a real preset collection twice, once with the Skia passes enabled and once
/// with the interpreter, and reports how far the two pictures drift. It is the validation harness
/// the cutover needs: a preset whose GPU picture differs from the interpreter by more than the
/// eight-bit quantisation is a real divergence, and this is what surfaces it before the GPU path
/// becomes the default.
/// </summary>
public sealed class PresetSkiaComparisonDiagnosticTests
{
    private const int FileCount = 200;
    private const int Frames = 3;
    private const int Width = 48;
    private const int Height = 27;
    private const int MaxReported = 12;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic test.</summary>
    /// <param name="output">Test output writer.</param>
    public PresetSkiaComparisonDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Compares both execution paths over a sample of the configured folder.</summary>
    [Fact]
    public void Report_SkiaAgainstTheInterpreterOverARealCollection()
    {
        var folder = ResolveFolder();
        if (folder is null)
        {
            _output.WriteLine("no preset folder configured; nothing to report");
            return;
        }

        var compared = 0;
        var skipped = 0;
        var exact = 0;
        var small = 0;
        var large = 0;
        var largeByKind = new Dictionary<string, int>(StringComparer.Ordinal);
        var worst = new List<(float Difference, string Name)>();
        foreach (var file in Directory
            .EnumerateFiles(folder, "*.milk", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Take(FileCount))
        {
            foreach (var section in VisualizerPreset.ParseSections(File.ReadAllText(file)))
            {
                VisualizerPreset preset;
                try
                {
                    preset = VisualizerPreset.Parse(section, Path.GetFileNameWithoutExtension(file));
                }
                catch (PresetExpressionException)
                {
                    continue;
                }

                float difference;
                try
                {
                    difference = Compare(preset);
                }
                catch (Exception)
                {
                    skipped++;
                    continue;
                }

                compared++;
                if (difference <= 1.5f / 255f)
                    exact++;
                else if (difference <= 8f / 255f)
                    small++;
                else
                    large++;

                var kind = preset.WarpShaders.Count > 0
                    ? "warp"
                    : preset.CompShaders.Count > 0
                        ? "comp"
                        : !preset.PerPixel.IsEmpty
                            ? "per_pixel"
                            : "plain";
                if (difference > 8f / 255f)
                    largeByKind[kind] = largeByKind.TryGetValue(kind, out var count) ? count + 1 : 1;
                worst.Add((difference, $"{kind,-9} {preset.Name}"));
            }
        }

        _output.WriteLine($"presets compared: {compared}   skipped: {skipped}");
        _output.WriteLine($"within 1.5/255: {exact}   within 8/255: {small}   above: {large}");
        _output.WriteLine("above 8/255 by kind:");
        foreach (var (kind, count) in largeByKind.OrderByDescending(pair => pair.Value))
            _output.WriteLine($"{count,6}  {kind}");
        _output.WriteLine("worst differences:");
        foreach (var (difference, name) in worst.OrderByDescending(pair => pair.Difference).Take(MaxReported))
            _output.WriteLine($"{difference * 255f,8:F2}/255  {name}");
    }

    /// <summary>Renders one preset both ways and returns the mean absolute difference.</summary>
    /// <param name="preset">Preset to render.</param>
    /// <returns>The mean difference per channel.</returns>
    private static float Compare(VisualizerPreset preset)
    {
        var cpu = Render(preset, skia: false);
        var gpu = Render(preset, skia: true);
        var total = 0f;
        var count = 0;
        for (var index = 0; index < Math.Min(cpu.Length, gpu.Length); index += 4)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                total += Math.Abs(cpu[index + channel] - gpu[index + channel]);
                count++;
            }
        }

        return total / Math.Max(1, count);
    }

    /// <summary>Renders a preset and returns its last frame.</summary>
    /// <param name="preset">Preset to render.</param>
    /// <param name="skia">Whether the Skia passes are enabled.</param>
    /// <returns>The last frame's pixels.</returns>
    private static float[] Render(VisualizerPreset preset, bool skia) =>
        Render(preset, skia, out _);

    /// <summary>Renders a preset and returns its last frame and the shader error, if any.</summary>
    /// <param name="preset">Preset to render.</param>
    /// <param name="skia">Whether the Skia passes are enabled.</param>
    /// <param name="error">Receives the renderer's shader error.</param>
    /// <returns>The last frame's pixels.</returns>
    private static float[] Render(VisualizerPreset preset, bool skia, out string? error)
    {
        var renderer = new PresetRenderer(preset, Width, Height)
        {
            ShaderTimeBudgetMilliseconds = 100_000d,
            WarpStageBudgetMilliseconds = 100_000d,
            UseSkiaPasses = skia
        };
        try
        {
            var audio = new DiagnosticAudio();
            for (var frame = 0; frame < Frames; frame++)
                renderer.RenderFrame(audio, 1d / 60d);
            error = renderer.ShaderError;
            return renderer.Output.Pixels.ToArray();
        }
        finally
        {
            renderer.Dispose();
        }
    }

    /// <summary>Reads the configured preset folder, or returns nothing when none is set.</summary>
    /// <returns>The folder path, or <see langword="null"/>.</returns>
    private static string? ResolveFolder()
    {
        var configured = Environment.GetEnvironmentVariable("ORYNIVO_PRESET_FOLDER");
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var settings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Orynivo",
            "settings.json");
        if (!File.Exists(settings))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(settings));
            if (document.RootElement.TryGetProperty("VisualizerPresetDirectory", out var element) &&
                element.ValueKind == JsonValueKind.String)
            {
                var path = element.GetString();
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    return path;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>A deterministic audio source with content on every band and a waveform.</summary>
    private sealed class DiagnosticAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.7f, 0.55f, 0.4f, 0.25f, 0.15f, 0.1f];
        private readonly float[] _waveform = Enumerable.Range(0, 256)
            .Select(index => MathF.Sin(index * 0.13f) * 0.6f)
            .ToArray();

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Bass => 0.7f;

        /// <inheritdoc/>
        public float Mid => 0.55f;

        /// <inheritdoc/>
        public float Treble => 0.4f;

        /// <inheritdoc/>
        public float Volume => 0.65f;
    }
}
