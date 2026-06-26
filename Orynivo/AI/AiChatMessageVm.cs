using System.ComponentModel;

namespace Orynivo.AI;

/// <summary>
/// View-model for a single AI chat message displayed in <see cref="AiChatView"/>.
/// Implements <see cref="INotifyPropertyChanged"/> so streaming token updates render live.
/// </summary>
public sealed class AiChatMessageVm : INotifyPropertyChanged
{
    private string _content = string.Empty;
    private bool _isStreaming;

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the message role: <c>user</c>, <c>assistant</c>, <c>tool</c>, or <c>error</c>.</summary>
    public string Role { get; init; } = "assistant";

    /// <summary>Gets a value indicating whether this message was sent by the user.</summary>
    public bool IsUser => Role == "user";

    /// <summary>Gets a value indicating whether this message is an assistant response.</summary>
    public bool IsAssistant => Role == "assistant";

    /// <summary>Gets a value indicating whether this message represents an error.</summary>
    public bool IsError => Role == "error";

    /// <summary>Gets a value indicating whether this is a tool-call status notification.</summary>
    public bool IsToolStatus => Role == "tool";

    /// <summary>Gets or sets the text content of the message, updated in real time during streaming.</summary>
    public string Content
    {
        get => _content;
        set
        {
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
        }
    }

    /// <summary>Gets or sets a value indicating whether this message is still being streamed.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            _isStreaming = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsStreaming)));
        }
    }
}
