using System.ComponentModel;
using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// One episode row of a podcast detail view. It lives outside the main window so XAML can
/// name it in an <c>x:DataType</c> directive, which compiled bindings require (a nested
/// type cannot be referenced from XAML).
/// </summary>
internal sealed class PodcastEpisodeViewModel : INotifyPropertyChanged
{
    private bool _downloaded;
    private string _status = string.Empty;

    /// <summary>Gets the underlying podcast episode.</summary>
    public required PodcastEpisode Episode { get; init; }

    /// <summary>Gets the episode title.</summary>
    public required string Title { get; init; }

    /// <summary>Gets the localized publication date.</summary>
    public required string Published { get; init; }

    /// <summary>Gets the localized duration.</summary>
    public required string Duration { get; init; }

    /// <summary>Gets the localized playback progress.</summary>
    public required string Progress { get; init; }

    /// <summary>Gets the localized play-state text without the download marker.</summary>
    public required string BaseStatus { get; init; }

    /// <summary>Gets the displayed status text including the download marker.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (string.Equals(_status, value, StringComparison.Ordinal))
                return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    /// <summary>Gets a value indicating whether the episode is cached for offline playback.</summary>
    public bool Downloaded
    {
        get => _downloaded;
        private set
        {
            if (_downloaded == value)
                return;
            _downloaded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Downloaded)));
        }
    }

    /// <summary>Gets the publication timestamp used for table sorting.</summary>
    public DateTimeOffset PublishedSort => Episode.PublishedAt ?? DateTimeOffset.MaxValue;

    /// <summary>Gets the duration used for numeric table sorting.</summary>
    public TimeSpan DurationSort { get; init; }

    /// <summary>Gets the progress used for numeric table sorting.</summary>
    public TimeSpan ProgressSort { get; init; }

    /// <summary>Applies the download state to the row without reloading the feed.</summary>
    /// <param name="downloaded">Whether the episode is cached.</param>
    /// <param name="downloadedLabel">Localized download marker.</param>
    public void ApplyDownloadState(bool downloaded, string downloadedLabel)
    {
        Downloaded = downloaded;
        Status = downloaded ? $"{BaseStatus} - {downloadedLabel}" : BaseStatus;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;
}
