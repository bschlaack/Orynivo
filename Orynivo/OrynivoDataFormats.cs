using Avalonia.Input;

namespace Orynivo;

/// <summary>
/// Application data formats used by in-process drag-and-drop. Keeping them in a
/// dedicated type makes the identifiers testable, because Avalonia validates
/// them eagerly and an invalid identifier would otherwise crash the window's
/// static initializer at startup.
/// </summary>
internal static class OrynivoDataFormats
{
    /// <summary>
    /// Gets the format carrying the JSON-serialized queue drag tokens.
    /// </summary>
    /// <remarks>
    /// <see cref="DataFormat.CreateStringApplicationFormat"/> only accepts ASCII
    /// letters, digits, the dot, and the hyphen, so the identifier must not look
    /// like a MIME type.
    /// </remarks>
    public static DataFormat<string> QueueDragTokens { get; } =
        DataFormat.CreateStringApplicationFormat("orynivo.queue-paths");
}
