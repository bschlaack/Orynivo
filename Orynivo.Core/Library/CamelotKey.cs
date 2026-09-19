using System.Globalization;

namespace Orynivo.Library;

/// <summary>
/// A musical key expressed on the Camelot wheel for harmonic mixing.
/// </summary>
/// <param name="Number">Wheel position from one through twelve.</param>
/// <param name="IsMinor">Whether the key is minor (the A side) rather than major (the B side).</param>
public readonly record struct CamelotKey(int Number, bool IsMinor)
{
    /// <summary>Pitch-class names indexed from C.</summary>
    private static readonly string[] PitchClassNames =
        ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    /// <summary>Camelot wheel position per major pitch class, indexed from C.</summary>
    private static readonly int[] MajorWheel = [8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1];

    /// <summary>Camelot wheel position per minor pitch class, indexed from C.</summary>
    private static readonly int[] MinorWheel = [5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10];

    /// <summary>Gets the wheel label, for example <c>8A</c>.</summary>
    public string Label => $"{Number.ToString(CultureInfo.InvariantCulture)}{(IsMinor ? 'A' : 'B')}";

    /// <summary>Creates the Camelot key for one pitch class and mode.</summary>
    /// <param name="pitchClass">Pitch class from zero (C) through eleven (B).</param>
    /// <param name="isMinor">Whether the key is minor.</param>
    /// <returns>The matching Camelot key.</returns>
    public static CamelotKey FromPitchClass(int pitchClass, bool isMinor)
    {
        var normalized = ((pitchClass % 12) + 12) % 12;
        return new CamelotKey(isMinor ? MinorWheel[normalized] : MajorWheel[normalized], isMinor);
    }

    /// <summary>
    /// Parses a Camelot label such as <c>8A</c> or a conventional key name such as
    /// <c>A minor</c>, <c>Am</c>, or <c>C dur</c>.
    /// </summary>
    /// <param name="text">Text to parse.</param>
    /// <param name="key">Parsed key when the text is recognised.</param>
    /// <returns><see langword="true"/> when a key was parsed.</returns>
    public static bool TryParse(string? text, out CamelotKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        return TryParseWheelLabel(trimmed, out key) || TryParseKeyName(trimmed, out key);
    }

    /// <summary>
    /// Gets the circular wheel distance. Identical keys are zero, relative
    /// major/minor pairs and neighbouring wheel positions are one, and further
    /// positions increase the distance.
    /// </summary>
    /// <param name="other">Key to compare with.</param>
    /// <returns>The non-negative wheel distance.</returns>
    public int Distance(CamelotKey other)
    {
        var difference = Math.Abs(Number - other.Number);
        var circular = Math.Min(difference, 12 - difference);
        return circular + (IsMinor == other.IsMinor ? 0 : 1);
    }

    /// <summary>Indicates whether two keys mix harmonically.</summary>
    /// <param name="other">Key to compare with.</param>
    /// <returns><see langword="true"/> when the keys are identical, relative, or adjacent on the wheel.</returns>
    public bool IsCompatible(CamelotKey other) => Distance(other) <= 1;

    /// <summary>Gets the conventional key name for this wheel position.</summary>
    /// <returns>A name such as <c>A minor</c> or <c>C major</c>.</returns>
    public string ToKeyName()
    {
        for (var pitchClass = 0; pitchClass < 12; pitchClass++)
        {
            if (FromPitchClass(pitchClass, IsMinor).Number != Number)
                continue;
            return $"{PitchClassNames[pitchClass]}{(IsMinor ? " minor" : " major")}";
        }
        return Label;
    }

    private static bool TryParseWheelLabel(string text, out CamelotKey key)
    {
        key = default;
        if (text.Length is < 2 or > 3)
            return false;

        var letter = char.ToUpperInvariant(text[^1]);
        if (letter is not ('A' or 'B'))
            return false;
        if (!int.TryParse(text[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ||
            number is < 1 or > 12)
        {
            return false;
        }

        key = new CamelotKey(number, letter == 'A');
        return true;
    }

    private static bool TryParseKeyName(string text, out CamelotKey key)
    {
        key = default;
        var normalized = text
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace("♯", "#")
            .Replace("♭", "b");
        if (normalized.Length == 0)
            return false;

        var minor = false;
        foreach (var suffix in new[] { "minor", "min", "moll", "m" })
        {
            if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;
            normalized = normalized[..^suffix.Length];
            minor = true;
            break;
        }

        if (!minor)
        {
            foreach (var suffix in new[] { "major", "maj", "dur" })
            {
                if (!normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;
                normalized = normalized[..^suffix.Length];
                break;
            }
        }

        if (!TryParsePitchClass(normalized, out var pitchClass))
            return false;
        key = FromPitchClass(pitchClass, minor);
        return true;
    }

    private static bool TryParsePitchClass(string text, out int pitchClass)
    {
        pitchClass = 0;
        if (text.Length is 0 or > 2)
            return false;

        var baseIndex = char.ToUpperInvariant(text[0]) switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' or 'H' => 11,
            _ => -1
        };
        if (baseIndex < 0)
            return false;

        var offset = 0;
        if (text.Length == 2)
        {
            offset = text[1] switch
            {
                '#' => 1,
                'b' or 'B' => -1,
                _ => int.MinValue
            };
            if (offset == int.MinValue)
                return false;
        }

        pitchClass = ((baseIndex + offset) % 12 + 12) % 12;
        return true;
    }
}
