using System.ComponentModel;
using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// One synchronized lyric line of the lyrics view. It lives outside the main window so
/// XAML can name it in an <c>x:DataType</c> directive, which compiled bindings require
/// (a nested type cannot be referenced from XAML).
/// </summary>
internal sealed class LyricLineViewModel : INotifyPropertyChanged
{
    private bool _isActive;

    /// <summary>Creates a lyric line.</summary>
    /// <param name="text">Display text of the line.</param>
    /// <param name="time">Line timestamp, or <see langword="null"/> for an untimed line.</param>
    /// <param name="words">Word-level timings of an enhanced-LRC line.</param>
    public LyricLineViewModel(string text, TimeSpan? time, IReadOnlyList<TimedLyricWord>? words = null)
    {
        Text = text;
        Time = time;
        Words = words ?? [];
    }

    /// <summary>Gets the display text of the line.</summary>
    public string Text { get; }

    /// <summary>Gets the line timestamp, or <see langword="null"/> for an untimed line.</summary>
    public TimeSpan? Time { get; }

    /// <summary>Gets the word-level timings of an enhanced-LRC line; empty for plain lines.</summary>
    public IReadOnlyList<TimedLyricWord> Words { get; }

    /// <summary>Gets or sets a value indicating whether the line is the audible one.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;
}
