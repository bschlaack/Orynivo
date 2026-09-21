using System.Text.Json;
using Orynivo.Visualization;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Measures how the shaders of a real preset collection fare on the GPU path: how many translate to
/// SkSL and are accepted by Skia, and which constructs the rest fail on. It is the gate for the GPU
/// visualizer, because a shader that cannot be translated stays on the CPU interpreter and keeps its
/// cost, so the share that translates decides whether Skia carries the collection.
/// </summary>
public sealed class PresetTranspileDiagnosticTests
{
    private const int FileCount = 500;
    private const int MaxReported = 12;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic test.</summary>
    /// <param name="output">Test output writer.</param>
    public PresetTranspileDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Translates every shader of a sample of the configured folder and reports the failures.</summary>
    [Fact]
    public void Report_ShaderTranslationOverARealCollection()
    {
        var folder = ResolveFolder();
        if (folder is null)
        {
            _output.WriteLine("no preset folder configured; nothing to report");
            return;
        }

        var shaders = 0;
        var compiled = 0;
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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

                foreach (var shader in preset.WarpShaders.Concat(preset.CompShaders))
                {
                    shaders++;
                    var reason = TryTranslate(shader);
                    if (reason is null)
                    {
                        compiled++;
                        continue;
                    }

                    var key = Normalize(reason);
                    failures[key] = failures.TryGetValue(key, out var count) ? count + 1 : 1;
                    if (!examples.TryGetValue(key, out var list))
                    {
                        list = [];
                        examples[key] = list;
                    }

                    if (list.Count < 3)
                        list.Add(Path.GetFileName(file));
                }
            }
        }

        _output.WriteLine($"shaders sampled: {shaders}");
        _output.WriteLine($"translated and accepted by Skia: {compiled}");
        _output.WriteLine($"failures: {shaders - compiled}");
        _output.WriteLine("top reasons:");
        foreach (var (reason, count) in failures.OrderByDescending(pair => pair.Value).Take(MaxReported))
        {
            _output.WriteLine($"{count,6}  {reason}");
            if (examples.TryGetValue(reason, out var list))
                _output.WriteLine($"        e.g. {string.Join(", ", list)}");
        }
    }

    /// <summary>Translates one shader and reports why it failed, or nothing when it worked.</summary>
    /// <param name="shader">Shader to translate.</param>
    /// <returns>The failure reason, or <see langword="null"/>.</returns>
    private static string? TryTranslate(VisualizerShader shader)
    {
        try
        {
            var sksl = ShaderTranspiler.Transpile(shader.Program);
            using var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
            return effect is null ? "SkSL rejected: " + FirstError(errors) : null;
        }
        catch (PresetExpressionException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>Keeps the first Skia error line, which carries the reason.</summary>
    /// <param name="errors">Skia error text.</param>
    /// <returns>The first line, or an empty string.</returns>
    private static string FirstError(string? errors)
    {
        if (string.IsNullOrWhiteSpace(errors))
            return string.Empty;

        var line = errors.Split('\n').FirstOrDefault(text => text.Contains("error:", StringComparison.Ordinal));
        return (line ?? errors).Trim();
    }

    /// <summary>Trims a message to its stable part so equal failures group together.</summary>
    /// <param name="message">Failure message.</param>
    /// <returns>The normalized message.</returns>
    private static string Normalize(string message)
    {
        var index = message.IndexOf(" (at position", StringComparison.Ordinal);
        return index > 0 ? message[..index] : message;
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
}
