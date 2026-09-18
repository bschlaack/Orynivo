using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using Orynivo.Library;

namespace Orynivo.Audio;

/// <summary>Versioned compact acoustic descriptors cached independently from source metadata.</summary>
/// <param name="Version">Descriptor schema version.</param>
/// <param name="Energy">Normalized root-mean-square energy.</param>
/// <param name="Brightness">Normalized zero-crossing/transient proxy.</param>
/// <param name="Dynamics">Normalized variation between short-time energy windows.</param>
/// <param name="AnalyzedAt">Unix timestamp at which analysis completed.</param>
/// <param name="Key">Estimated musical key on the Camelot wheel, or <see langword="null"/> when it is ambiguous.</param>
public sealed record AudioFeatureDescriptor(
    int Version,
    double Energy,
    double Brightness,
    double Dynamics,
    long AnalyzedAt,
    CamelotKey? Key = null);

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
    public const int CurrentVersion = 2;
    private const int SampleRate = 8000;
    private const int MaximumSeconds = 90;
    private const int ChromaFrameSize = 2048;
    private const int ChromaHopSize = 4096;
    private const int LowestChromaOctave = 3;
    private const int ChromaOctaveCount = 5;
    private const double MinimumKeyCorrelation = 0.15d;

    /// <summary>Krumhansl-Schmuckler major-key profile, indexed from the tonic.</summary>
    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];

    /// <summary>Krumhansl-Schmuckler minor-key profile, indexed from the tonic.</summary>
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

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
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            EstimateKey(samples, sampleRate));
    }

    /// <summary>
    /// Estimates the musical key with a bounded Goertzel chromagram and
    /// Krumhansl-Schmuckler profile correlation. The estimate stays deliberately
    /// conservative: a flat or ambiguous chroma returns <see langword="null"/>
    /// instead of a guess, so harmonic ordering simply skips that track.
    /// </summary>
    /// <param name="samples">Normalized mono floating-point samples.</param>
    /// <param name="sampleRate">Sample rate in hertz.</param>
    /// <returns>The estimated Camelot key, or <see langword="null"/> when the chroma is too flat to decide.</returns>
    internal static CamelotKey? EstimateKey(IReadOnlyList<float> samples, int sampleRate)
    {
        if (sampleRate <= 0 || samples.Count < ChromaFrameSize)
            return null;

        var chroma = new double[12];
        var frame = new double[ChromaFrameSize];
        var frameCount = 0;
        for (var start = 0; start + ChromaFrameSize <= samples.Count; start += ChromaHopSize)
        {
            var energy = 0d;
            for (var index = 0; index < ChromaFrameSize; index++)
            {
                var window = 0.5d - 0.5d * Math.Cos(2d * Math.PI * index / (ChromaFrameSize - 1));
                var value = Math.Clamp(samples[start + index], -1f, 1f) * window;
                frame[index] = value;
                energy += value * value;
            }

            if (energy < 1e-6d)
                continue;

            frameCount++;
            for (var pitchClass = 0; pitchClass < 12; pitchClass++)
            {
                var magnitude = 0d;
                for (var octave = LowestChromaOctave; octave < LowestChromaOctave + ChromaOctaveCount; octave++)
                {
                    var frequency = 440d * Math.Pow(2d, (12 * (octave + 1) + pitchClass - 69) / 12d);
                    if (frequency >= sampleRate / 2d)
                        continue;
                    magnitude += GoertzelMagnitude(frame, frequency, sampleRate);
                }

                chroma[pitchClass] += magnitude;
            }
        }

        if (frameCount == 0)
            return null;

        var mean = chroma.Average();
        var deviation = Math.Sqrt(chroma.Select(value => (value - mean) * (value - mean)).Average());
        if (deviation <= 1e-9d)
            return null;

        var bestScore = double.MinValue;
        var bestPitchClass = 0;
        var bestIsMinor = false;
        for (var rotation = 0; rotation < 12; rotation++)
        {
            var majorScore = Correlate(chroma, MajorProfile, rotation);
            if (majorScore > bestScore)
            {
                bestScore = majorScore;
                bestPitchClass = rotation;
                bestIsMinor = false;
            }

            var minorScore = Correlate(chroma, MinorProfile, rotation);
            if (minorScore > bestScore)
            {
                bestScore = minorScore;
                bestPitchClass = rotation;
                bestIsMinor = true;
            }
        }

        return bestScore < MinimumKeyCorrelation
            ? null
            : CamelotKey.FromPitchClass(bestPitchClass, bestIsMinor);
    }

    private static double GoertzelMagnitude(ReadOnlySpan<double> frame, double frequency, int sampleRate)
    {
        var coefficient = 2d * Math.Cos(2d * Math.PI * frequency / sampleRate);
        var previous = 0d;
        var previousPrevious = 0d;
        foreach (var sample in frame)
        {
            var current = sample + coefficient * previous - previousPrevious;
            previousPrevious = previous;
            previous = current;
        }

        var power = previous * previous + previousPrevious * previousPrevious -
                    coefficient * previous * previousPrevious;
        return Math.Sqrt(Math.Max(0d, power));
    }

    /// <summary>Correlates a chroma vector with a rotated key profile using Pearson correlation.</summary>
    /// <param name="chroma">Twelve-element chroma vector indexed by pitch class.</param>
    /// <param name="profile">Key profile indexed from the tonic.</param>
    /// <param name="rotation">Tonic pitch class the profile is rotated to.</param>
    /// <returns>The correlation from minus one through one.</returns>
    private static double Correlate(double[] chroma, double[] profile, int rotation)
    {
        var chromaMean = 0d;
        var profileMean = 0d;
        for (var index = 0; index < 12; index++)
        {
            chromaMean += chroma[(rotation + index) % 12];
            profileMean += profile[index];
        }

        chromaMean /= 12d;
        profileMean /= 12d;

        var covariance = 0d;
        var chromaVariance = 0d;
        var profileVariance = 0d;
        for (var index = 0; index < 12; index++)
        {
            var chromaValue = chroma[(rotation + index) % 12] - chromaMean;
            var profileValue = profile[index] - profileMean;
            covariance += chromaValue * profileValue;
            chromaVariance += chromaValue * chromaValue;
            profileVariance += profileValue * profileValue;
        }

        var denominator = Math.Sqrt(chromaVariance * profileVariance);
        return denominator <= 1e-12d ? 0d : covariance / denominator;
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
}
