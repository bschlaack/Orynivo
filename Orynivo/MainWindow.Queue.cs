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
/// Queue persistence and Up Next view behaviour for <see cref="MainWindow"/>.
/// </summary>
public partial class MainWindow : Window
{
    private void RestorePlaybackQueueState()
    {
        _queue.Clear();
        using var db = AudioDatabase.OpenDefault();
        var snapshot = db.GetPlaybackQueue();
        var paths = snapshot.Paths;
        var currentIndex = snapshot.CurrentIndex;

        if (paths.Count == 0 && _settings.PlaybackQueuePaths.Count > 0)
        {
            paths = _settings.PlaybackQueuePaths
                .Select(NormalizePersistedQueuePath)
                .Where(CanPersistQueuePath)
                .ToList();
            currentIndex = paths.Count == 0
                ? -1
                : Math.Clamp(_settings.PlaybackQueueIndex, 0, paths.Count - 1);
            db.SavePlaybackQueue(paths, currentIndex);
            if (ClearLegacyPlaybackQueueSettings())
                _settingsStore.Save(_settings);
        }

        // Migrate queue entries written by older builds, which could contain an
        // authenticated Orynivo stream URL, to the stable credential-free form.
        var normalizedPaths = paths.Select(NormalizePersistedQueuePath).ToList();
        var restoredPaths = normalizedPaths.Where(CanPersistQueuePath).ToList();
        if (!normalizedPaths.SequenceEqual(paths, StringComparer.Ordinal) || restoredPaths.Count != paths.Count)
        {
            currentIndex = currentIndex < 0
                ? -1
                : Math.Min(currentIndex, restoredPaths.Count - 1);
            db.SavePlaybackQueue(restoredPaths, currentIndex);
        }

        foreach (var path in restoredPaths)
        {
            _queue.Add(CreatePlaylistItem(path));
        }

        _queueIndex = _queue.Count == 0
            ? -1
            : currentIndex < 0 ? -1 : Math.Clamp(currentIndex, 0, _queue.Count - 1);
        ResetQueuePlaybackState();
        RefreshQueueNavigationButtons();
    }

