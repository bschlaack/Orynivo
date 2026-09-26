using System.Text.Json;
using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Reports how the presets of the configured folder actually parse, grouped by failure reason.
/// It is a diagnostic: it does nothing when no preset folder is configured, and it never asserts
/// on the outcome, because the contents of a user's preset collection are not a test fixture.
/// Run it with <c>--logger "console;verbosity=detailed"</c> to read the report.
/// </summary>
public sealed class PresetFolderDiagnosticTests
{
    private const int MaxReported = 12;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic test.</summary>
    /// <param name="output">Test output writer.</param>
    public PresetFolderDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Parses every preset of the configured folder and reports the failures.</summary>
    [Fact]
    public void Report_PresetFolderParseFailures()
    {
        var folder = ResolveFolder();
        if (folder is null)
        {
            _output.WriteLine("no preset folder configured; nothing to report");
            return;
        }

        var files = Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                Path.GetExtension(path).Equals(".milk", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".oryvis", StringComparison.OrdinalIgnoreCase))
            .Take(2000)
            .ToList();
        _output.WriteLine($"folder: {folder}");
        _output.WriteLine($"files sampled: {files.Count}");

        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var failedFiles = 0;
        var failedBlocks = 0;
        var shaderFailures = 0;
        var parsed = 0;
        foreach (var file in files)
        {
            try
            {
                var preset = VisualizerPreset.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
                parsed++;
                failedBlocks += preset.FailedBlocks.Count;
                foreach (var block in preset.FailedBlocks)
                {
                    if (block.StartsWith("warp_", StringComparison.Ordinal) ||
                        block.StartsWith("comp_", StringComparison.Ordinal))
                    {
                        shaderFailures++;
                    }

                    var key = Normalize(block);
                    failures[key] = failures.TryGetValue(key, out var count) ? count + 1 : 1;
                    if (!examples.TryGetValue(key, out var list))
                    {
                        list = [];
                        examples[key] = list;
                    }

                    if (list.Count < 4)
                        list.Add(Path.GetFileName(file));
                }
            }
            catch (PresetExpressionException exception)
            {
                failedFiles++;
                var key = "WHOLE FILE " + Normalize(exception.Message);
                failures[key] = failures.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        _output.WriteLine($"parsed: {parsed}   whole-file failures: {failedFiles}   expression-block failures: {failedBlocks}   shader-slot failures: {shaderFailures}");
        _output.WriteLine("top failure reasons:");
        foreach (var (reason, count) in failures.OrderByDescending(pair => pair.Value).Take(MaxReported))
        {
            _output.WriteLine($"{count,6}  {reason}");
            if (examples.TryGetValue(reason, out var list))
                _output.WriteLine($"        e.g. {string.Join(", ", list)}");
        }
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
