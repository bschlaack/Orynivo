using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Dialog for manually searching and selecting album cover artwork.</summary>
public partial class CoverSearchWindow : Window
{

    private readonly ObservableCollection<CoverSearchResultViewModel> _results = [];
    private readonly DispatcherTimer _busyTimer;
    private readonly string[] _busyFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private int _busyFrameIndex;
    private CancellationTokenSource? _searchCancellation;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _selecting;
    private bool _closed;
    private (string Album, string Artist) _activeQuery;

    /// <summary>Selected original artwork, populated only after a successful original download.</summary>
    public CoverSearchResult? SelectedResult { get; private set; }

    /// <summary>
    /// Initializes a runtime-loader instance with an empty album query.
    /// </summary>
    public CoverSearchWindow()
        : this(string.Empty, null)
    {
    }

    /// <summary>Initializes a new cover-search dialog with editable album and artist queries.</summary>
    /// <param name="albumTitle">Initial album title query.</param>
    /// <param name="artistName">Initial artist query, or <see langword="null"/>.</param>
    public CoverSearchWindow(string albumTitle, string? artistName = null)
    {
        InitializeComponent();
        QueryTextBox.Text = albumTitle;
        ArtistQueryTextBox.Text = artistName;
        ResultsListBox.ItemsSource = _results;
        _busyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _busyTimer.Tick += (_, _) =>
        {
            _busyFrameIndex = (_busyFrameIndex + 1) % _busyFrames.Length;
            BusyIndicatorTextBlock.Text = _busyFrames[_busyFrameIndex];
        };
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
        Loaded += async (_, _) =>
        {
            QueryTextBox.Focus();
            QueryTextBox.SelectAll();
            await SearchAsync();
        };
        QueryTextBox.KeyDown += SearchTextBox_OnKeyDown;
        ArtistQueryTextBox.KeyDown += SearchTextBox_OnKeyDown;
        Closed += (_, _) =>
        {
            _closed = true;
            _searchCancellation?.Cancel();
            _lifetime.Cancel();
            _lifetime.Dispose();
            _busyTimer.Stop();
            ClearResults();
        };
    }

    private async void SearchAgainButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchAsync();

    private async void SearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        if (_closed || _selecting)
            return;
        var query = (QueryTextBox.Text ?? string.Empty, ArtistQueryTextBox.Text ?? string.Empty);
        if (_searchCancellation is not null && _activeQuery == query)
            return;
        _activeQuery = query;
        // A new query supersedes the previous one; stale callbacks must never touch the UI.
        _searchCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchCancellation = cancellation;
        ClearResults();
        var elapsed = Stopwatch.StartNew();
        BusyIndicatorTextBlock.IsVisible = true;
        StatusTextBlock.Text = LocalizationManager.Current.CoverSearchRunning;
        _busyTimer.Start();
        try
        {
            await MusicBrainzCoverSearch.SearchByAlbumTitleAsync(
                query.Item1,
                query.Item2, cancellation.Token, async result =>
                {
                    CoverSearchDiagnostics.Record("preview-downloaded", elapsed.ElapsedMilliseconds, bytes: result.ImageData.Length);
                    var decode = Stopwatch.StartNew();
                    var bitmap = await Task.Run(() =>
                    {
                        try { return CreateBitmap(result.ImageData); }
                        catch (Exception ex) { throw new InvalidDataException("Invalid preview.", ex); }
                    }, cancellation.Token);
                    var decodeMs = decode.ElapsedMilliseconds;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_closed || cancellation.IsCancellationRequested || _searchCancellation != cancellation)
                        {
                            bitmap.Dispose();
                            return;
                        }
                        _results.Add(new CoverSearchResultViewModel(result, bitmap));
                        CoverSearchDiagnostics.Record("preview", elapsed.ElapsedMilliseconds, decodeMs, result.ImageData.Length);
                    });
                });

            if (_searchCancellation == cancellation && !_closed && !_selecting)
                StatusTextBlock.Text = _results.Count == 0
                    ? LocalizationManager.Current.CoverSearchNoResults
                    : string.Empty;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception)
        {
            if (_searchCancellation == cancellation && !_closed && !_selecting)
                StatusTextBlock.Text = LocalizationManager.Current.CoverSearchFailed;
        }
        finally
        {
            CoverSearchDiagnostics.Record("search-complete", elapsed.ElapsedMilliseconds);
            if (_searchCancellation == cancellation)
            {
                _searchCancellation = null;
                if (!_closed && !_selecting)
                {
                    _busyTimer.Stop();
                    BusyIndicatorTextBlock.IsVisible = false;
                }
            }
        }
    }

    private async void UseSelectedCoverButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_closed || _selecting || ResultsListBox.SelectedItem is not CoverSearchResultViewModel selected)
            return;

        _selecting = true;
        _searchCancellation?.Cancel();
        SearchButton.IsEnabled = UseCoverButton.IsEnabled = false;
        StatusTextBlock.Text = LocalizationManager.Current.CoverOriginalLoading;
        BusyIndicatorTextBlock.IsVisible = true;
        _busyTimer.Start();
        var timer = Stopwatch.StartNew();
        try
        {
            var original = await MusicBrainzCoverSearch.DownloadOriginalAsync(selected.Result, _lifetime.Token);
            // Reject corrupt originals before the caller persists them.
            await Task.Run(() => { using var image = CreateBitmap(original.ImageData); }, _lifetime.Token);
            if (!_closed)
            {
                SelectedResult = original;
                Close(true);
            }
        }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception)
        {
            if (!_closed)
                StatusTextBlock.Text = LocalizationManager.Current.CoverSearchFailed;
        }
        finally
        {
            CoverSearchDiagnostics.Record("original-complete", timer.ElapsedMilliseconds);
            _selecting = false;
            if (!_closed)
            {
                SearchButton.IsEnabled = UseCoverButton.IsEnabled = true;
                _busyTimer.Stop();
                BusyIndicatorTextBlock.IsVisible = false;
            }
        }
    }

    private void ClearResults()
    {
        foreach (var result in _results)
            result.Image.Dispose();
        _results.Clear();
    }

    private static Bitmap CreateBitmap(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Bitmap.DecodeToWidth(stream, 250);
    }
}

/// <summary>
/// Presentation wrapper for one CoverSearchWindow result. It lives outside the window so XAML
/// can name it in an <c>x:DataType</c> directive, which compiled bindings require
/// (a nested private type cannot be referenced from XAML).
/// </summary>
internal sealed record CoverSearchResultViewModel(CoverSearchResult Result, Bitmap Image)
{
    public string Title => Result.Title;
    public string? Artist => Result.Artist;
}
