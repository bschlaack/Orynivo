namespace Orynivo.Visualization;

/// <summary>
/// Reports an unusable visualization preset expression. The message names the offending
/// position so a preset author can find the mistake without a debugger.
/// </summary>
public sealed class PresetExpressionException : Exception
{
    /// <summary>Creates the exception for one position in a preset expression.</summary>
    /// <param name="message">Human-readable reason.</param>
    /// <param name="position">Zero-based character offset inside the expression.</param>
    public PresetExpressionException(string message, int position)
        : base($"{message} (at position {position})")
    {
        Position = position;
    }

    /// <summary>Gets the zero-based character offset the failure was reported at.</summary>
    public int Position { get; }
}
