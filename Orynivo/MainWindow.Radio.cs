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

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Internetradio
    // ------------------------------------------------------------------

    private void LoadCatalogFilterCache()
    {
        _catalogFilterCacheData = _catalogFilterCache.Load();
        if (_catalogFilterCacheData.RadioGenres.Any(option => option.Count is not null))
            _catalogFilterCacheData.RadioGenresUpdatedAt = null;
        _radioGenreCatalog.Clear();
        _radioGenreCatalog.AddRange(_catalogFilterCacheData.RadioGenres
            .Select(option => new CatalogFilterOption(option.Value)));
        _podcastCategoryCatalog.Clear();
        _podcastCategoryCatalog.AddRange(_catalogFilterCacheData.PodcastCategories);
        _podcastLanguageCatalog.Clear();
        _podcastLanguageCatalog.AddRange(_catalogFilterCacheData.PodcastLanguages);
        if (_podcastCategoryCatalog.Any(option => string.IsNullOrWhiteSpace(option.Key)))
            _catalogFilterCacheData.PodcastCategoriesUpdatedAt = null;
        BuildRadioGenreFilter();
        BuildPodcastCategoryFilter();
        BuildPodcastLanguageFilter();
    }

    private async Task EnsureRadioFilterCatalogAsync()
    {
        BuildRadioGenreFilter();
        if (_radioFilterCatalogLoading ||
            CatalogFilterCache.IsFresh(_catalogFilterCacheData.RadioGenresUpdatedAt))
            return;

        _radioFilterCatalogLoading = true;
        try
        {
            var tags = await _radioBrowserService.GetTagsAsync();
            var options = tags
                .Select(tag => (Genre: NormalizeRadioGenre(tag.Name), tag.StationCount))
                .Where(item => item.Genre is not null)
                .GroupBy(item => item.Genre!, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key))
                .OrderBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            _radioGenreCatalog.Clear();
            _radioGenreCatalog.AddRange(options);
            _catalogFilterCacheData.RadioGenres = options;
            _catalogFilterCacheData.RadioGenresUpdatedAt = DateTimeOffset.UtcNow;
            _catalogFilterCache.Save(_catalogFilterCacheData);
            BuildRadioGenreFilter();
        }
        catch
        {
            // Existing cached filter data remains usable.
        }
        finally
        {
            _radioFilterCatalogLoading = false;
        }
    }

    private async void RadioSearchButton_OnClick(object? sender, RoutedEventArgs e) =>
        await SearchRadioStationsAsync();

    private async void RadioSearchTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        await SearchRadioStationsAsync();
    }

    private async Task SearchRadioStationsAsync()
    {
        CancelAndDispose(ref _radioSearchCts);
        _radioSearchCts = new CancellationTokenSource();
        var cancellationToken = _radioSearchCts.Token;

        RadioStationsDataGrid.ItemsSource = null;
        RadioStatusTextBlock.IsVisible = true;
        RadioStatusTextBlock.Text = LocalizationManager.Current.RadioLoading;
        try
        {
            var stations = await _radioBrowserService.SearchAsync(
                RadioSearchTextBox.Text,
                _selectedRadioGenres,
                cancellationToken);
            var rows = stations.Select(station => new RadioStationViewModel
            {
                StationUuid = station.StationUuid,
                Name = station.Name,
                StreamUrl = station.StreamUrl,
                Homepage = station.Homepage,
                Favicon = station.Favicon,
                CountryCode = station.CountryCode,
                Codec = station.Codec,
                Bitrate = station.Bitrate,
                Tags = station.Tags,
                Genres = NormalizeRadioGenres(station.Tags)
            }).ToList();
            _radioSearchResults.Clear();
            _radioSearchResults.AddRange(rows);
            BuildRadioGenreFilter();
            ApplyRadioGenreFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            RadioStatusTextBlock.Text = LocalizationManager.Current.RadioSearchFailed;
        }
    }

    private void RadioGenreFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        BuildRadioGenreFilter();
        RadioGenreFilterPopup.IsOpen = !RadioGenreFilterPopup.IsOpen;
    }

    private async void ClearRadioGenreFilterButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _selectedRadioGenres.Clear();
        BuildRadioGenreFilter();
        await SearchRadioStationsAsync();
    }

    private void BuildRadioGenreFilter()
    {
        var useCatalog = string.IsNullOrWhiteSpace(RadioSearchTextBox.Text);
        var options = useCatalog && _radioGenreCatalog.Count > 0
            ? _radioGenreCatalog
            : _radioSearchResults
                .SelectMany(row => row.Genres)
                .GroupBy(genre => genre, StringComparer.OrdinalIgnoreCase)
                .Select(group => new CatalogFilterOption(group.Key, group.Count()))
                .OrderByDescending(option => option.Count)
                .ThenBy(option => option.Value, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        RadioGenreFilterPanel.Children.Clear();
        foreach (var option in options)
        {
            var checkBox = new CheckBox
            {
                Content = option.Count is > 0
                    ? $"{option.Value} ({option.Count:N0})"
                    : option.Value,
                Tag = option.Value,
                IsChecked = _selectedRadioGenres.Contains(option.Value),
                Margin = new Thickness(2, 4, 2, 4),
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("HeaderCheckBoxTheme")
            };
            checkBox.IsCheckedChanged += RadioGenreCheckBox_OnChanged;
            RadioGenreFilterPanel.Children.Add(checkBox);
        }

        UpdateRadioGenreFilterButton();
    }

    private async void RadioGenreCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: string genre } checkBox)
            return;
        if (checkBox.IsChecked == true)
            _selectedRadioGenres.Add(genre);
        else
            _selectedRadioGenres.Remove(genre);
        UpdateRadioGenreFilterButton();
        await SearchRadioStationsAsync();
    }

    private void ApplyRadioGenreFilter()
    {
        var rows = _radioSearchResults;
        RadioStationsDataGrid.ItemsSource = rows;
        RadioStatusTextBlock.Text = rows.Count == 0
            ? LocalizationManager.Current.RadioNoResults
            : string.Empty;
        RadioStatusTextBlock.IsVisible = rows.Count == 0;
        ContentCountTextBlock.Text = LocalizationManager.FormatEntryCount(rows.Count);
    }

    private void UpdateRadioGenreFilterButton()
    {
        RadioGenreFilterButton.Content = _selectedRadioGenres.Count == 0
            ? LocalizationManager.Current.RadioGenres
            : $"{LocalizationManager.Current.RadioGenres} ({_selectedRadioGenres.Count})";
    }

    private static IReadOnlyList<string> NormalizeRadioGenres(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return [];

        return tags.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeRadioGenre)
            .Where(genre => genre is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(genre => genre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeRadioGenre(string tag)
    {
        var value = tag.Trim().ToLowerInvariant();
        if (value.Length < 2 ||
            value is "aac" or "mp3" or "ogg" or "fm" or "am" or "radio" or "music" or "otr")
            return null;

        if (value.Contains("news", StringComparison.Ordinal) || value == "information") return "News";
        if (value.Contains("talk", StringComparison.Ordinal)) return "Talk";
        if (value.Contains("public radio", StringComparison.Ordinal)) return "Public Radio";
        if (value.Contains("classical", StringComparison.Ordinal)) return "Classical";
        if (value.Contains("jazz", StringComparison.Ordinal)) return "Jazz";
        if (value.Contains("easy listening", StringComparison.Ordinal)) return "Easy Listening";
        if (value.Contains("adult contemporary", StringComparison.Ordinal)) return "Adult Contemporary";
        if (value.Contains("top 40", StringComparison.Ordinal) || value == "hits") return "Hits";
        if (value.Contains("oldies", StringComparison.Ordinal)) return "Oldies";
        if (value.Contains("80s", StringComparison.Ordinal)) return "80s";
        if (value.Contains("90s", StringComparison.Ordinal)) return "90s";
        if (value.Contains("70s", StringComparison.Ordinal)) return "70s";
        if (value.Contains("hip hop", StringComparison.Ordinal) || value.Contains("hip-hop", StringComparison.Ordinal)) return "Hip-Hop";
        if (value.Contains("r&b", StringComparison.Ordinal) || value.Contains("soul", StringComparison.Ordinal)) return "R&B / Soul";
        if (value.Contains("electronic", StringComparison.Ordinal) || value is "edm" or "techno" or "house" or "trance") return "Electronic";
        if (value.Contains("dance", StringComparison.Ordinal)) return "Dance";
        if (value.Contains("alternative", StringComparison.Ordinal)) return "Alternative";
        if (value.Contains("indie", StringComparison.Ordinal)) return "Indie";
        if (value.Contains("metal", StringComparison.Ordinal)) return "Metal";
        if (value.Contains("rock", StringComparison.Ordinal)) return "Rock";
        if (value.Contains("pop", StringComparison.Ordinal)) return "Pop";
        if (value.Contains("blues", StringComparison.Ordinal)) return "Blues";
        if (value.Contains("country", StringComparison.Ordinal)) return "Country";
        if (value.Contains("reggae", StringComparison.Ordinal)) return "Reggae";
        if (value.Contains("folk", StringComparison.Ordinal)) return "Folk";
        if (value.Contains("latin", StringComparison.Ordinal)) return "Latin";
        if (value.Contains("world", StringComparison.Ordinal)) return "World";
        if (value.Contains("culture", StringComparison.Ordinal)) return "Culture";
        if (value.Contains("comedy", StringComparison.Ordinal)) return "Comedy";
        if (value.Contains("sport", StringComparison.Ordinal)) return "Sports";
        return null;
    }

    private async void RadioStationsDataGrid_OnMouseDoubleClick(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (FindAncestor<Button>(e.Source as Visual) is not null ||
            !TryGetDoubleTappedRow<RadioStationViewModel>(RadioStationsDataGrid, e, out var station))
            return;
        await PlayRadioAsync(station.ToRecord());
    }

    private async void PlayRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RadioStationViewModel station })
            await PlayRadioAsync(station.ToRecord());
    }

    private void AddRadioButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RadioStationViewModel station })
            return;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            db.SaveRadioStation(station.ToBrowserStation());
            LoadNavPlaylists();
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.RadioAdded, station.Name);
        }
        catch (Exception)
        {
            StatusTextBlock.Text = LocalizationManager.Current.RadioSearchFailed;
        }
    }

    private async Task PlaySavedRadioAsync(long radioId)
    {
        RadioStationRecord? station;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            station = db.GetRadioStation(radioId);
        }
        catch
        {
            station = null;
        }

        if (station is not null)
            await PlayRadioAsync(station);
    }

    private async Task PlayRadioAsync(RadioStationRecord station)
    {
        RefreshQueueNavigationButtons();
        _ = _radioBrowserService.RegisterClickAsync(station.StationUuid);
        try
        {
            await StartPlaybackAsync(station.StreamUrl, station);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped;
        }
        catch (Exception ex)
        {
            StopPlayback();
            StatusTextBlock.Text = ex.Message;
        }
    }

    private void ShowRadioNowPlaying(RadioStationRecord station, AudioFileInfo info)
    {
        RadioNowPlayingPanel.IsVisible = true;
        RadioNowPlayingStation.Text = station.Name;
        RadioNowPlayingTitle.Text = LocalizationManager.Current.RadioMetadataUnavailable;
        RadioNowPlayingArtist.Text = string.Empty;
        RadioNowPlayingDescription.Text = BuildRadioDescription(station, info, null);
        RadioNowPlayingLogo.Source = null;
        NowPlayingArtworkImage.Source = null;
        _ = LoadRadioLogoAsync(station.Favicon, _playbackCts?.Token ?? CancellationToken.None);
    }

    private void StartRadioMetadataMonitor(RadioStationRecord station)
    {
        CancelAndDispose(ref _radioMetadataCts);
        _radioMetadataCts = CancellationTokenSource.CreateLinkedTokenSource(
            _playbackCts?.Token ?? CancellationToken.None);
        _ = MonitorRadioMetadataAsync(station, _radioMetadataCts.Token);
    }

    private async Task MonitorRadioMetadataAsync(
        RadioStationRecord station,
        CancellationToken cancellationToken)
    {
        string? previousSignature = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var metadata = await _radioMetadataService.ProbeAsync(
                    station.StreamUrl,
                    cancellationToken);
                var signature = metadata is null
                    ? string.Empty
                    : $"{metadata.StreamTitle}\n{metadata.Description}\n{metadata.Genre}";
                if (!string.Equals(signature, previousSignature, StringComparison.Ordinal))
                {
                    previousSignature = signature;
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateRadioMetadata(station, metadata));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (previousSignature is null)
                {
                    previousSignature = string.Empty;
                    await Dispatcher.UIThread.InvokeAsync(() => UpdateRadioMetadata(station, null));
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateRadioMetadata(RadioStationRecord station, RadioStreamMetadata? metadata)
    {
        if (_currentRadioStation?.StationUuid != station.StationUuid)
            return;

        var title = metadata?.Title;
        var artist = metadata?.Artist;
        if (string.IsNullOrWhiteSpace(title))
        {
            RadioNowPlayingTitle.Text = LocalizationManager.Current.RadioMetadataUnavailable;
            RadioNowPlayingArtist.Text = string.Empty;
            NowPlayingTitleBlock.Text = station.Name;
            NowPlayingArtistBlock.Text = LocalizationManager.Current.InternetRadio;
        }
        else
        {
            RadioNowPlayingTitle.Text = title;
            RadioNowPlayingArtist.Text = artist ?? metadata!.StationName ?? station.Name;
            NowPlayingTitleBlock.Text = title;
            NowPlayingArtistBlock.Text = artist ?? station.Name;
        }

        RadioNowPlayingDescription.Text = BuildRadioDescription(
            station,
            null,
            metadata);
        RefreshWindowsMediaMetadata();
    }

    private static string BuildRadioDescription(
        RadioStationRecord station,
        AudioFileInfo? info,
        RadioStreamMetadata? metadata)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata?.Description))
            parts.Add(metadata.Description);
        if (!string.IsNullOrWhiteSpace(metadata?.Genre))
            parts.Add(metadata.Genre);
        if (!string.IsNullOrWhiteSpace(station.CountryCode))
            parts.Add(station.CountryCode);
        if (!string.IsNullOrWhiteSpace(station.Codec))
            parts.Add(station.Bitrate > 0
                ? $"{station.Codec} · {station.Bitrate} kbps"
                : station.Codec);
        else if (info is not null)
            parts.Add($"{info.CodecName.ToUpperInvariant()} · {info.SourceSampleRate:N0} Hz");
        return string.Join("  ·  ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private async Task LoadRadioLogoAsync(string? favicon, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(favicon, UriKind.Absolute, out var uri))
            return;
        try
        {
            var bytes = await RadioImageHttpClient.GetByteArrayAsync(uri, cancellationToken);
            await using var stream = new MemoryStream(bytes);
            var image = new Bitmap(stream);
            if (!cancellationToken.IsCancellationRequested)
            {
                RadioNowPlayingLogo.Source = image;
                NowPlayingArtworkImage.Source = image;
            }
        }
        catch
        {
            // A missing or invalid station logo does not affect playback.
        }
    }

    private void ClearRadioNowPlaying()
    {
        RadioNowPlayingPanel.IsVisible = false;
        RadioNowPlayingLogo.Source = null;
        RadioNowPlayingStation.Text = string.Empty;
        RadioNowPlayingTitle.Text = string.Empty;
        RadioNowPlayingArtist.Text = string.Empty;
        RadioNowPlayingDescription.Text = string.Empty;
    }

    private static HttpClient CreateRadioImageHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Orynivo/1.0");
        return client;
    }
}



