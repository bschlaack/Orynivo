using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>Edits the persisted profile used for new and active Infinite Mix queues.</summary>
public partial class InfiniteMixDialog : Window
{
    private readonly IReadOnlyList<OrynivoServerSettings> _servers;
    private readonly InfiniteMixSettings _settings;
    private readonly Dictionary<string, CheckBox> _serverChecks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ObservableCollection<string> _includedGenres = [];
    private readonly ObservableCollection<string> _excludedGenres = [];
    private readonly List<string> _availableGenres = [];
    private CancellationTokenSource? _genreLoadCts;

    /// <summary>Gets the confirmed settings snapshot, or <see langword="null"/> when cancelled.</summary>
    public InfiniteMixSettings? Result { get; private set; }

    /// <summary>Initializes an empty design/runtime-loader instance.</summary>
    public InfiniteMixDialog() : this(new InfiniteMixSettings(), [])
    {
    }

    /// <summary>Initializes the dialog from the current profile and configured servers.</summary>
    /// <param name="settings">Current Infinite Mix profile.</param>
    /// <param name="servers">Configured Orynivo Servers.</param>
    public InfiniteMixDialog(InfiniteMixSettings settings, IReadOnlyList<OrynivoServerSettings> servers)
    {
        InitializeComponent();
        _settings = settings;
        _servers = servers;
        MoodComboBox.ItemsSource = new[]
        {
            LocalizationManager.Current.InfiniteMixMoodCalm,
            LocalizationManager.Current.InfiniteMixMoodBalanced,
            LocalizationManager.Current.InfiniteMixMoodEnergetic
        };
        MoodComboBox.SelectedIndex = (int)settings.Mood;
        DiscoverySlider.Value = Math.Clamp(settings.DiscoveryLevel, 0, 100);
        DiscoveryValueText.Text = $"{DiscoverySlider.Value:0} %";
        PeriodComboBox.ItemsSource = new[] { 3, 7, 30, 90 };
        PeriodComboBox.SelectedItem = new[] { 3, 7, 30, 90 }.Contains(settings.HistoryDays) ? settings.HistoryDays : 30;
        WeightFavoritesCheckBox.IsChecked = settings.WeightFavorites;
        PreferRareCheckBox.IsChecked = settings.PreferRareTracks;
        foreach (var genre in settings.IncludedGenres.Distinct(StringComparer.CurrentCultureIgnoreCase))
            _includedGenres.Add(genre);
        foreach (var genre in settings.ExcludedGenres.Distinct(StringComparer.CurrentCultureIgnoreCase))
            _excludedGenres.Add(genre);
        IncludedGenreChips.ItemsSource = _includedGenres;
        ExcludedGenreChips.ItemsSource = _excludedGenres;

        AddSourceCheck(LocalizationManager.Current.LocalSource, "local", settings.IncludeLocalLibrary);
        foreach (var server in servers)
        {
            var enabled = !settings.ServerSelectionConfigured || settings.EnabledServerIds.Contains(server.Id);
            AddSourceCheck(server.Name, server.Id, enabled);
        }
        Opened += InfiniteMixDialog_OnOpened;
        Closed += (_, _) => _genreLoadCts?.Cancel();
    }

    private void AddSourceCheck(string label, string id, bool enabled)
    {
        var checkBox = new CheckBox { Content = label, IsChecked = enabled };
        checkBox.IsCheckedChanged += SourceCheckBox_OnChanged;
        SourcesPanel.Children.Add(checkBox);
        _serverChecks[id] = checkBox;
    }

    private async void InfiniteMixDialog_OnOpened(object? sender, EventArgs e)
    {
        WindowChrome.ApplyTheme(this);
        await LoadAvailableGenresAsync();
    }

