using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Collects the name, filters, history rules, ordering, and result limit for a smart playlist.
/// </summary>
public partial class SmartPlaylistDialog : Window
{
    private readonly SmartPlaylistCriteria _initialCriteria;
    private DispatcherTimer? _previewTimer;
    private CancellationTokenSource? _previewCts;

    /// <summary>Gets the confirmed playlist name.</summary>
    public string? PlaylistName { get; private set; }

    /// <summary>Gets the confirmed smart-playlist criteria.</summary>
    public SmartPlaylistCriteria? Criteria { get; private set; }

    /// <summary>
    /// Gets or sets an optional resolver that counts how many tracks match the current criteria,
    /// used for the live preview. When <see langword="null"/> the preview line stays hidden
    /// (e.g. for remote smart playlists that cannot be resolved ad hoc). Set by the caller
    /// before the dialog is shown.
    /// </summary>
    public Func<SmartPlaylistCriteria, CancellationToken, Task<int?>>? CountResolver { get; set; }

    /// <summary>
    /// Initializes a runtime-loader instance with empty criteria.
    /// </summary>
    public SmartPlaylistDialog()
        : this(new SmartPlaylistCriteria(), string.Empty)
    {
    }

    /// <summary>
    /// Initializes the dialog with the current name and criteria of an existing smart playlist.
    /// </summary>
    /// <param name="initialCriteria">Criteria used to prepopulate the dialog.</param>
    /// <param name="playlistName">Current smart-playlist display name.</param>
    public SmartPlaylistDialog(SmartPlaylistCriteria initialCriteria, string playlistName)
    {
        _initialCriteria = initialCriteria;
        InitializeComponent();
        NameTextBox.Text = playlistName;
        CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(playlistName);
        PopulateFields();
        WirePreviewTriggers();
        Opened += (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            NameTextBox.Focus();
            SchedulePreview();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
    }

    private void PopulateFields()
    {
        FavoritesOnlyCheckBox.IsChecked = _initialCriteria.FavoritesOnly;
        SearchTextTextBox.Text = _initialCriteria.SearchText;
        GenresTextBox.Text = string.Join(", ", _initialCriteria.Genres);
        FormatsTextBox.Text = string.Join(", ", _initialCriteria.Formats);
        BitratesTextBox.Text = string.Join(", ", _initialCriteria.Bitrates);
        SourcesTextBox.Text = string.Join(", ", _initialCriteria.SourceKeys);
        MinimumYearTextBox.Text = FormatNumber(_initialCriteria.MinimumYear);
        MaximumYearTextBox.Text = FormatNumber(_initialCriteria.MaximumYear);
        ArtistTextBox.Text = _initialCriteria.ArtistContains;
        AlbumTextBox.Text = _initialCriteria.AlbumContains;
        MinimumDurationTextBox.Text = FormatNumber(_initialCriteria.MinimumDurationSeconds / 60d);
        MaximumDurationTextBox.Text = FormatNumber(_initialCriteria.MaximumDurationSeconds / 60d);
        AddedWithinDaysTextBox.Text = FormatNumber(_initialCriteria.AddedWithinDays);
        PlayedWithinDaysTextBox.Text = FormatNumber(_initialCriteria.PlayedWithinDays);
        NeverPlayedCheckBox.IsChecked = _initialCriteria.NeverPlayed;
        MinimumPlayCountTextBox.Text = FormatNumber(_initialCriteria.MinimumPlayCount);
        MaximumPlayCountTextBox.Text = FormatNumber(_initialCriteria.MaximumPlayCount);
        ResultLimitTextBox.Text = FormatNumber(_initialCriteria.ResultLimit);

        SortOrderComboBox.ItemsSource = new[]
        {
            new SortOrderOption(SmartPlaylistSortOrder.Title, LocalizationManager.Current.SmartPlaylistSortTitle),
            new SortOrderOption(SmartPlaylistSortOrder.Random, LocalizationManager.Current.SmartPlaylistSortRandom),
            new SortOrderOption(SmartPlaylistSortOrder.LastPlayedNewest, LocalizationManager.Current.SmartPlaylistSortLastPlayed),
            new SortOrderOption(SmartPlaylistSortOrder.LeastRecentlyPlayed, LocalizationManager.Current.SmartPlaylistSortLeastRecentlyPlayed)
        };
        SortOrderComboBox.SelectedItem = ((IEnumerable<SortOrderOption>)SortOrderComboBox.ItemsSource)
            .First(option => option.Value == _initialCriteria.SortOrder);
    }

    private void NameTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
        => CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void CreateButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!TryBuildCriteria(out var criteria))
            return;

