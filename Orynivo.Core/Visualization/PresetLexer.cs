using System.Globalization;

namespace Orynivo.Visualization;

/// <summary>
/// Splits a preset expression into the tokens the parser consumes. It is deliberately
/// small: numbers, identifiers, operators, parentheses, commas, semicolons, and the
/// <c>//</c> line comment that preset authors use.
/// </summary>
internal sealed class PresetLexer
{
    private readonly string _source;
    private int _position;

    /// <summary>Creates a lexer over one expression block.</summary>
    /// <param name="source">Expression text.</param>
    public PresetLexer(string source)
    {
        _source = source ?? string.Empty;
    }

    /// <summary>Reads the next token.</summary>
    /// <returns>The token, or an end-of-input token when the source is exhausted.</returns>
    /// <exception cref="PresetExpressionException">The source contains an unknown character.</exception>
    public PresetToken Next()
    {
        SkipTrivia();
        if (_position >= _source.Length)
            return new PresetToken(PresetTokenKind.End, string.Empty, _position);

        var start = _position;
        var character = _source[_position];

        if (char.IsDigit(character) || (character == '.' && _position + 1 < _source.Length && char.IsDigit(_source[_position + 1])))
            return ReadNumber(start);

        if (char.IsLetter(character) || character == '_')
            return ReadIdentifier(start);

        _position++;
        return character switch
        {
            '(' => new PresetToken(PresetTokenKind.OpenParenthesis, "(", start),
            ')' => new PresetToken(PresetTokenKind.CloseParenthesis, ")", start),
            ',' => new PresetToken(PresetTokenKind.Comma, ",", start),
            ';' => new PresetToken(PresetTokenKind.Semicolon, ";", start),
            '?' => new PresetToken(PresetTokenKind.Question, "?", start),
            ':' => new PresetToken(PresetTokenKind.Colon, ":", start),
            '+' => ReadOperator('+', start, PresetTokenKind.Plus),
            '-' => ReadOperator('-', start, PresetTokenKind.Minus),
            '*' => ReadOperator('*', start, PresetTokenKind.Star),
            '/' => ReadOperator('/', start, PresetTokenKind.Slash),
            '%' => ReadOperator('%', start, PresetTokenKind.Percent),
            '=' => ReadOperator('=', start, PresetTokenKind.Assign),
            '!' => ReadOperator('!', start, PresetTokenKind.Not),
            '<' => ReadOperator('<', start, PresetTokenKind.Less),
            '>' => ReadOperator('>', start, PresetTokenKind.Greater),
            '&' => ReadOperator('&', start, PresetTokenKind.And),
            '|' => ReadOperator('|', start, PresetTokenKind.Or),
            _ => throw new PresetExpressionException($"Unexpected character '{character}'", start)
        };
    }

    private PresetToken ReadOperator(char character, int start, PresetTokenKind single)
    {
        // The doubled forms carry a second character that differs for !, < and >.
        if (_position < _source.Length)
        {
            PresetTokenKind? doubled = (character, _source[_position]) switch
            {
                ('&', '&') => PresetTokenKind.And,
                ('|', '|') => PresetTokenKind.Or,
                ('=', '=') => PresetTokenKind.Equal,
                ('!', '=') => PresetTokenKind.NotEqual,
                ('<', '=') => PresetTokenKind.LessOrEqual,
                ('>', '=') => PresetTokenKind.GreaterOrEqual,
                // Presets write "n += 1" and "zoom -= 0.03" constantly, so the compound
                // assignments must lex as one token; splitting them into an operator and an
                // "=" leaves the statement parser with a stray assignment it cannot place.
                ('+', '=') => PresetTokenKind.AssignCompound,
                ('-', '=') => PresetTokenKind.AssignCompound,
                ('*', '=') => PresetTokenKind.AssignCompound,
                ('/', '=') => PresetTokenKind.AssignCompound,
                ('%', '=') => PresetTokenKind.AssignCompound,
                _ => null
            };
            if (doubled is { } kind)
            {
                _position++;
                return new PresetToken(kind, _source.Substring(start, 2), start);
            }
        }

        return new PresetToken(single, _source.Substring(start, _position - start), start);
    }

    private PresetToken ReadNumber(int start)
    {
        while (_position < _source.Length && (char.IsDigit(_source[_position]) || _source[_position] == '.'))
            _position++;

        // Exponent part, for example 1e-3.
        if (_position < _source.Length && (_source[_position] is 'e' or 'E'))
        {
            var exponentStart = _position;
            _position++;
            if (_position < _source.Length && (_source[_position] is '+' or '-'))
                _position++;
            if (_position < _source.Length && char.IsDigit(_source[_position]))
            {
                while (_position < _source.Length && char.IsDigit(_source[_position]))
                    _position++;
            }
            else
            {
                _position = exponentStart;
            }
        }

        var text = _source[start.._position];
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new PresetExpressionException($"Unusable number '{text}'", start);

        return new PresetToken(PresetTokenKind.Number, text, start, value);
    }

    private PresetToken ReadIdentifier(int start)
    {
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_'))
            _position++;

        return new PresetToken(PresetTokenKind.Identifier, _source[start.._position], start);
    }

    private void SkipTrivia()
    {
        while (_position < _source.Length)
        {
            var character = _source[_position];
            if (char.IsWhiteSpace(character))
            {
                _position++;
                continue;
            }

            if (character == '/' && _position + 1 < _source.Length && _source[_position + 1] == '/')
            {
                while (_position < _source.Length && _source[_position] != '\n')
                    _position++;
                continue;
            }

            break;
        }
    }
}