    /// <summary>Loads metadata for restored Orynivo Server queue references in the background.</summary>
    private async Task HydrateRestoredOrynivoQueueAsync()
    {
        var references = _queue
            .Select((item, index) => (item, index))
            .Where(entry => entry.item.FilePath.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var (item, index) in references)
        {
            try
            {
                var playablePath = await ResolveRemoteMcpTrackAsync(item.FilePath);
                if (string.IsNullOrWhiteSpace(playablePath) ||
                    !await Dispatcher.UIThread.InvokeAsync(() =>
                        index < _queue.Count && ReferenceEquals(_queue[index], item)))
                    continue;
                if (_orynivoTracksByUrl.TryGetValue(playablePath, out var row))
                {
                    _queue[index] = ToPlaylistItem(row);
                    PersistPlaybackQueue();
                }
            }
            catch
            {
                // A temporarily unavailable server must not prevent local startup.
            }
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_currentTopLevelTag == "Queue")
                RefreshQueueRows();
        });
    }

    private void CapturePlaybackQueueState()
    {
        var persisted = new List<string>();
        var persistedIndex = -1;
        for (var index = 0; index < _queue.Count; index++)
        {
            var path = GetPersistableQueuePath(_queue[index]);
            if (!CanPersistQueuePath(path))
                continue;
            if (index == _queueIndex)
                persistedIndex = persisted.Count;
            persisted.Add(path);
        }

        var currentIndex = persistedIndex >= 0
            ? persistedIndex
            : persisted.Count == 0 ? -1 : Math.Min(_queueIndex, persisted.Count - 1);
        using var db = AudioDatabase.OpenDefault();

        // Detect a wholesale replacement (playing a completely different selection) and keep
        // the outgoing queue as a restorable "previous queue". Appends, moves, removals, and
        // next/previous keep some of the old items, so the intersection stays non-empty.
        var previous = db.GetPlaybackQueue();
        if (previous.Paths.Count > 0)
        {
            var newSet = new HashSet<string>(persisted, StringComparer.OrdinalIgnoreCase);
            if (!previous.Paths.Any(newSet.Contains))
                db.SavePreviousPlaybackQueue(previous.Paths);
        }

        db.SavePlaybackQueue(persisted, currentIndex);
        ClearLegacyPlaybackQueueSettings();
    }

    /// <summary>Returns a credential-free stable identity for a queue item.</summary>
    private string GetPersistableQueuePath(PlaylistItem item)
    {
        var path = item.FilePath;
        if (path.StartsWith("orynivo://", StringComparison.OrdinalIgnoreCase))
            return path;
        if (_orynivoTracksByUrl.TryGetValue(path, out var row) &&
            row.OrynivoServer is { } server && row.Id is long trackId)
            return BuildOrynivoPlaylistReference(server, trackId);
        return path;
    }

    /// <summary>Converts a legacy authenticated Orynivo stream URL to a stable reference.</summary>
    private string NormalizePersistedQueuePath(string path)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return path;

        foreach (var server in _settings.OrynivoServers ?? [])
        {
            if (!Uri.TryCreate(server.BaseUrl, UriKind.Absolute, out var baseUri) ||
                !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != baseUri.Port)
                continue;
            var prefix = baseUri.AbsolutePath.TrimEnd('/');
            var streamPrefix = $"{prefix}/api/stream/";
            if (!uri.AbsolutePath.StartsWith(streamPrefix, StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(uri.AbsolutePath[streamPrefix.Length..], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var trackId))
                continue;
            return BuildOrynivoPlaylistReference(server, trackId);
        }

        return path;
    }

    /// <summary>Restores the queue that was playing before the most recent wholesale replacement.</summary>
    /// <returns>A task representing the asynchronous restore.</returns>
    private async Task RestorePreviousQueueAsync()
    {
        IReadOnlyList<string> paths;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            paths = db.GetPreviousPlaybackQueue();
        }
        catch { paths = []; }

        if (paths.Count == 0)
        {
            StatusTextBlock.Text = LocalizationManager.Current.NoPreviousQueue;
            return;
        }

        _queue.Clear();
        foreach (var path in paths)
            _queue.Add(CreatePlaylistItem(path));
        _queueIndex = _queue.Count > 0 ? 0 : -1;
        ResetQueuePlaybackState();
        // Persisting captures the outgoing queue as the new "previous", making restore reversible.
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();

        if (_queue.Count == 0)
            return;
        try { await StartPlaybackAsync(_queue[0].FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
        UpdateNowPlayingRowHighlights();
    }

    /// <summary>Handles the Up Next "restore last queue" header button.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click event data.</param>
    private async void RestoreQueueButton_OnClick(object? sender, RoutedEventArgs e)
        => await RestorePreviousQueueAsync();

    /// <summary>Shows the "restore last queue" button in the Up Next view when a previous queue exists.</summary>
    /// <param name="tag">The current top-level view tag.</param>
    private void UpdateRestoreQueueButtonState(string tag)
    {
        var available = false;
        if (tag == "Queue")
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                available = db.GetPreviousPlaybackQueue().Count > 0;
            }
            catch { available = false; }
        }
        RestoreQueueButton.IsVisible = available;
    }

    private bool ClearLegacyPlaybackQueueSettings()
    {
        if (_settings.PlaybackQueuePaths.Count == 0 && _settings.PlaybackQueueIndex == -1)
            return false;
        _settings.PlaybackQueuePaths = [];
        _settings.PlaybackQueueIndex = -1;
        return true;
    }

    /// <summary>Returns whether a queue path may be persisted without credentials.</summary>
    /// <param name="path">The candidate path or URL.</param>
    /// <returns><see langword="true"/> when the path may be persisted.</returns>
    private static bool CanPersistQueuePath(string path) => QueuePathPolicy.CanPersist(path);

    private static bool IsAvailableLocalTrack(string path)
    {
        if (File.Exists(path))
            return true;
        if (!CueSheetParser.IsVirtualPath(path))
            return false;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            var track = db.GetByPath(path);
            return track is not null &&
                   File.Exists(track.SourcePath) &&
                   (track.CuePath is null || File.Exists(track.CuePath));
        }
        catch
        {
            return false;
        }
    }

    private void PersistPlaybackQueue()
    {
        CapturePlaybackQueueState();
        if (ClearLegacyPlaybackQueueSettings())
            _settingsStore.Save(_settings);
    }

    private Button CreateQueueActionButton(
        string content,
        string tooltip,
        ContentRow row,
        EventHandler<RoutedEventArgs> clickHandler)
    {
        var button = new Button
        {
            Content = content,
            Tag = row,
            Width = 34,
            Height = 28,
            Padding = new Thickness(0),
            Theme = FindResource<ControlTheme>("HeaderFilterButtonTheme")
        };
        ToolTip.SetTip(button, tooltip);
        button.Click += clickHandler;
        return button;
    }

    private void RefreshQueueRows()
    {
        var preserveScrollOffset = _currentTopLevelTag == "Queue" &&
                                    ReferenceEquals(ContentDataGrid.ItemsSource, _queueRows)
            ? _contentDataGridVerticalScrollBar?.Value
            : null;
        _queueRows.Clear();
        using var db = AudioDatabase.OpenDefault();
        var localTracks = db.GetTrackListByPaths(_queue.Select(item => item.FilePath))
            .ToDictionary(track => track.Path, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < _queue.Count; index++)
        {
            var item = _queue[index];
            ContentRow row;
            if (localTracks.TryGetValue(item.FilePath, out var track))
            {
                row = ToTrackContentRow(track);
            }
            else if (_plexTracksByUrl.TryGetValue(item.FilePath, out var plexRow))
            {
                row = CreateQueueRow(plexRow);
            }
            else if (_orynivoTracksByUrl.TryGetValue(item.FilePath, out var orynivoRow))
            {
                row = CreateQueueRow(orynivoRow);
            }
            else
            {
                row = new ContentRow
                {
                    Title = item.DisplayTitle,
                    Artist = item.Artist,
                    Album = item.Album,
                    Duration = item.Duration ?? string.Empty,
                    Format = item.Format,
                    FileName = item.FileName,
                    FilePath = item.FilePath
                };
            }

            row.Nr = (index + 1).ToString(CultureInfo.CurrentCulture);
            row.QueueItem = item;
            _queueRows.Add(row);
        }

        ContentDataGrid.ItemsSource = _queueRows;
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(_queueRows.Count);
        ClearQueueButton.IsEnabled = _queue.Count > 0;
        SaveQueueAsPlaylistButton.IsEnabled =
            _queue.Any(item => CanPersistQueuePath(item.FilePath));
        Dispatcher.UIThread.Post(UpdateNowPlayingRowHighlights, DispatcherPriority.Loaded);
        if (preserveScrollOffset is double offset)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AttachContentDataGridVerticalScrollBar();
                if (_contentDataGridVerticalScrollBar is { } scrollBar)
                    scrollBar.Value = Math.Clamp(offset, scrollBar.Minimum, scrollBar.Maximum);
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Copies catalog metadata into a queue-owned row without mutating provider caches.</summary>
    /// <param name="source">Catalog row to copy.</param>
    /// <returns>A queue row retaining navigation, source, and track metadata.</returns>
    private static ContentRow CreateQueueRow(ContentRow source) => new()
    {
        Id = source.Id,
        ArtistId = source.ArtistId,
        AlbumId = source.AlbumId,
        Title = source.Title,
        AlphabetIndexText = source.AlphabetIndexText,
        Artist = source.Artist,
        Album = source.Album,
        AlbumArtist = source.AlbumArtist,
        Year = source.Year,
        TrackNumber = source.TrackNumber,
        DiscNumber = source.DiscNumber,
        Genre = source.Genre,
        Bitrate = source.Bitrate,
        SampleRate = source.SampleRate,
        SampleRateHz = source.SampleRateHz,
        BitDepth = source.BitDepth,
        Channels = source.Channels,
        ChannelCount = source.ChannelCount,
        Composer = source.Composer,
        Bpm = source.Bpm,
        FileName = source.FileName,
        FileSize = source.FileSize,
        AddedAt = source.AddedAt,
        ReplayGainTrack = source.ReplayGainTrack,
        ReplayGainAlbum = source.ReplayGainAlbum,
        MusicBrainzTrackId = source.MusicBrainzTrackId,
        Folder = source.Folder,
        EntityType = source.EntityType,
        ExternalId = source.ExternalId,
        PlexServerId = source.PlexServerId,
        PlexAlbumRatingKey = source.PlexAlbumRatingKey,
        PlexArtistRatingKey = source.PlexArtistRatingKey,
        OrynivoServer = source.OrynivoServer,
        Duration = source.Duration,
        Format = source.Format,
        FilePath = source.FilePath,
        SourcePath = source.SourcePath,
        PlexPartUrls = source.PlexPartUrls,
        KnownDuration = source.KnownDuration,
        IsFavorite = source.IsFavorite,
        UserRating = source.UserRating,
        MusicBrainzRating = source.MusicBrainzRating,
        MusicBrainzRatingVotes = source.MusicBrainzRatingVotes,
        MusicBrainzRatingFetchedAt = source.MusicBrainzRatingFetchedAt
    };

    private async void QueueMoveUpButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { QueueItem: not null } row })
            return;
        var index = IndexOfQueueItem(row.QueueItem);
        if (index <= 0)
            return;
        await MoveQueueItemAsync(index, index - 1);
    }

    private async void QueueMoveDownButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { QueueItem: not null } row })
            return;
        var index = IndexOfQueueItem(row.QueueItem);
        if (index < 0 || index + 1 >= _queue.Count)
            return;
        await MoveQueueItemAsync(index, index + 1);
    }

    private async void QueueRemoveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContentRow { QueueItem: not null } row })
            return;
        var index = IndexOfQueueItem(row.QueueItem);
        if (index < 0)
            return;

        var currentItem = GetCurrentQueueItem();
        _queue.RemoveAt(index);
        if (ReferenceEquals(currentItem, row.QueueItem))
            _queueIndex = index - 1;
        else
            _queueIndex = IndexOfQueueItem(currentItem);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        await RefreshActiveGaplessQueueAsync();
    }

    private async Task MoveQueueItemAsync(int oldIndex, int newIndex)
    {
        var currentItem = GetCurrentQueueItem();
        _queue.Move(oldIndex, newIndex);
        _queueIndex = IndexOfQueueItem(currentItem);
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        await RefreshActiveGaplessQueueAsync();
    }

    /// <summary>Handles the Up Next header button that clears the complete queue.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click event data.</param>
    private void ClearQueueButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ClearPlaybackQueue();
        StatusTextBlock.Text = LocalizationManager.Current.QueueCleared;
    }

    /// <summary>Clears the editable playback queue without stopping the currently playing item.</summary>
    private void ClearPlaybackQueue()
    {
        StopInfiniteMix();
        _queue.Clear();
        _queueIndex = -1;
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueRowsIfVisible();
        RefreshQueueNavigationButtons();
        ClearQueueButton.IsEnabled = false;
        SaveQueueAsPlaylistButton.IsEnabled = false;
    }

    private PlaylistItem? GetCurrentQueueItem() =>
        _queueIndex >= 0 && _queueIndex < _queue.Count ? _queue[_queueIndex] : null;

    private int IndexOfQueueItem(PlaylistItem? item)
    {
        if (item is null)
            return -1;
        for (var index = 0; index < _queue.Count; index++)
        {
            if (ReferenceEquals(_queue[index], item))
                return index;
        }
        return -1;
    }

    private void RefreshQueueRowsIfVisible()
    {
        if (_currentTopLevelTag == "Queue")
            RefreshQueueRows();
    }

    private async Task RefreshActiveGaplessQueueAsync()
    {
        if (_player is not IGaplessAudioPlayer ||
            string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        var path = _currentFilePath;
        var position = _player.Position;
        var wasPaused = _player.IsPaused;
        await StartPlaybackAsync(path, initialPosition: position);
        if (wasPaused)
            PausePlayback();
    }

    private async void SaveQueueAsPlaylistButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var paths = _queue
            .Select(item => item.FilePath)
            .Where(CanPersistQueuePath)
            .ToList();
        if (paths.Count == 0)
            return;

        var dialog = new NewPlaylistDialog();
        if (await dialog.ShowDialog<bool>(this) == false ||
            string.IsNullOrWhiteSpace(dialog.PlaylistName))
        {
            return;
        }

        var name = dialog.PlaylistName.Trim();
        using (var db = AudioDatabase.OpenDefault())
            db.CreatePlaylist(name, paths);
        LoadNavPlaylists();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.TracksAddedToPlaylist,
            paths.Count,
            name);
    }
}