        PlaylistName = NameTextBox.Text?.Trim();
        Criteria = criteria;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private bool TryBuildCriteria(out SmartPlaylistCriteria criteria)
    {
        if (TryBuildCriteriaCore(out criteria))
        {
            ValidationTextBlock.IsVisible = false;
            return true;
        }

        ValidationTextBlock.Text = LocalizationManager.Current.InvalidSmartPlaylistCriteria;
        ValidationTextBlock.IsVisible = true;
        return false;
    }

    private bool TryBuildCriteriaCore(out SmartPlaylistCriteria criteria)
    {
        criteria = new SmartPlaylistCriteria();
        if (!TryOptionalInt(MinimumYearTextBox.Text, out var minimumYear) ||
            !TryOptionalInt(MaximumYearTextBox.Text, out var maximumYear) ||
            !TryOptionalDouble(MinimumDurationTextBox.Text, out var minimumDurationMinutes) ||
            !TryOptionalDouble(MaximumDurationTextBox.Text, out var maximumDurationMinutes) ||
            !TryOptionalInt(AddedWithinDaysTextBox.Text, out var addedWithinDays) ||
            !TryOptionalInt(PlayedWithinDaysTextBox.Text, out var playedWithinDays) ||
            !TryOptionalInt(MinimumPlayCountTextBox.Text, out var minimumPlayCount) ||
            !TryOptionalInt(MaximumPlayCountTextBox.Text, out var maximumPlayCount) ||
            !TryOptionalInt(ResultLimitTextBox.Text, out var resultLimit) ||
            !TryIntegerList(BitratesTextBox.Text, out var bitrates) ||
            !IsPositive(minimumYear) ||
            !IsPositive(maximumYear) ||
            !IsNonNegative(minimumDurationMinutes) ||
            !IsNonNegative(maximumDurationMinutes) ||
            !IsPositive(addedWithinDays) ||
            !IsPositive(playedWithinDays) ||
            !IsNonNegative(minimumPlayCount) ||
            !IsNonNegative(maximumPlayCount) ||
            !IsPositive(resultLimit) ||
            minimumYear > maximumYear ||
            minimumDurationMinutes > maximumDurationMinutes ||
            minimumPlayCount > maximumPlayCount ||
            (NeverPlayedCheckBox.IsChecked == true &&
             (playedWithinDays.HasValue || minimumPlayCount is > 0)))
        {
            return false;
        }

        criteria = new SmartPlaylistCriteria
        {
            FavoritesOnly = FavoritesOnlyCheckBox.IsChecked == true,
            SearchText = NullIfWhiteSpace(SearchTextTextBox.Text),
            Genres = ParseStringList(GenresTextBox.Text),
            Formats = ParseStringList(FormatsTextBox.Text)
                .Select(value => value.ToLowerInvariant())
                .ToList(),
            Bitrates = bitrates,
            SourceKeys = ParseStringList(SourcesTextBox.Text)
                .Select(value => value.ToLowerInvariant())
                .ToList(),
            MinimumYear = minimumYear,
            MaximumYear = maximumYear,
            ArtistContains = NullIfWhiteSpace(ArtistTextBox.Text),
            AlbumContains = NullIfWhiteSpace(AlbumTextBox.Text),
            MinimumDurationSeconds = minimumDurationMinutes * 60d,
            MaximumDurationSeconds = maximumDurationMinutes * 60d,
            AddedWithinDays = addedWithinDays,
            PlayedWithinDays = playedWithinDays,
            NeverPlayed = NeverPlayedCheckBox.IsChecked == true,
            MinimumPlayCount = minimumPlayCount,
            MaximumPlayCount = maximumPlayCount,
            SortOrder = (SortOrderComboBox.SelectedItem as SortOrderOption)?.Value
                        ?? SmartPlaylistSortOrder.Title,
            ResultLimit = resultLimit
        };
        return true;
    }

