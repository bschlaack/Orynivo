using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;

namespace Orynivo.Audio;

/// <summary>Versioned compact acoustic descriptors cached independently from source metadata.</summary>
/// <param name="Version">Descriptor schema version.</param>
/// <param name="Energy">Normalized root-mean-square energy.</param>
/// <param name="Brightness">Normalized zero-crossing/transient proxy.</param>
/// <param name="Dynamics">Normalized variation between short-time energy windows.</param>
/// <param name="AnalyzedAt">Unix timestamp at which analysis completed.</param>
public sealed record AudioFeatureDescriptor(
    int Version,
    double Energy,
    double Brightness,
    double Dynamics,
    long AnalyzedAt);

/// <summary>One provider-local source selected for optional acoustic analysis.</summary>
/// <param name="TrackId">Provider-local track identifier.</param>
/// <param name="SourcePath">Physical source path.</param>
/// <param name="SegmentStart">Optional virtual-track segment start.</param>
/// <param name="SegmentEnd">Optional virtual-track segment end.</param>
public sealed record AudioFeatureAnalysisCandidate(
    long TrackId,
    string SourcePath,
    TimeSpan? SegmentStart,
    TimeSpan? SegmentEnd);

/// <summary>Extracts bounded, provider-local acoustic descriptors through a low-rate FFmpeg decode.</summary>
public static class AudioFeatureAnalysisService
{
    /// <summary>Current cached acoustic-descriptor version.</summary>
    public const int CurrentVersion = 1;
    private const int SampleRate = 8000;
    private const int MaximumSeconds = 90;

    /// <summary>Analyzes at most ninety seconds of a local physical source using one FFmpeg thread.</summary>
    /// <param name="sourcePath">Physical audio source path.</param>
    /// <param name="segmentStart">Optional virtual-track segment start.</param>
    /// <param name="segmentEnd">Optional virtual-track segment end.</param>
    /// <param name="cancellationToken">Cancellation token that also terminates FFmpeg.</param>
    /// <returns>Normalized descriptors, or <see langword="null"/> when no samples could be decoded.</returns>
    public static async Task<AudioFeatureDescriptor?> AnalyzeAsync(
        string sourcePath,
        TimeSpan? segmentStart = null,
        TimeSpan? segmentEnd = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var start = segmentStart.GetValueOrDefault();
        var duration = segmentEnd is { } end && end > start
            ? Math.Min(MaximumSeconds, (end - start).TotalSeconds)
            : MaximumSeconds;
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            WorkingDirectory = FfmpegLocator.GetSafeWorkingDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[]
                 {
                     "-v", "error",
                     "-threads", "1",
                     "-ss", start.TotalSeconds.ToString("F6", CultureInfo.InvariantCulture),
                     "-i", sourcePath,
                     "-t", duration.ToString("F3", CultureInfo.InvariantCulture),
                     "-vn",
                     "-ac", "1",
                     "-ar", SampleRate.ToString(CultureInfo.InvariantCulture),
                     "-f", "f32le",
                     "pipe:1"
                 })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo);
        if (process is null)
            return null;
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch
        {
            // Best effort only; FFmpeg is already restricted to one worker thread.
        }

        var samples = new List<float>(SampleRate * Math.Min(10, (int)Math.Ceiling(duration)));
        var buffer = new byte[32 * 1024];
        var carry = new byte[sizeof(float)];
        var carryCount = 0;
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            while (true)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                var offset = 0;
                if (carryCount > 0)
                {
                    var needed = sizeof(float) - carryCount;
                    var copied = Math.Min(needed, read);
                    Buffer.BlockCopy(buffer, 0, carry, carryCount, copied);
                    carryCount += copied;
                    offset += copied;
                    if (carryCount == sizeof(float))
                    {
                        samples.Add(ReadSingle(carry));
                        carryCount = 0;
                    }
                }
                while (offset + sizeof(float) <= read)
                {
                    samples.Add(ReadSingle(buffer.AsSpan(offset, sizeof(float))));
                    offset += sizeof(float);
                }
                if (offset < read)
                {
                    carryCount = read - offset;
                    Buffer.BlockCopy(buffer, offset, carry, 0, carryCount);
                }
            }
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
            return process.ExitCode == 0 ? AnalyzePcm(samples, SampleRate) : null;
        }
        catch
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }
    }

    /// <summary>Calculates descriptors from normalized mono samples for deterministic tests and streaming decoders.</summary>
    /// <param name="samples">Normalized mono floating-point samples.</param>
    /// <param name="sampleRate">Sample rate in hertz.</param>
    /// <returns>Normalized descriptors, or <see langword="null"/> for an empty stream.</returns>
    internal static AudioFeatureDescriptor? AnalyzePcm(IReadOnlyList<float> samples, int sampleRate)
    {
        if (samples.Count == 0 || sampleRate <= 0)
            return null;
        var sumSquares = 0d;
        var crossings = 0;
        var windows = new List<double>();
        var windowSquares = 0d;
        var windowSamples = 0;
        var previous = samples[0];
        foreach (var raw in samples)
        {
            var sample = Math.Clamp(raw, -1f, 1f);
            sumSquares += sample * sample;
            windowSquares += sample * sample;
            windowSamples++;
            if ((sample >= 0) != (previous >= 0))
                crossings++;
            previous = sample;
            if (windowSamples >= sampleRate)
            {
                windows.Add(Math.Sqrt(windowSquares / windowSamples));
                windowSquares = 0;
                windowSamples = 0;
            }
        }
        if (windowSamples > 0)
            windows.Add(Math.Sqrt(windowSquares / windowSamples));
        var rms = Math.Sqrt(sumSquares / samples.Count);
        var audible = windows.Where(value => value > 0.00001d).ToList();
        var dynamicDb = audible.Count < 2 ? 0d : 20d * Math.Log10(audible.Max() / audible.Min());
        return new AudioFeatureDescriptor(
            CurrentVersion,
            Math.Clamp(rms * 3d, 0d, 1d),
            Math.Clamp(crossings / (double)Math.Max(1, samples.Count - 1) / 0.35d, 0d, 1d),
            Math.Clamp(dynamicDb / 30d, 0d, 1d),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
}
