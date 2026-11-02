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
        var hasSupportedDirective = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Split('#', 2)[0].Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            var preampMatch = PreampRegex().Match(line);
            if (preampMatch.Success)
            {
                profile.PreampDb += ParseNumber(preampMatch.Groups["gain"].Value);
                if (!double.IsFinite(profile.PreampDb))
                    throw new FormatException("The Equalizer APO preamp is outside the supported range.");
                hasSupportedDirective = true;
                continue;
            }

            var filterMatch = FilterRegex().Match(line);
            if (filterMatch.Success)
            {
                if (filterMatch.Groups["state"].Value.Equals("OFF", StringComparison.OrdinalIgnoreCase))
                    continue;
                profile.Filters.Add(ParseFilter(filterMatch));
                hasSupportedDirective = true;
                EnsureFilterLimit(profile);
                continue;
            }

            var graphicMatch = GraphicEqRegex().Match(line);
            if (graphicMatch.Success)
            {
                AddGraphicEqFilters(profile, graphicMatch.Groups["points"].Value);
                EnsureFilterLimit(profile);
                hasSupportedDirective = true;
                continue;
            }

            if (DirectiveRegex().IsMatch(line))
                throw new FormatException("The Equalizer APO profile contains an invalid or unsupported equalizer directive.");
        }

        if (!hasSupportedDirective)
            throw new FormatException("The file contains no supported Equalizer APO filters.");
        return profile;
    }

    /// <summary>Parses one supported filter without silently substituting invalid numeric values.</summary>
    /// <param name="match">The complete filter directive match.</param>
    /// <returns>The validated filter.</returns>
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
        var frequency = ParseNumber(match.Groups["frequency"].Value);
        if (q <= 0 || frequency <= 0)
            throw new FormatException("Equalizer APO frequency and Q must be positive.");
        return new EqualizerFilter
        {
            Type = type,
            Frequency = frequency,
            GainDb = match.Groups["gain"].Success ? ParseNumber(match.Groups["gain"].Value) : 0,
            Q = q
        };
    }

    /// <summary>Converts complete graphic points into bounded shelf filters.</summary>
    /// <param name="profile">The profile receiving the filters and baseline gain.</param>
    /// <param name="pointsText">Semicolon-separated frequency and gain pairs.</param>
    private static void AddGraphicEqFilters(EqualizerProfile profile, string pointsText)
    {
        var points = pointsText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(point => point.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Select(parts => parts.Length == 2
                ? (Frequency: ParseNumber(parts[0]), Gain: ParseNumber(parts[1]))
                : throw new FormatException("Each GraphicEQ point requires a frequency and gain."))
            .OrderBy(point => point.Frequency)
            .ToArray();

        if (points.Length == 0 || points.Any(point => point.Frequency <= 0))
            throw new FormatException("GraphicEQ points require positive frequencies.");

        profile.PreampDb += points[0].Gain;
        if (!double.IsFinite(profile.PreampDb))
            throw new FormatException("The Equalizer APO preamp is outside the supported range.");
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
            EnsureFilterLimit(profile);
        }
    }

    /// <summary>Parses a finite culture-independent profile value.</summary>
    /// <param name="value">The numeric field.</param>
    /// <returns>The finite value.</returns>
    private static double ParseNumber(string value)
    {
        if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ||
            !double.IsFinite(result))
            throw new FormatException("The Equalizer APO profile contains an invalid numeric value.");
        return result;
    }

    /// <summary>Rejects imports exceeding the supported active-filter count.</summary>
    /// <param name="profile">The profile under construction.</param>
    private static void EnsureFilterLimit(EqualizerProfile profile)
    {
        if (profile.Filters.Count > MaximumFilters)
            throw new FormatException("The Equalizer APO profile contains too many filters.");
    }

    [GeneratedRegex(
        @"^\s*Preamp\s*:\s*(?<gain>[+-]?\d+(?:[.,]\d+)?)\s*dB\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PreampRegex();

    /// <summary>Matches complete supported parametric directives without accepting unknown suffixes.</summary>
    /// <returns>The supported parametric filter recognizer.</returns>
    [GeneratedRegex(
        @"^\s*Filter(?:\s+\d+)?\s*:\s*(?<state>ON|OFF)\s+(?<type>[A-Z]+)\s+Fc\s+(?<frequency>[+-]?\d+(?:[.,]\d+)?)\s*Hz(?:\s+Gain\s+(?<gain>[+-]?\d+(?:[.,]\d+)?)\s*dB)?(?:\s+Q\s+(?<q>[+-]?\d+(?:[.,]\d+)?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FilterRegex();

    [GeneratedRegex(
        @"^\s*GraphicEQ\s*:\s*(?<points>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GraphicEqRegex();

    /// <summary>Identifies equalizer directives whose invalid syntax must not be ignored.</summary>
    /// <returns>The supported directive-prefix recognizer.</returns>
    [GeneratedRegex(@"^(?:Preamp|Filter|GraphicEQ)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectiveRegex();
}
