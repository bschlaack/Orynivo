using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Podcast catalog search, filtering, episode browsing, and artwork for
/// <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Podcasts
    // ------------------------------------------------------------------

    private Task EnsurePodcastFilterCatalogAsync()
    {
        BuildPodcastCategoryFilter();
        BuildPodcastLanguageFilter();
        if (_podcastFilterCatalogLoading ||
            (CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastCategoriesUpdatedAt) &&
             CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastLanguagesUpdatedAt)))
            return Task.CompletedTask;

        _podcastFilterCatalogLoading = true;
        _ = RefreshPodcastFilterCatalogAsync();
        return Task.CompletedTask;
    }

    private async Task RefreshPodcastFilterCatalogAsync()
    {
        try
        {
            if (!CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastCategoriesUpdatedAt))
            {
                var categories = await _podcastService.GetCategoryCatalogAsync();
                var options = categories
                    .Select(category => new CatalogFilterOption(
                        category.Name,
                        Key: category.Id))
                    .ToList();
                _podcastCategoryCatalog.Clear();
                _podcastCategoryCatalog.AddRange(options);
                _catalogFilterCacheData.PodcastCategories = options;
                _catalogFilterCacheData.PodcastCategoriesUpdatedAt = DateTimeOffset.UtcNow;
                _catalogFilterCache.Save(_catalogFilterCacheData);
                BuildPodcastCategoryFilter();
            }

            if (!CatalogFilterCache.IsFresh(_catalogFilterCacheData.PodcastLanguagesUpdatedAt))
            {
                var feedUrls = (await _podcastService.GetPopularFeedUrlsAsync()).ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
                try
                {
                    using var db = AudioDatabase.OpenDefault();
                    foreach (var podcast in db.GetPodcasts())
                        feedUrls.Add(podcast.FeedUrl);
                }
                catch
                {
                }

                var languages = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(
                    StringComparer.OrdinalIgnoreCase);
                await Parallel.ForEachAsync(
                    feedUrls,
                    new ParallelOptions { MaxDegreeOfParallelism = 10 },
                    async (feedUrl, token) =>
                    {
                        var language = await _podcastService.GetFeedLanguageAsync(feedUrl, token);
                        if (language is not null)
                            languages.TryAdd(language, 0);
                    });
                MergePodcastLanguagesIntoCache(languages.Keys);
                _catalogFilterCacheData.PodcastLanguagesUpdatedAt = DateTimeOffset.UtcNow;
                _catalogFilterCache.Save(_catalogFilterCacheData);
                BuildPodcastLanguageFilter();
            }
        }
        catch
        {
            // Existing cached filter data remains usable.
        }
        finally
        {
            _podcastFilterCatalogLoading = false;
        }
    }

    private void MergePodcastLanguagesIntoCache(IEnumerable<string> languages)
    {
        var values = _podcastLanguageCatalog
            .Select(option => option.Value)
            .Concat(languages)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(FormatPodcastLanguage, StringComparer.CurrentCultureIgnoreCase)
            .Select(language => new CatalogFilterOption(language))
            .ToList();
        _podcastLanguageCatalog.Clear();
        _podcastLanguageCatalog.AddRange(values);
        _catalogFilterCacheData.PodcastLanguages = values;
    }

    private async void PodcastSearchButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchPodcastsAsync();

    private async void PodcastSearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await SearchPodcastsAsync();
    }

    private async Task SearchPodcastsAsync()
    {
        CancelAndDispose(ref _podcastSearchCts);
        _podcastSearchCts = new CancellationTokenSource();
        var cancellationToken = _podcastSearchCts.Token;

        if (string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text) &&
            _selectedPodcastCategories.Count == 0 &&
            _selectedPodcastLanguages.Count == 0)
        {
            _podcastSearchResults.Clear();
            PodcastsDataGrid.ItemsSource = null;
            PodcastStatusTextBlock.Text = string.Empty;
            PodcastStatusTextBlock.IsVisible = false;
            ContentCountTextBlock.Text = string.Empty;
            BuildPodcastCategoryFilter();
            BuildPodcastLanguageFilter();
            return;
        }

        PodcastsDataGrid.ItemsSource = null;
        PodcastStatusTextBlock.IsVisible = true;
        PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastLoading;
        try
        {
            var selectedGenreIds = _podcastCategoryCatalog
                .Where(option => _selectedPodcastCategories.Contains(option.Value))
                .Select(option => option.Key)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Cast<string>()
                .ToList();
            var hasSearch = !string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text);
            IReadOnlyList<PodcastSearchResult> podcasts;
            if (!hasSearch &&
                selectedGenreIds.Count == 0 &&
                _selectedPodcastLanguages.Count > 0)
            {
                podcasts = await _podcastService.GetPopularPodcastsAsync(cancellationToken);
            }
            else
            {
                podcasts = await _podcastService.SearchAsync(
                    PodcastSearchTextBox.Text,
                    selectedGenreIds,
                    cancellationToken);
            }
            var rows = podcasts.Select(podcast => new PodcastViewModel
            {
                CollectionId = podcast.CollectionId,
                Name = podcast.Name,
                Author = podcast.Author,
                FeedUrl = podcast.FeedUrl,
                ArtworkUrl = podcast.ArtworkUrl,
                Genre = podcast.Genre,
                Genres = podcast.Genres,
                GenreIds = podcast.GenreIds,
                Language = podcast.Language
            }).ToList();
            _podcastSearchResults.Clear();
            _podcastSearchResults.AddRange(rows);
            _podcastLanguagesLoading = rows.Count > 0;
            if (rows.Count == 0)
                PrunePodcastLanguageSelection();
            BuildPodcastCategoryFilter();
            BuildPodcastLanguageFilter();
            ApplyPodcastFilters();

            if (rows.Count > 0)
            {
                PodcastStatusTextBlock.IsVisible = true;
                PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastLanguagesLoading;
                await Parallel.ForEachAsync(
                    rows,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 6,
                        CancellationToken = cancellationToken
                    },
                    async (row, token) =>
                    {
                        row.Language = await _podcastService.GetFeedLanguageAsync(
                            row.FeedUrl,
                            token);
                    });
                var availableLanguages = rows
                    .Select(row => row.Language)
                    .Where(language => language is not null)
                    .Cast<string>()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                MergePodcastLanguagesIntoCache(availableLanguages);
                _catalogFilterCache.Save(_catalogFilterCacheData);
                _podcastLanguagesLoading = false;
                PrunePodcastLanguageSelection();
                BuildPodcastLanguageFilter();
                ApplyPodcastFilters();
            }
        }
        catch (OperationCanceledException)
        {
            _podcastLanguagesLoading = false;
        }
        catch
        {
            _podcastLanguagesLoading = false;
            PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastSearchFailed;
        }
    }

    private void PodcastCategoryFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BuildPodcastCategoryFilter();
        PodcastCategoryFilterPopup.IsOpen = !PodcastCategoryFilterPopup.IsOpen;
    }

    private void PodcastLanguageFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BuildPodcastLanguageFilter();
        PodcastLanguageFilterPopup.IsOpen = !PodcastLanguageFilterPopup.IsOpen;
    }

    private async void ClearPodcastCategoryFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedPodcastCategories.Clear();
        BuildPodcastCategoryFilter();
        await SearchPodcastsAsync();
    }

    private async void ClearPodcastLanguageFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedPodcastLanguages.Clear();
        BuildPodcastLanguageFilter();
        await SearchPodcastsAsync();
    }

    private void BuildPodcastCategoryFilter()
    {
        var useCatalog = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text);
        var options = useCatalog && _podcastCategoryCatalog.Count > 0
            ? _podcastCategoryCatalog
            : _podcastSearchResults
                .SelectMany(row => row.Genres)
                .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key, group.Count()))
                .OrderByDescending(option => option.Count)
                .ThenBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        BuildPodcastFilterOptions(
            PodcastCategoryFilterPanel,
            options,
            _selectedPodcastCategories,
            PodcastCategoryCheckBox_OnChanged,
            value => value);
        UpdatePodcastFilterButtons();
    }

    private void PrunePodcastCategorySelection()
    {
        var available = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text)
            ? _podcastCategoryCatalog.Select(option => option.Value)
            : _podcastSearchResults.SelectMany(row => row.Genres);
        var values = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedPodcastCategories.RemoveWhere(value => !values.Contains(value));
    }

    private void PrunePodcastLanguageSelection()
    {
        var available = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text)
            ? _podcastLanguageCatalog.Select(option => option.Value)
            : _podcastSearchResults
                .Select(row => row.Language)
                .Where(language => language is not null)
                .Cast<string>();
        var values = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedPodcastLanguages.RemoveWhere(value => !values.Contains(value));
    }

    private void BuildPodcastLanguageFilter()
    {
        var useCatalog = string.IsNullOrWhiteSpace(PodcastSearchTextBox.Text);
        var options = useCatalog && _podcastLanguageCatalog.Count > 0
            ? _podcastLanguageCatalog
            : _podcastSearchResults
                .Where(row => !string.IsNullOrWhiteSpace(row.Language))
                .GroupBy(row => row.Language!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key, group.Count()))
                .OrderBy(
                    option => FormatPodcastLanguage(option.Value),
                    StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        BuildPodcastFilterOptions(
            PodcastLanguageFilterPanel,
            options,
            _selectedPodcastLanguages,
            PodcastLanguageCheckBox_OnChanged,
            FormatPodcastLanguage);
        UpdatePodcastFilterButtons();
    }

    private void BuildPodcastFilterOptions(
        Panel panel,
        IReadOnlyList<CatalogFilterOption> options,
        IReadOnlySet<string> selected,
        EventHandler<RoutedEventArgs>? changedHandler,
        Func<string, string> displayName)
    {
        panel.Children.Clear();
        foreach (var option in options)
        {
            var checkBox = new CheckBox
            {
                Content = option.Count is > 0
                    ? $"{displayName(option.Value)} ({option.Count:N0})"
                    : displayName(option.Value),
                Tag = option.Value,
                IsChecked = selected.Contains(option.Value),
                Margin = new Thickness(2, 4, 2, 4),
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("HeaderCheckBoxTheme")
            };
            checkBox.IsCheckedChanged += changedHandler;
            panel.Children.Add(checkBox);
        }
    }

    private async void PodcastCategoryCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePodcastSelection(sender, _selectedPodcastCategories);
        UpdatePodcastFilterButtons();
        await SearchPodcastsAsync();
    }

    private async void PodcastLanguageCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        UpdatePodcastSelection(sender, _selectedPodcastLanguages);
        UpdatePodcastFilterButtons();
        await SearchPodcastsAsync();
    }

    private static void UpdatePodcastSelection(object? sender, HashSet<string> selection)
    {
        if (sender is not CheckBox { Tag: string value } checkBox)
            return;
        if (checkBox.IsChecked == true)
            selection.Add(value);
        else
            selection.Remove(value);
    }

    private void ApplyPodcastFilters()
    {
        var rows = _podcastSearchResults
            .Where(row =>
                (_podcastLanguagesLoading ||
                 _selectedPodcastLanguages.Count == 0 ||
                 (row.Language is not null && _selectedPodcastLanguages.Contains(row.Language))))
            .ToList();
        PodcastsDataGrid.ItemsSource = rows;
        { var _tmp = PodcastsDataGrid.ItemsSource; PodcastsDataGrid.ItemsSource = null; PodcastsDataGrid.ItemsSource = _tmp; };
        PodcastStatusTextBlock.Text = rows.Count == 0
            ? LocalizationManager.Current.PodcastNoResults
            : string.Empty;
        PodcastStatusTextBlock.IsVisible = rows.Count == 0;
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private void UpdatePodcastFilterButtons()
    {
        PodcastCategoryFilterButton.Content = _selectedPodcastCategories.Count == 0
            ? LocalizationManager.Current.PodcastCategories
            : $"{LocalizationManager.Current.PodcastCategories} ({_selectedPodcastCategories.Count})";
        PodcastLanguageFilterButton.Content = _selectedPodcastLanguages.Count == 0
            ? LocalizationManager.Current.PodcastLanguages
            : $"{LocalizationManager.Current.PodcastLanguages} ({_selectedPodcastLanguages.Count})";
    }

    private static string FormatPodcastLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;
        try
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var name = culture.DisplayName;
            return $"{name} ({culture.TwoLetterISOLanguageName.ToUpperInvariant()})";
        }
        catch (CultureNotFoundException)
        {
            return language.ToUpperInvariant();
        }
    }

    private async void PodcastsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null ||
            !TryGetDoubleTappedRow<PodcastViewModel>(PodcastsDataGrid, e, out var podcast))
            return;
        await ShowPodcastEpisodesAsync(podcast.ToRecord());
    }

    private async void ShowPodcastEpisodesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PodcastViewModel podcast })
            await ShowPodcastEpisodesAsync(podcast.ToRecord());
    }

    private void AddPodcastButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PodcastViewModel podcast })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SavePodcast(podcast.ToSearchResult());
            LoadNavPlaylists();
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.PodcastAdded, podcast.Name);
        }
        catch
        {
            StatusTextBlock.Text = LocalizationManager.Current.PodcastSearchFailed;
        }
    }

    private async Task ShowSavedPodcastAsync(long podcastId)
    {
        PodcastRecord? podcast;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            podcast = db.GetPodcast(podcastId);
        }
        catch
        {
            podcast = null;
        }

        if (podcast is null)
        {
            PodcastsDataGrid.ItemsSource = null;
            PodcastStatusTextBlock.IsVisible = true;
            PodcastStatusTextBlock.Text = LocalizationManager.Current.PodcastNoResults;
            return;
        }

        await ShowPodcastEpisodesAsync(podcast, showBackButton: false);
    }

    private async Task ShowPodcastEpisodesAsync(
        PodcastRecord podcast,
        bool showBackButton = true)
    {
        CancelAndDispose(ref _podcastFeedCts);
        _podcastFeedCts = new CancellationTokenSource();
        var cancellationToken = _podcastFeedCts.Token;
        _activePodcast = podcast;
        PodcastEpisodesView.IsVisible = true;
        PodcastEpisodesBackButton.IsVisible = showBackButton;
        PodcastEpisodesTitle.Text = podcast.Name;
        PodcastEpisodesAuthor.Text = podcast.Author ?? string.Empty;
        PodcastEpisodesStatistics.Text = string.Empty;
        PodcastEpisodesMetadata.Text = string.Empty;
        PodcastEpisodesDescription.Text = string.Empty;
        PodcastEpisodesArtwork.Source = null;
        PodcastEpisodesDataGrid.ItemsSource = null;
        PodcastEpisodesStatusTextBlock.IsVisible = true;
        PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastEpisodesLoading;
        _ = LoadPodcastHeaderArtworkAsync(podcast.ArtworkUrl, cancellationToken);
        try
        {
            var feed = await _podcastService.GetFeedAsync(
                podcast.FeedUrl,
                cancellationToken);
            var progress = podcast.Id > 0
                ? await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    return db.GetPodcastEpisodeProgress(podcast.Id);
                }, cancellationToken)
                : new Dictionary<string, PodcastEpisodeProgress>(StringComparer.Ordinal);
            var rows = feed.Episodes.Select(episode => CreatePodcastEpisodeRow(podcast, episode, progress))
                .ToList();
            PodcastEpisodesDataGrid.ItemsSource = rows;
            PodcastEpisodesStatusTextBlock.Text = rows.Count == 0
                ? LocalizationManager.Current.PodcastNoEpisodes
                : string.Empty;
            PodcastEpisodesStatusTextBlock.IsVisible = rows.Count == 0;
            ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
            UpdatePodcastHeader(podcast, feed, progress);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            PodcastEpisodesStatusTextBlock.IsVisible = true;
            PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastFeedFailed;
        }
    }

    private void UpdatePodcastHeader(
        PodcastRecord podcast,
        PodcastFeed feed,
        IReadOnlyDictionary<string, PodcastEpisodeProgress> progress)
    {
        var completed = feed.Episodes.Count(episode =>
            progress.TryGetValue(episode.EpisodeKey, out var item) && item.IsCompleted);
        var started = feed.Episodes.Count(episode =>
            progress.TryGetValue(episode.EpisodeKey, out var item) &&
            !item.IsCompleted &&
            item.PositionSeconds > 0);
        var unheard = Math.Max(0, feed.Episodes.Count - completed);
        PodcastEpisodesStatistics.Text = string.Join(
            "  ·  ",
            string.Format(LocalizationManager.Current.PodcastEpisodeTotal, feed.Episodes.Count),
            string.Format(LocalizationManager.Current.PodcastEpisodeUnheard, unheard),
            string.Format(LocalizationManager.Current.PodcastEpisodeStarted, started));

        var metadata = new List<string>();
        var categories = feed.Categories.Count > 0
            ? feed.Categories
            : string.IsNullOrWhiteSpace(podcast.Genre) ? [] : [podcast.Genre];
        if (categories.Count > 0)
            metadata.Add(string.Join(", ", categories.Take(3)));
        if (!string.IsNullOrWhiteSpace(feed.Language))
            metadata.Add(FormatPodcastLanguage(feed.Language));
        if (feed.Episodes.FirstOrDefault()?.PublishedAt is DateTimeOffset latest)
        {
            metadata.Add(string.Format(
                LocalizationManager.Current.PodcastLatestEpisode,
                latest.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)));
        }
        PodcastEpisodesMetadata.Text = string.Join("  ·  ", metadata);
        PodcastEpisodesDescription.Text = NormalizePodcastDescription(feed.Description);
        ToolTip.SetTip(PodcastEpisodesDescription, PodcastEpisodesDescription.Text);
    }

    private static string NormalizePodcastDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;
        var withoutTags = Regex.Replace(description, "<[^>]+>", " ");
        return Regex.Replace(WebUtility.HtmlDecode(withoutTags), @"\s+", " ").Trim();
    }

    private PodcastEpisodeViewModel CreatePodcastEpisodeRow(
        PodcastRecord podcast,
        PodcastEpisode episode,
        IReadOnlyDictionary<string, PodcastEpisodeProgress> progressByEpisode)
    {
        progressByEpisode.TryGetValue(episode.EpisodeKey, out var progress);
        var duration = progress?.DurationSeconds is > 0
            ? TimeSpan.FromSeconds(progress.DurationSeconds.Value)
            : episode.FeedDuration;
        var position = progress?.PositionSeconds is > 0
            ? TimeSpan.FromSeconds(progress.PositionSeconds)
            : TimeSpan.Zero;
        var downloaded = PodcastDownloadService.GetDownloadedPath(podcast, episode) is not null;
        var status = progress?.IsCompleted == true
            ? LocalizationManager.Current.PodcastPlayed
            : position > TimeSpan.Zero
                ? LocalizationManager.Current.PodcastInProgress
                : LocalizationManager.Current.PodcastUnplayed;
        var row = new PodcastEpisodeViewModel
        {
            Episode = episode,
            Title = episode.Title,
            Published = episode.PublishedAt?.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) ?? string.Empty,
            Duration = duration is null ? string.Empty : FormatTime(duration.Value),
            Progress = position <= TimeSpan.Zero
                ? string.Empty
                : duration is null
                    ? FormatTime(position)
                    : $"{FormatTime(position)} / {FormatTime(duration.Value)}",
            BaseStatus = status,
            DurationSort = duration ?? TimeSpan.MaxValue,
            ProgressSort = position
        };
        row.ApplyDownloadState(downloaded, LocalizationManager.Current.PodcastDownloaded);
        return row;
    }

    private async void PodcastEpisodesDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_activePodcast is null ||
            !TryGetDoubleTappedRow<PodcastEpisodeViewModel>(PodcastEpisodesDataGrid, e, out var row))
            return;
        await PlayPodcastEpisodeAsync(_activePodcast, row.Episode);
    }

    private void PodcastEpisodesBackButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CancelAndDispose(ref _podcastFeedCts);
        PodcastEpisodesView.IsVisible = false;
        _activePodcast = null;
        ContentCountTextBlock.Text = ((PodcastsDataGrid.ItemsSource as System.Collections.IList)?.Count ?? 0) > 0
            ? LocalizationManager.FormatEntryCount(((PodcastsDataGrid.ItemsSource as System.Collections.IList)?.Count ?? 0))
            : string.Empty;
    }

    private async Task PlayPodcastEpisodeAsync(PodcastRecord podcast, PodcastEpisode episode)
    {
        RefreshQueueNavigationButtons();
        try
        {
            // Play the local copy when the episode was downloaded for offline use and
            // record the use so eviction keeps the newest episodes.
            var downloaded = PodcastDownloadService.GetDownloadedPath(podcast, episode);
            if (downloaded is not null)
                PodcastDownloadService.MarkUsed(podcast, episode);

            await StartPlaybackAsync(
                downloaded ?? episode.AudioUrl,
                podcastPlayback: new PodcastPlayback(podcast, episode));
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch
        {
            StopPlayback();
            PodcastEpisodesStatusTextBlock.IsVisible = true;
            PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastFeedFailed;
        }
    }

    /// <summary>Attaches the per-row episode context menu when a row is realized.</summary>
    /// <param name="row">Realized episode row.</param>
    private void SetPodcastEpisodeContextFlyout(DataGridRow row)
    {
        row.ContextFlyout = BuildPodcastEpisodeContextFlyout();
        row.AddHandler(
            PointerPressedEvent,
            PodcastEpisodeRow_OnPreviewMouseRightButtonDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private void PodcastEpisodeRow_OnPreviewMouseRightButtonDown(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not DataGridRow row || !e.GetCurrentPoint(row).Properties.IsRightButtonPressed)
            return;
        if (row.ContextFlyout is not PopupFlyoutBase flyout)
            return;
        if (FindAncestor<DataGrid>(row) is { } grid)
            grid.SelectedItem = row.DataContext;
        e.Handled = true;
        flyout.ShowAt(row, showAtPointer: true);
    }

    private MenuFlyout BuildPodcastEpisodeContextFlyout()
    {
        var menu = CreateSidebarMenuFlyout();
        var download = CreateFlyoutMenuItem(LocalizationManager.Current.PodcastDownload);
        download.Click += PodcastDownloadMenuItem_OnClick;
        menu.Items.Add(download);
        var remove = CreateFlyoutMenuItem(LocalizationManager.Current.PodcastDeleteDownload);
        remove.Click += PodcastDeleteDownloadMenuItem_OnClick;
        menu.Items.Add(remove);
        return menu;
    }

    /// <summary>Downloads the clicked episode into the local offline cache.</summary>
    /// <param name="sender">The download action.</param>
    /// <param name="e">Click details.</param>
    private async void PodcastDownloadMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PodcastEpisodeViewModel row } ||
            _activePodcast is not { } podcast)
        {
            return;
        }

        PodcastEpisodesStatusTextBlock.IsVisible = true;
        PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastDownloading;
        var progress = new Progress<double>(value => PodcastEpisodesStatusTextBlock.Text =
            $"{LocalizationManager.Current.PodcastDownloading} {value:P0}");
        try
        {
            var path = await PodcastDownloadService.DownloadAsync(
                podcast,
                row.Episode,
                progress,
                CancellationToken.None);
            if (path is null)
            {
                PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastDownloadFailed;
                return;
            }

            PodcastDownloadService.EnforceLimit(_settings.PodcastDownloadLimitMb * 1024L * 1024L);
            row.ApplyDownloadState(true, LocalizationManager.Current.PodcastDownloaded);
            PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastDownloaded;
        }
        catch (OperationCanceledException)
        {
            PodcastEpisodesStatusTextBlock.Text = string.Empty;
        }
    }

    /// <summary>Removes the clicked episode from the local offline cache.</summary>
    /// <param name="sender">The delete action.</param>
    /// <param name="e">Click details.</param>
    private void PodcastDeleteDownloadMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PodcastEpisodeViewModel row } ||
            _activePodcast is not { } podcast)
        {
            return;
        }

        PodcastDownloadService.Delete(podcast, row.Episode);
        row.ApplyDownloadState(false, LocalizationManager.Current.PodcastDownloaded);
        PodcastEpisodesStatusTextBlock.IsVisible = true;
        PodcastEpisodesStatusTextBlock.Text = LocalizationManager.Current.PodcastDownloadRemoved;
    }

    private async Task LoadPodcastArtworkAsync(string? artworkUrl, CancellationToken cancellationToken)
    {
        var image = await DownloadPodcastImageAsync(artworkUrl, 400, cancellationToken);
        if (image is not null && !cancellationToken.IsCancellationRequested)
            NowPlayingArtworkImage.Source = image;
    }

    private async Task LoadPodcastHeaderArtworkAsync(string? artworkUrl, CancellationToken cancellationToken)
    {
        var image = await DownloadPodcastImageAsync(artworkUrl, 160, cancellationToken);
        if (image is not null && !cancellationToken.IsCancellationRequested)
            PodcastEpisodesArtwork.Source = image;
    }

    private static async Task<Bitmap?> DownloadPodcastImageAsync(
        string? artworkUrl,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(artworkUrl, UriKind.Absolute, out var uri))
            return null;
        try
        {
            var bytes = await RadioImageHttpClient.GetByteArrayAsync(uri, cancellationToken);
            await using var stream = new MemoryStream(bytes);
            var image = new Bitmap(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }
}
