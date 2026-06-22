using System.Globalization;
using System.Text.RegularExpressions;

namespace Orynivo.Audio;

/// <summary>
/// Parses the Equalizer APO syntax emitted by AutoEQ for preamp, parametric,
/// and GraphicEQ profiles.
/// </summary>
internal static partial class EqualizerApoParser
{
    private const long MaximumProfileBytes = 4 * 1024 * 1024;
    private const int MaximumFilters = 512;

    /// <summary>Parses an Equalizer APO or AutoEQ profile file.</summary>
    /// <param name="filePath">Path of the UTF-8 or legacy text profile.</param>
    /// <returns>The imported equalizer profile.</returns>
    internal static EqualizerProfile ParseFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > MaximumProfileBytes)
            throw new FormatException("The Equalizer APO profile is too large.");
        return Parse(Path.GetFileNameWithoutExtension(filePath), File.ReadAllLines(filePath));
    }

    /// <summary>Parses Equalizer APO or AutoEQ profile lines.</summary>
    /// <param name="name">Display name assigned to the imported profile.</param>
    /// <param name="lines">Profile lines to parse.</param>
    /// <returns>The imported equalizer profile.</returns>
    internal static EqualizerProfile Parse(string name, IEnumerable<string> lines)
    {
        var profile = new EqualizerProfile { Name = name };
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            var preampMatch = PreampRegex().Match(line);
            if (preampMatch.Success)
            {
                profile.PreampDb += ParseNumber(preampMatch.Groups["gain"].Value);
                continue;
            }

            var filterMatch = FilterRegex().Match(line);
            if (filterMatch.Success)
            {
                if (filterMatch.Groups["state"].Value.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                    continue;
                profile.Filters.Add(ParseFilter(filterMatch));
                EnsureFilterLimit(profile);
                continue;
            }

            var graphicMatch = GraphicEqRegex().Match(line);
            if (graphicMatch.Success)
            {
                AddGraphicEqFilters(profile, graphicMatch.Groups["points"].Value);
                EnsureFilterLimit(profile);
            }
        }

        if (profile.Filters.Count == 0 && Math.Abs(profile.PreampDb) < 0.0001)
            throw new FormatException("The file contains no supported Equalizer APO filters.");
        return profile;
    }

    private static EqualizerFilter ParseFilter(Match match)
    {
        var typeCode = match.Groups["type"].Value.ToUpperInvariant();
        var type = typeCode switch
        {
            "PK" or "PEQ" => EqualizerFilterType.Peak,
            "LS" or "LSC" => EqualizerFilterType.LowShelf,
            "HS" or "HSC" => EqualizerFilterType.HighShelf,
            "LP" or "LPQ" => EqualizerFilterType.LowPass,
            "HP" or "HPQ" => EqualizerFilterType.HighPass,
            _ => throw new FormatException($"Unsupported Equalizer APO filter type '{typeCode}'.")
        };
        var q = match.Groups["q"].Success
            ? ParseNumber(match.Groups["q"].Value)
            : 0.7071067811865476;
        return new EqualizerFilter
        {
            Type = type,
            Frequency = ParseNumber(match.Groups["frequency"].Value),
            GainDb = match.Groups["gain"].Success ? ParseNumber(match.Groups["gain"].Value) : 0,
            Q = q
        };
    }

    private static void AddGraphicEqFilters(EqualizerProfile profile, string pointsText)
    {
        var points = pointsText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(point => point.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .Select(parts => (Frequency: ParseNumber(parts[0]), Gain: ParseNumber(parts[1])))
            .Where(point => point.Frequency > 0)
            .OrderBy(point => point.Frequency)
            .ToArray();

        if (points.Length == 0)
            return;

        profile.PreampDb += points[0].Gain;
        for (var index = 1; index < points.Length; index++)
        {
            var gainChange = points[index].Gain - points[index - 1].Gain;
            if (Math.Abs(gainChange) < 0.001)
                continue;
            profile.Filters.Add(new EqualizerFilter
            {
                Type = EqualizerFilterType.HighShelf,
                Frequency = Math.Sqrt(points[index - 1].Frequency * points[index].Frequency),
                GainDb = gainChange,
                Q = 0.7071067811865476
            });
        }
    }

    private static double ParseNumber(string value) =>
        double.Parse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static void EnsureFilterLimit(EqualizerProfile profile)
    {
        if (profile.Filters.Count > MaximumFilters)
            throw new FormatException("The Equalizer APO profile contains too many filters.");
    }

    [GeneratedRegex(
        @"^\s*Preamp\s*:\s*(?<gain>[+-]?\d+(?:[.,]\d+)?)\s*dB\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreampRegex();

    [GeneratedRegex(
        @"^\s*Filter(?:\s+\d+)?\s*:\s*(?<state>ON|OFF)\s+(?<type>[A-Z]+)\s+Fc\s+(?<frequency>[+-]?\d+(?:[.,]\d+)?)\s*Hz(?:\s+Gain\s+(?<gain>[+-]?\d+(?:[.,]\d+)?)\s*dB)?(?:\s+Q\s+(?<q>[+-]?\d+(?:[.,]\d+)?))?.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FilterRegex();

    [GeneratedRegex(
        @"^\s*GraphicEQ\s*:\s*(?<points>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GraphicEqRegex();
}