    private async void SourceCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (IsVisible)
            await LoadAvailableGenresAsync();
    }

    private async Task LoadAvailableGenresAsync()
    {
        _genreLoadCts?.Cancel();
        _genreLoadCts?.Dispose();
        _genreLoadCts = new CancellationTokenSource();
        var cancellationToken = _genreLoadCts.Token;
        try
        {
            var localTask = _serverChecks.GetValueOrDefault("local")?.IsChecked == true
                ? Task.Run(() =>
                {
                    using var database = AudioDatabase.OpenDefault();
                    return database.GetTrackFacets().Select(facet => facet.Genre).ToList();
                }, cancellationToken)
                : Task.FromResult(new List<string?>());
            var selectedServers = _servers.Where(server =>
                _serverChecks.GetValueOrDefault(server.Id)?.IsChecked == true).ToList();
            var remoteTasks = selectedServers.Select(server => LoadServerGenresAsync(server, cancellationToken));
            var remoteGenres = await Task.WhenAll(remoteTasks);
            var genres = (await localTask).Concat(remoteGenres.SelectMany(value => value))
                .SelectMany(SplitGenreText)
                .Concat(_includedGenres)
                .Concat(_excludedGenres)
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            cancellationToken.ThrowIfCancellationRequested();
            _availableGenres.Clear();
            _availableGenres.AddRange(genres);
            UpdateSuggestions(IncludedGenreInput, IncludedGenreSuggestions, _includedGenres);
            UpdateSuggestions(ExcludedGenreInput, ExcludedGenreSuggestions, _excludedGenres);
        }
        catch (OperationCanceledException)
        {
            // A newer source selection superseded this load.
        }
    }

    private static async Task<IReadOnlyList<string?>> LoadServerGenresAsync(
        OrynivoServerSettings server,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new OrynivoServerClient();
            return (await client.GetTrackFacetsAsync(server, cancellationToken))
                .Select(facet => facet.Genre).ToList();
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    private static IEnumerable<string> SplitGenreText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void DiscoverySlider_OnValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        => DiscoveryValueText.Text = $"{e.NewValue:0} %";

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var enabledServers = _servers
            .Where(server => _serverChecks.TryGetValue(server.Id, out var check) && check.IsChecked == true)
            .Select(server => server.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Result = new InfiniteMixSettings
        {
            Mood = (InfiniteMixMood)Math.Clamp(MoodComboBox.SelectedIndex, 0, 2),
            DiscoveryLevel = (int)Math.Round(DiscoverySlider.Value),
            HistoryDays = PeriodComboBox.SelectedItem is int days ? days : 30,
            IncludeLocalLibrary = _serverChecks["local"].IsChecked == true,
            EnabledServerIds = enabledServers,
            ServerSelectionConfigured = true,
            WeightFavorites = WeightFavoritesCheckBox.IsChecked == true,
            PreferRareTracks = PreferRareCheckBox.IsChecked == true,
            IncludedGenres = _includedGenres.ToList(),
            ExcludedGenres = _excludedGenres.ToList(),
            GenreFeedback = new Dictionary<string, int>(_settings.GenreFeedback, StringComparer.OrdinalIgnoreCase),
            ExcludedTrackKeys = new HashSet<string>(_settings.ExcludedTrackKeys, StringComparer.OrdinalIgnoreCase)
        };
        Close(true);
    }

    private void IncludedGenreInput_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateSuggestions(IncludedGenreInput, IncludedGenreSuggestions, _includedGenres);

    private void ExcludedGenreInput_OnTextChanged(object? sender, TextChangedEventArgs e) =>
        UpdateSuggestions(ExcludedGenreInput, ExcludedGenreSuggestions, _excludedGenres);

    private void UpdateSuggestions(TextBox input, ListBox suggestions, IReadOnlyCollection<string> selected)
    {
        var query = input.Text?.Trim() ?? string.Empty;
        var matches = string.IsNullOrWhiteSpace(query)
            ? []
            : _availableGenres.Where(genre =>
                    genre.Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
                    !selected.Contains(genre, StringComparer.CurrentCultureIgnoreCase))
                .Take(8).ToList();
        suggestions.ItemsSource = matches;
        suggestions.IsVisible = matches.Count > 0;
    }

    private void IncludedGenreInput_OnKeyDown(object? sender, KeyEventArgs e) =>
        HandleGenreInputKey(e, IncludedGenreInput, IncludedGenreSuggestions, true);

    private void ExcludedGenreInput_OnKeyDown(object? sender, KeyEventArgs e) =>
        HandleGenreInputKey(e, ExcludedGenreInput, ExcludedGenreSuggestions, false);

    private void HandleGenreInputKey(KeyEventArgs e, TextBox input, ListBox suggestions, bool included)
    {
        if (e.Key == Key.Enter)
        {
            var firstSuggestion = (suggestions.ItemsSource as IEnumerable<string>)?.FirstOrDefault();
            AddGenre(included, suggestions.SelectedItem as string ?? firstSuggestion ?? input.Text);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            suggestions.IsVisible = false;
            e.Handled = true;
        }
    }

    private void AddIncludedGenreButton_OnClick(object? sender, RoutedEventArgs e) =>
        AddGenre(true, IncludedGenreInput.Text);

    private void AddExcludedGenreButton_OnClick(object? sender, RoutedEventArgs e) =>
        AddGenre(false, ExcludedGenreInput.Text);

    private void IncludedGenreSuggestions_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (IncludedGenreSuggestions.SelectedItem is string genre)
            AddGenre(true, genre);
    }

    private void ExcludedGenreSuggestions_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ExcludedGenreSuggestions.SelectedItem is string genre)
            AddGenre(false, genre);
    }

    private void AddGenre(bool included, string? value)
    {
        var genre = value?.Trim().TrimEnd(',', ';').Trim();
        if (string.IsNullOrWhiteSpace(genre))
            return;
        var target = included ? _includedGenres : _excludedGenres;
        var opposite = included ? _excludedGenres : _includedGenres;
        var oppositeMatch = opposite.FirstOrDefault(item =>
            string.Equals(item, genre, StringComparison.CurrentCultureIgnoreCase));
        if (oppositeMatch is not null)
            opposite.Remove(oppositeMatch);
        if (!target.Contains(genre, StringComparer.CurrentCultureIgnoreCase))
            target.Add(genre);
        var input = included ? IncludedGenreInput : ExcludedGenreInput;
        var suggestions = included ? IncludedGenreSuggestions : ExcludedGenreSuggestions;
        input.Text = string.Empty;
        suggestions.SelectedItem = null;
        suggestions.IsVisible = false;
    }

    private void RemoveIncludedGenreButton_OnClick(object? sender, RoutedEventArgs e) =>
        RemoveGenre(_includedGenres, sender);

    private void RemoveExcludedGenreButton_OnClick(object? sender, RoutedEventArgs e) =>
        RemoveGenre(_excludedGenres, sender);

    private static void RemoveGenre(ObservableCollection<string> genres, object? sender)
    {
        if (sender is Button { Tag: string genre })
        {
            var match = genres.FirstOrDefault(item =>
                string.Equals(item, genre, StringComparison.CurrentCultureIgnoreCase));
            if (match is not null)
                genres.Remove(match);
        }
    }
}
