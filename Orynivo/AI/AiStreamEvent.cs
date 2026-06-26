namespace Orynivo.AI;

/// <summary>
/// Discriminated union of events emitted by <see cref="AiChatService"/> during a streaming response.
/// </summary>
public abstract record AiStreamEvent
{
    /// <summary>Creates a content token event.</summary>
    /// <param name="text">The streamed text fragment.</param>
    /// <returns>A <see cref="TokenEvent"/>.</returns>
    public static AiStreamEvent Token(string text) => new TokenEvent(text);

    /// <summary>Creates a tool-call notification event.</summary>
    /// <param name="name">The tool being invoked.</param>
    /// <returns>A <see cref="ToolCallEvent"/>.</returns>
    public static AiStreamEvent ToolCall(string name) => new ToolCallEvent(name);

    /// <summary>Creates an error event.</summary>
    /// <param name="message">Human-readable error description.</param>
    /// <returns>An <see cref="ErrorEvent"/>.</returns>
    public static AiStreamEvent Error(string message) => new ErrorEvent(message);

    /// <summary>Creates a completion event signalling the end of the assistant's turn.</summary>
    /// <returns>A <see cref="DoneEvent"/>.</returns>
    public static AiStreamEvent Done() => new DoneEvent();

    /// <summary>A streamed text fragment from the model.</summary>
    /// <param name="Text">The text fragment.</param>
    public sealed record TokenEvent(string Text) : AiStreamEvent;

    /// <summary>The model is calling a tool.</summary>
    /// <param name="ToolName">Name of the tool being called.</param>
    public sealed record ToolCallEvent(string ToolName) : AiStreamEvent;

    /// <summary>A transport or API error occurred.</summary>
    /// <param name="Message">Human-readable error description.</param>
    public sealed record ErrorEvent(string Message) : AiStreamEvent;

    /// <summary>The model has finished its response for this turn.</summary>
    public sealed record DoneEvent : AiStreamEvent;
}
