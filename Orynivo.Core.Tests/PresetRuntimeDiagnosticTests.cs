using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Core.Tests;

/// <summary>
/// Renders every preset of the configured folder for a few frames and reports the ones whose frame
/// failed at run time. A block that fails to parse costs a preset its motion, but a frame that throws
/// costs it the whole picture, so the loop budget and the warp budget are measured rather than
/// assumed. Set <c>ORYNIVO_PRESET_FOLDER</c> to a real collection to run it.
/// </summary>
public sealed class PresetRuntimeDiagnosticTests
{
    /// <summary>The number of files the report samples.</summary>
    private const int FileSample = 2000;

    /// <summary>The number of frames each preset renders.</summary>
    private const int Frames = 3;

    /// <summary>The render size, kept small because this walks a whole collection.</summary>
    private const int Width = 64;

    /// <summary>The render height, kept small because this walks a whole collection.</summary>
    private const int Height = 36;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic test.</summary>
    /// <param name="output">Test output writer.</param>
    public PresetRuntimeDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Renders every preset of the configured folder and reports the runtime failures.</summary>
    [Fact]
    public void Report_PresetFolderRuntimeFailures()
    {
        var folder = Environment.GetEnvironmentVariable("ORYNIVO_PRESET_FOLDER");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _output.WriteLine("no preset folder configured; nothing to report");
            return;
        }

        var files = Directory
            .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                Path.GetExtension(path).Equals(".milk", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".oryvis", StringComparison.OrdinalIgnoreCase))
            .Take(FileSample)
            .ToList();
        _output.WriteLine($"folder: {folder}");
        _output.WriteLine($"files sampled: {files.Count}");

        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var examples = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var rendered = 0;
        var failed = 0;
        var loopBudget = 0;
        foreach (var file in files)
        {
            VisualizerPreset preset;
            try
            {
                preset = VisualizerPreset.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
            }
            catch (Exception)
            {
                continue;
            }

            var renderer = new PresetRenderer(preset, Width, Height);
            try
            {
                for (var frame = 0; frame < Frames; frame++)
                {
                    renderer.RenderFrame(new Silent(), 1d / 60d);
                }
            }
            catch (Exception exception)
            {
                Record(failures, examples, $"{exception.GetType().Name}: {exception.Message}", file);
                failed++;
                continue;
            }

            rendered++;
            if (renderer.PresetError is not { } error)
            {
                continue;
            }

            if (error.Contains("looped too often", StringComparison.Ordinal))
            {
                loopBudget++;
            }

            Record(failures, examples, error, file);
            failed++;
        }

        _output.WriteLine(
            $"rendered: {rendered}   presets with a runtime error: {failed}   loop-budget errors: {loopBudget}");
        foreach (var (reason, count) in failures.OrderByDescending(pair => pair.Value).Take(10))
        {
            _output.WriteLine($"{count,5} x {reason}");
            foreach (var example in examples[reason].Take(3))
            {
                _output.WriteLine($"        {example}");
            }
        }
    }

    /// <summary>Adds one failure to the grouped report.</summary>
    /// <param name="failures">Failure counts by reason.</param>
    /// <param name="examples">Example file names by reason.</param>
    /// <param name="reason">Failure reason.</param>
    /// <param name="file">File the failure came from.</param>
    private static void Record(
        Dictionary<string, int> failures,
        Dictionary<string, List<string>> examples,
        string reason,
        string file)
    {
        var key = Normalize(reason);
        failures[key] = failures.TryGetValue(key, out var count) ? count + 1 : 1;
        if (!examples.TryGetValue(key, out var list))
        {
            list = [];
            examples[key] = list;
        }

        if (list.Count < 3)
        {
            list.Add(Path.GetFileName(file));
        }
    }

    /// <summary>Trims a message to its stable part so equal failures group together.</summary>
    /// <param name="message">Failure message.</param>
    /// <returns>The grouped key.</returns>
    private static string Normalize(string message)
    {
        var cut = message.IndexOf('(');
        return (cut > 0 ? message[..cut] : message).Trim();
    }

    /// <summary>An audio source that reports silence.</summary>
    private sealed class Silent : IVisualizerAudioSource
    {
        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => [];

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => [];

        /// <inheritdoc/>
        public float Bass => 0f;

        /// <inheritdoc/>
        public float Mid => 0f;

        /// <inheritdoc/>
        public float Treble => 0f;

        /// <inheritdoc/>
        public float Volume => 0f;
    }
}
