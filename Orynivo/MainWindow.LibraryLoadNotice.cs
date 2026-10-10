using Avalonia.Interactivity;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Incomplete shared-library and search presentation with in-place retry for the existing navigation owner.</summary>
public partial class MainWindow
{
    private int? _unifiedLibraryRetryVersion;

    /// <summary>Clears the visible notice when its owning navigation is abandoned.</summary>
    private void ClearUnifiedLibraryLoadNotice()
    {
        _unifiedLibraryLoadResult = null;
        _unifiedLibraryRetryVersion = null;
        UpdateUnifiedLibraryLoadNotice();
    }

    /// <summary>Refreshes the current notice and keyboard-accessible retry action in the selected language.</summary>
    private void UpdateUnifiedLibraryLoadNotice()
    {
        var result = _unifiedLibraryLoadResult;
        var text = result is null ? string.Empty : LibraryLoadNoticeText.Format(result.Status,
            result.Sources.Count(source => source.Status is LibrarySourceLoadStatus.Failed or LibrarySourceLoadStatus.TimedOut),
            LocalizationManager.Current);
        if (SearchResultsScrollViewer.IsVisible)
            text = GetLibrarySearchNoticeText();
        LibraryLoadNotice.IsVisible = text.Length > 0;
        LibraryLoadNoticeMessage.Text = text;
        var busy = _unifiedLibraryRetryVersion.HasValue || _librarySearchRetryVersion.HasValue;
        LibraryLoadRetryButton.IsEnabled = !busy;
        LibraryLoadRetryButton.Content = busy
            ? LocalizationManager.Current.LibraryLoadRetrying
            : LocalizationManager.Current.LibraryLoadRetry;
    }

    /// <summary>Retries the owning catalog or search without clearing usable rows or showing a loading overlay.</summary>
    /// <param name="sender">Retry button.</param>
    /// <param name="e">Button activation event.</param>
    private async void LibraryLoadRetryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (SearchResultsScrollViewer.IsVisible && _librarySearchLoadResult?.Status is LibraryLoadStatus.Partial or LibraryLoadStatus.Failed)
        {
            if (_librarySearchRetryVersion.HasValue || _librarySearchQuery is null)
                return;
            await ShowLibrarySearchResultsAsync(_librarySearchQuery, _librarySearchOnlyServer, isRetry: true);
            return;
        }
        if (_unifiedLibraryRetryVersion.HasValue ||
            _unifiedLibraryLoadResult?.Status is not (LibraryLoadStatus.Partial or LibraryLoadStatus.Failed) ||
            _currentTopLevelTag is not ("Artists" or "Albums" or "Tracks") ||
            !CanReloadCurrentViewAfterLibraryChange())
            return;

        var version = _unifiedLibraryLoadVersion + 1;
        _unifiedLibraryRetryVersion = version;
        UpdateUnifiedLibraryLoadNotice();
        try
        {
            await BindLocalRowsAndStartRemoteAppendAsync(_currentTopLevelTag, preservePosition: true);
        }
        finally
        {
            if (_unifiedLibraryRetryVersion == version)
            {
                _unifiedLibraryRetryVersion = null;
                UpdateUnifiedLibraryLoadNotice();
            }
        }
    }
}
