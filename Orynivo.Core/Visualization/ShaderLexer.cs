using System.Globalization;

namespace Orynivo.Visualization;

/// <summary>
/// Splits HLSL <c>ps_2_0</c> shader source into tokens. Milkdrop presets embed their shader code
/// as text, so the shader runtime needs a front end that understands the subset those shaders
/// use: identifiers, numbers with optional suffixes, the usual operators, swizzles, and the
/// keywords. Comments and whitespace are skipped; an unexpected character reports its position
/// instead of throwing an index error.
/// </summary>
public static class ShaderLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "else", "for", "while", "do", "return", "break", "continue", "discard",
        "true", "false", "const", "static", "inline"
    };

    private static readonly string[] MultiCharacterOperators =
    [
        "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "++", "--", "<<", ">>"
    ];

    /// <summary>Tokenizes shader source.</summary>
    /// <param name="source">Shader source text.</param>
    /// <returns>The tokens, always ending with an <see cref="ShaderTokenKind.End"/> token.</returns>
    /// <exception cref="PresetExpressionException">The source contains an unexpected character.</exception>
    public static IReadOnlyList<ShaderToken> Tokenize(string? source)
    {
        var text = source ?? string.Empty;
        var tokens = new List<ShaderToken>();
        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                while (index < text.Length && text[index] != '\n')
                    index++;
                continue;
            }

            if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                index += 2;
                while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                    index++;
                index = Math.Min(text.Length, index + 2);
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                var start = index;
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                    index++;
                var word = text[start..index];
                tokens.Add(new ShaderToken(
                    Keywords.Contains(word) ? ShaderTokenKind.Keyword : ShaderTokenKind.Identifier,
                    word,
                    start,
                    0f));
                continue;
            }

            if (char.IsDigit(current) || (current == '.' && index + 1 < text.Length && char.IsDigit(text[index + 1])))
            {
                var start = index;
                while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
                    index++;
                // A trailing f/h suffix marks a float or half literal and is not part of the value.
                if (index < text.Length && (text[index] == 'f' || text[index] == 'h' || text[index] == 'F'))
                    index++;

                var number = text[start..index];
                var parseable = number.TrimEnd('f', 'h', 'F');
                var value = float.TryParse(parseable, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0f;
                tokens.Add(new ShaderToken(ShaderTokenKind.Number, number, start, value));
                continue;
            }

            var matched = false;
            foreach (var candidate in MultiCharacterOperators)
            {
                if (index + candidate.Length <= text.Length &&
                    string.CompareOrdinal(text, index, candidate, 0, candidate.Length) == 0)
                {
                    tokens.Add(new ShaderToken(ShaderTokenKind.Punctuation, candidate, index, 0f));
                    index += candidate.Length;
                    matched = true;
                    break;
                }
            }

            if (matched)
                continue;

            if ("+-*/%<>=!&|^~?:;,.()[]{}".IndexOf(current) >= 0)
            {
                tokens.Add(new ShaderToken(ShaderTokenKind.Punctuation, current.ToString(), index, 0f));
                index++;
                continue;
            }

            throw new PresetExpressionException(
                $"Unexpected character '{current}' in shader source.", index);
        }

        tokens.Add(new ShaderToken(ShaderTokenKind.End, string.Empty, text.Length, 0f));
        return tokens;
    }
}
