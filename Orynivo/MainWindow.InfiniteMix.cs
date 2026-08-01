using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

public partial class MainWindow
{
    private const int InfiniteMixBatchSize = 20;
    private const int InfiniteMixRefillThreshold = 5;
    private bool _infiniteMixEnabled;
    private bool _infiniteMixLoading;

    /// <summary>Starts or stops the history-informed, automatically replenished queue.</summary>
    private async void InfiniteMixButton_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_infiniteMixEnabled)
        {
            StopInfiniteMix();
            return;
        }

        _infiniteMixEnabled = true;
        UpdateInfiniteMixUi();
        _queue.Clear();
        _queueIndex = -1;
        await RefillInfiniteMixAsync(force: true);
        if (_queue.Count == 0)
        {
            StopInfiniteMix();
            StatusTextBlock.Text = LocalizationManager.Current.NoData;
            return;
        }

        _queueIndex = 0;
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(_queue[0].FilePath); }
        catch (Exception exception) { CrashLogger.Log(exception, "Infinite Mix playback"); }
    }

    /// <summary>Stops automatic replenishment without clearing the tracks already queued.</summary>
    private void StopInfiniteMix()
    {
        _infiniteMixEnabled = false;
        UpdateInfiniteMixUi();
    }

    /// <summary>Updates the shared Infinite Mix buttons and queue status.</summary>
    private void UpdateInfiniteMixUi()
    {
        InfiniteMixButton.Content = _infiniteMixEnabled
            ? LocalizationManager.Current.InfiniteMixStop
            : LocalizationManager.Current.InfiniteMixStart;
        if (_infiniteMixEnabled)
            StatusTextBlock.Text = LocalizationManager.Current.InfiniteMixActive;
    }

    /// <summary>Schedules a refill when the active mix is approaching the end of its queue.</summary>
    private void EnsureInfiniteMixQueue()
    {
        if (!_infiniteMixEnabled || _infiniteMixLoading)
            return;
        var remaining = _queueIndex < 0 ? _queue.Count : _queue.Count - _queueIndex - 1;
        if (remaining <= InfiniteMixRefillThreshold)
            _ = RefillInfiniteMixAsync(force: false);
    }

    /// <summary>Ranks unified local/server candidates and appends a diverse batch to the queue.</summary>
    /// <param name="force">Whether to refill regardless of the current remaining count.</param>
    private async Task RefillInfiniteMixAsync(bool force)
    {
        if (_infiniteMixLoading || !_infiniteMixEnabled)
            return;
        var remaining = _queueIndex < 0 ? _queue.Count : _queue.Count - _queueIndex - 1;
        if (!force && remaining > InfiniteMixRefillThreshold)
            return;

        _infiniteMixLoading = true;
        if (force)
            ShowInfiniteMixProgress(5);
        try
        {
            var local = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return new GenreCloudSource(null, GenreCloudService.BuildSnapshot(db.GetTrackFacets(), null, 1000));
            });
            if (force)
                ShowInfiniteMixProgress(25);
            var remote = await Task.WhenAll(_settings.OrynivoServers.Select(LoadInfiniteMixSourceAsync));
            var sources = new[] { local }.Concat(remote.Where(source => source is not null).Cast<GenreCloudSource>()).ToList();
            if (force)
                ShowInfiniteMixProgress(45);
            var listeningWeights = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var since = DateTimeOffset.UtcNow.AddDays(-14).ToUnixTimeSeconds();
                var weights = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var (genre, seconds) in db.GetTopGenres(100, since))
                    foreach (var key in GenreCloudService.ResolveGenreKeys(genre))
                        weights[key] = weights.GetValueOrDefault(key) + seconds;
                return (IReadOnlyDictionary<string, double>)weights;
            });
            if (force)
                ShowInfiniteMixProgress(60);
            var ranked = sources.SelectMany(source => source.Snapshot.Candidates.Select(candidate =>
                    new GenreCloudCandidate(source.Server, candidate,
                        CalculateGenreCandidateScore(source.Server, candidate, listeningWeights) + Random.Shared.NextDouble() * 18)))
                .OrderByDescending(candidate => candidate.Score)
                .ToList();
            var rows = await ResolveGenreCandidateRowsAsync(ranked, CancellationToken.None);
            if (force)
                ShowInfiniteMixProgress(82);
            var queued = _queue.Select(item => item.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var recentArtists = _queue.Skip(Math.Max(0, _queue.Count - 4))
                .Select(item => item.Artist).Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var selectedAlbums = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sourceLimit = Math.Max(3, (int)Math.Ceiling(InfiniteMixBatchSize / (double)Math.Max(1, sources.Count)) + 2);
            var selected = new List<ContentRow>();
            foreach (var row in rows)
            {
                if (selected.Count >= InfiniteMixBatchSize || string.IsNullOrWhiteSpace(row.FilePath) || queued.Contains(row.FilePath))
                    continue;
                if (sourceCounts.GetValueOrDefault(row.SourceKey) >= sourceLimit)
                    continue;
                if (!string.IsNullOrWhiteSpace(row.Artist) && recentArtists.Contains(row.Artist) && selected.Count < 8)
                    continue;
                var albumKey = $"{row.SourceKey}|{row.AlbumId}|{row.Album}";
                if (!string.IsNullOrWhiteSpace(row.Album) && !selectedAlbums.Add(albumKey))
                    continue;
                selected.Add(row);
                queued.Add(row.FilePath);
                sourceCounts[row.SourceKey] = sourceCounts.GetValueOrDefault(row.SourceKey) + 1;
            }
            foreach (var row in selected)
                _queue.Add(ToPlaylistItem(row));
            if (force)
                ShowInfiniteMixProgress(100);
            PersistPlaybackQueue();
            RefreshQueueRowsIfVisible();
            RefreshQueueNavigationButtons();
            await RefreshActiveGaplessQueueAsync();
        }
        catch (Exception exception)
        {
            CrashLogger.Log(exception, "Infinite Mix refill");
        }
        finally
        {
            _infiniteMixLoading = false;
            if (force)
                HideInfiniteMixProgress();
        }
    }

    /// <summary>Shows the blocking initial-mix progress overlay at the supplied percentage.</summary>
    /// <param name="value">Progress from zero through one hundred.</param>
    private void ShowInfiniteMixProgress(double value)
    {
        InfiniteMixButton.IsEnabled = false;
        InfiniteMixProgressBar.Value = Math.Clamp(value, 0, 100);
        InfiniteMixProgressTextBlock.Text = $"{InfiniteMixProgressBar.Value:0} %";
        InfiniteMixLoadingOverlay.IsVisible = true;
    }

    /// <summary>Hides the initial-mix overlay and restores its start button.</summary>
    private void HideInfiniteMixProgress()
    {
        InfiniteMixLoadingOverlay.IsVisible = false;
        InfiniteMixButton.IsEnabled = true;
    }

    /// <summary>Loads one remote mix source without allowing an unavailable server to suppress other libraries.</summary>
    /// <param name="server">Configured remote library.</param>
    /// <returns>The source snapshot, or <see langword="null"/> when the server is unavailable.</returns>
    private async Task<GenreCloudSource?> LoadInfiniteMixSourceAsync(OrynivoServerSettings server)
    {
        try
        {
            return await LoadRemoteGenreCloudAsync(server, null, CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }
}
