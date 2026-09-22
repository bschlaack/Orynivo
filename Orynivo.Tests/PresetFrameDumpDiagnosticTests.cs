using Orynivo.Visualization;
using Xunit;
using Xunit.Abstractions;

namespace Orynivo.Tests;

/// <summary>
/// Diagnostic that renders one preset with the CPU renderer and writes the frames as BMP files, so
/// they can be compared against the reference implementation's render of the same preset. It does
/// nothing unless <c>ORYNIVO_PRESET_DUMP_FILE</c> and <c>ORYNIVO_PRESET_DUMP_DIR</c> are set, because
/// a render is not a test fixture. Run it with
/// <c>--logger "console;verbosity=detailed"</c> to read the report.
/// </summary>
public sealed class PresetFrameDumpDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the diagnostic.</summary>
    /// <param name="output">Test output writer.</param>
    public PresetFrameDumpDiagnosticTests(ITestOutputHelper output) => _output = output;

    /// <summary>Renders the configured preset and writes the frames.</summary>
    [Fact]
    public void Dump_ConfiguredPresetFrames()
    {
        var file = Environment.GetEnvironmentVariable("ORYNIVO_PRESET_DUMP_FILE");
        var directory = Environment.GetEnvironmentVariable("ORYNIVO_PRESET_DUMP_DIR");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file) || string.IsNullOrWhiteSpace(directory))
        {
            _output.WriteLine("no preset dump configured; nothing to do");
            return;
        }

        var width = ReadInt("ORYNIVO_PRESET_DUMP_WIDTH", 320);
        var height = ReadInt("ORYNIVO_PRESET_DUMP_HEIGHT", 180);
        var frames = ReadInt("ORYNIVO_PRESET_DUMP_FRAMES", 4);
        var fps = ReadInt("ORYNIVO_PRESET_DUMP_FPS", 60);
        var mesh = ReadFlag("ORYNIVO_PRESET_DUMP_MESH", true);
        Directory.CreateDirectory(directory);

        var preset = VisualizerPreset.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
        var renderer = new PresetRenderer(preset, width, height)
        {
            MeshPerPixelEnabled = mesh,
            // The reference dump must run the shaders fully; a budget that abandons them would show
            // the overlay over a stale frame and make the comparison meaningless.
            ShaderTimeBudgetMilliseconds = 100_000d,
            ShaderPassBudgetMilliseconds = 100_000d,
        };
        var audio = new ConstantAudio();
        for (var frame = 0; frame < frames; frame++)
        {
            renderer.RenderFrame(audio, 1d / fps);
            var pixels = renderer.Output.Pixels;
            WriteBmp(
                Path.Combine(directory, $"ory-{frame:D3}.bmp"),
                pixels,
                renderer.Output.Width,
                renderer.Output.Height);
        }

        _output.WriteLine(
            $"dumped {frames} frame(s) of '{preset.Name}' at {width} x {height} to {directory} " +
            $"(mesh={mesh}, blocks failed={preset.FailedBlocks.Count})");
    }

    /// <summary>Reads an integer environment variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="fallback">Value used when the variable is unset or unusable.</param>
    /// <returns>The value.</returns>
    private static int ReadInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) && value > 0 ? value : fallback;

    /// <summary>Reads a zero-or-one environment variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <param name="fallback">Value used when the variable is unset or unusable.</param>
    /// <returns>The value.</returns>
    private static bool ReadFlag(string name, bool fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value != 0 : fallback;

    /// <summary>Writes a 24-bit bottom-up BMP from a float RGBA frame.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="pixels">Float RGBA pixels in top-down order.</param>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    private static void WriteBmp(string path, ReadOnlySpan<float> pixels, int width, int height)
    {
        var rowSize = ((width * 3) + 3) & ~3;
        var imageSize = rowSize * height;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(54 + imageSize);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((short)1);
        writer.Write((short)24);
        writer.Write(0);
        writer.Write(imageSize);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        var row = new byte[rowSize];
        for (var y = height - 1; y >= 0; y--)
        {
            Array.Clear(row);
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                row[(x * 3) + 2] = ToByte(pixels[offset]);
                row[(x * 3) + 1] = ToByte(pixels[offset + 1]);
                row[x * 3] = ToByte(pixels[offset + 2]);
            }

            writer.Write(row);
        }
    }

    /// <summary>Clamps a zero-to-one channel to a byte.</summary>
    /// <param name="value">Channel value.</param>
    /// <returns>The byte value.</returns>
    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)(value * 255f + 0.5f), 0, 255);

    /// <summary>A source with fixed levels and a constant waveform, matching the oracle's input.</summary>
    private sealed class ConstantAudio : IVisualizerAudioSource
    {
        private readonly float[] _bands = [0.5f, 0.5f, 0.5f, 0.5f];
        private readonly float[] _waveform = new float[64];

        /// <summary>Creates the source.</summary>
        public ConstantAudio() => Array.Fill(_waveform, 0.5f);

        /// <inheritdoc/>
        public ReadOnlySpan<float> Bands => _bands;

        /// <inheritdoc/>
        public ReadOnlySpan<float> Waveform => _waveform;

        /// <inheritdoc/>
        public float Volume => 0.5f;

        /// <inheritdoc/>
        public float Bass => 0.5f;

        /// <inheritdoc/>
        public float Mid => 0.5f;

        /// <inheritdoc/>
        public float Treble => 0.5f;
    }
}