    /// <summary>Subscribes every criteria input to the debounced live-preview refresh.</summary>
    private void WirePreviewTriggers()
    {
        TextBox[] textBoxes =
        [
            SearchTextTextBox, GenresTextBox, FormatsTextBox, BitratesTextBox, SourcesTextBox,
            MinimumYearTextBox, MaximumYearTextBox, ArtistTextBox, AlbumTextBox,
            MinimumDurationTextBox, MaximumDurationTextBox, AddedWithinDaysTextBox,
            PlayedWithinDaysTextBox, MinimumPlayCountTextBox, MaximumPlayCountTextBox, ResultLimitTextBox
        ];
        foreach (var box in textBoxes)
            box.TextChanged += (_, _) => SchedulePreview();

        FavoritesOnlyCheckBox.IsCheckedChanged += (_, _) => SchedulePreview();
        NeverPlayedCheckBox.IsCheckedChanged += (_, _) => SchedulePreview();
        SortOrderComboBox.SelectionChanged += (_, _) => SchedulePreview();
    }

    /// <summary>Restarts the debounce timer so the preview updates shortly after typing stops.</summary>
    private void SchedulePreview()
    {
        if (CountResolver is null)
            return;
        _previewTimer ??= CreatePreviewTimer();
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private DispatcherTimer CreatePreviewTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = UpdatePreviewAsync();
        };
        return timer;
    }

    /// <summary>Resolves the current criteria to a match count and shows it in the preview line.</summary>
    /// <returns>A task representing the asynchronous preview refresh.</returns>
    private async Task UpdatePreviewAsync()
    {
        if (CountResolver is null)
        {
            PreviewTextBlock.IsVisible = false;
            return;
        }

        PreviewTextBlock.IsVisible = true;
        if (!TryBuildCriteriaCore(out var criteria))
        {
            PreviewTextBlock.Text = LocalizationManager.Current.SmartPlaylistPreviewInvalid;
            return;
        }

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        PreviewTextBlock.Text = LocalizationManager.Current.SmartPlaylistPreviewComputing;
        try
        {
            var count = await CountResolver(criteria, ct);
            if (ct.IsCancellationRequested)
                return;
            PreviewTextBlock.Text = count.HasValue
                ? string.Format(LocalizationManager.Current.SmartPlaylistPreviewCount, count.Value)
                : string.Empty;
        }
        catch (OperationCanceledException) { }
        catch
        {
            PreviewTextBlock.Text = string.Empty;
        }
    }

    private static List<string> ParseStringList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

    private static bool TryIntegerList(string? value, out List<int> result)
    {
        result = [];
        foreach (var item in ParseStringList(value))
        {
            if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number) ||
                number <= 0)
                return false;
            result.Add(number);
        }
        result = result.Distinct().Order().ToList();
        return true;
    }

    private static bool TryOptionalInt(string? value, out int? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed))
            return false;
        result = parsed;
        return true;
    }

    private static bool TryOptionalDouble(string? value, out double? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed))
            return false;
        result = parsed;
        return true;
    }

    private static bool IsNonNegative(int? value) => !value.HasValue || value.Value >= 0;

    private static bool IsNonNegative(double? value) => !value.HasValue || value.Value >= 0;

    private static bool IsPositive(int? value) => !value.HasValue || value.Value > 0;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FormatNumber<T>(T? value) where T : struct, IFormattable =>
        value?.ToString(null, CultureInfo.CurrentCulture);

    /// <summary>
    /// Associates a persisted sort value with its localized display label.
    /// </summary>
    /// <param name="Value">Persisted sort value.</param>
    /// <param name="Label">Localized display label.</param>
    private sealed record SortOrderOption(SmartPlaylistSortOrder Value, string Label)
    {
        /// <summary>Returns the localized display label.</summary>
        /// <returns>The localized display label.</returns>
        public override string ToString() => Label;
    }
}
