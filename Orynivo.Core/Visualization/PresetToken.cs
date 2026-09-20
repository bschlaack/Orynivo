namespace Orynivo.Visualization;

/// <summary>Kinds of token produced by <see cref="PresetLexer"/>.</summary>
internal enum PresetTokenKind
{
    /// <summary>End of the expression.</summary>
    End,

    /// <summary>Numeric literal.</summary>
    Number,

    /// <summary>Variable or function name.</summary>
    Identifier,

    /// <summary>Opening parenthesis.</summary>
    OpenParenthesis,

    /// <summary>Closing parenthesis.</summary>
    CloseParenthesis,

    /// <summary>Argument separator.</summary>
    Comma,

    /// <summary>Statement separator.</summary>
    Semicolon,

    /// <summary>Ternary condition separator.</summary>
    Question,

    /// <summary>Ternary branch separator.</summary>
    Colon,

    /// <summary>Addition.</summary>
    Plus,

    /// <summary>Subtraction or unary negation.</summary>
    Minus,

    /// <summary>Multiplication.</summary>
    Star,

    /// <summary>Division.</summary>
    Slash,

    /// <summary>Remainder.</summary>
    Percent,

    /// <summary>Assignment.</summary>
    Assign,

    /// <summary>Logical negation.</summary>
    Not,

    /// <summary>Less than.</summary>
    Less,

    /// <summary>Greater than.</summary>
    Greater,

    /// <summary>Logical and.</summary>
    And,

    /// <summary>Logical or.</summary>
    Or,

    /// <summary>Equality comparison.</summary>
    Equal,

    /// <summary>Inequality comparison.</summary>
    NotEqual,

    /// <summary>Less than or equal.</summary>
    LessOrEqual,

    /// <summary>Greater than or equal.</summary>
    GreaterOrEqual
}

/// <summary>One lexed token with its source position.</summary>
/// <param name="Kind">Token kind.</param>
/// <param name="Text">Original text.</param>
/// <param name="Position">Zero-based offset in the expression.</param>
/// <param name="Value">Numeric value for number tokens.</param>
internal readonly record struct PresetToken(
    PresetTokenKind Kind,
    string Text,
    int Position,
    float Value = 0f);
