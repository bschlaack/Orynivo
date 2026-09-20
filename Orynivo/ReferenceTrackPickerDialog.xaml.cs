using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Small search dialog that lets the smart-playlist editor replace the similarity
/// reference track. The caller supplies the search callback, so the dialog never
/// touches the database or the network itself.
/// </summary>
public partial class ReferenceTrackPickerDialog : Window
{
    private DispatcherTimer? _searchTimer;
    private CancellationTokenSource? _searchCts;

    /// <summary>Initializes a runtime-loader instance.</summary>
    public ReferenceTrackPickerDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            SearchTextBox.Focus();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    /// <summary>One selectable reference track.</summary>
    /// <param name="Label">Display label, for example <c>Title — Artist</c>.</param>
    /// <param name="SourceKey">Credential-free provider key such as <c>local</c> or <c>server:&lt;id&gt;</c>.</param>
    /// <param name="TrackId">Provider-local track identifier.</param>
    public sealed record Candidate(string Label, string SourceKey, long TrackId)
    {
        /// <summary>Returns the display label.</summary>
        /// <returns>The display label.</returns>
        public override string ToString() => Label;
    }

    /// <summary>Gets or sets the search callback supplied by the caller.</summary>
    public Func<string, CancellationToken, Task<IReadOnlyList<Candidate>>>? Search { get; set; }

    /// <summary>Gets the confirmed reference track, or <see langword="null"/> when the user cancelled.</summary>
    public Candidate? Selected { get; private set; }

    private void SearchTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (Search is null)
            return;
        _searchTimer ??= CreateSearchTimer();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private DispatcherTimer CreateSearchTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RunSearchAsync();
        };
        return timer;
    }

    private async Task RunSearchAsync()
    {
        if (Search is null)
            return;

        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            ResultsListBox.ItemsSource = null;
            ShowStatus(LocalizationManager.Current.ReferenceTrackSearchHint);
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        ShowStatus(LocalizationManager.Current.ReferenceTrackSearching);
        try
        {
            var results = await Search(query, token);
            if (token.IsCancellationRequested)
                return;
            ResultsListBox.ItemsSource = results;
            if (results.Count == 0)
                ShowStatus(LocalizationManager.Current.ReferenceTrackNotFound);
            else
                StatusTextBlock.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ResultsListBox.ItemsSource = null;
            ShowStatus(LocalizationManager.Current.ReferenceTrackNotFound);
        }
    }

    private void ShowStatus(string text)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.IsVisible = true;
    }

    private void ResultsListBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        UseButton.IsEnabled = ResultsListBox.SelectedItem is Candidate;

    private void ResultsListBox_OnDoubleTapped(object? sender, TappedEventArgs e) => Confirm();

    private void UseButton_OnClick(object? sender, RoutedEventArgs e) => Confirm();

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void Confirm()
    {
        if (ResultsListBox.SelectedItem is not Candidate candidate)
            return;
        Selected = candidate;
        Close(true);
    }
}
