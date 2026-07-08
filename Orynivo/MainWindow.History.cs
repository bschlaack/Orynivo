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
    private async Task ShowDailyHistoryAsync(DateTime date)
    {
        var entries = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetHistoryForDay(date);
        });

        var dialog = new DailyHistoryDialog(date, entries, _settings);
        if (await dialog.ShowDialog<bool>(this) == false || dialog.SelectedEntry is not { } entry)
            return;

        switch (dialog.SelectedAction)
        {
            case DailyHistoryAction.Track:
                await OpenHistoryTrackAsync(entry);
                break;
            case DailyHistoryAction.Album:
                await OpenHistoryAlbumAsync(entry);
                break;
            case DailyHistoryAction.Artist when entry.ArtistId is long artistId:
                await ShowArtistAlbumsAsync(
                    artistId,
                    entry.Artist ?? LocalizationManager.Current.Unknown);
                break;
            case DailyHistoryAction.Artist:
                await OpenHistoryArtistAsync(entry);
                break;
        }
    }

    private async Task OpenHistoryAlbumAsync(DailyHistoryEntry entry)
    {
        if (entry.AlbumId is long localAlbumId)
        {
            await ShowAlbumTracksAsync(
                localAlbumId,
                entry.Album ?? LocalizationManager.Current.Unknown);
            return;
        }

        var remoteRow = await ResolveOrynivoHistoryTrackRowAsync(entry);
        if (remoteRow is not { OrynivoServer: { } server, AlbumId: long remoteAlbumId })
            return;

        _activeArtistFilterId = null;
        _activeArtistFilterName = null;
        _activeOrynivoServer = server;
        await OpenOrynivoAlbumTracksAsync(
            remoteAlbumId,
            remoteRow.Album ?? entry.Album ?? LocalizationManager.Current.Unknown,
            remoteRow.AlbumArtist ?? remoteRow.Artist ?? entry.Artist);
    }

    private async Task OpenHistoryTrackAsync(DailyHistoryEntry entry)
    {
        if (entry.TrackId is null || !IsAvailableLocalTrack(entry.Path))
            return;

        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = "Tracks";

        var tracksItem = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Tag as string, "Tracks", StringComparison.Ordinal));
        if (tracksItem is not null && !ReferenceEquals(NavListBox.SelectedItem, tracksItem))
        {
            _suppressNavSelectionChanged = true;
            try
            {
                NavListBox.SelectedItem = tracksItem;
            }
            finally
            {
                _suppressNavSelectionChanged = false;
            }
        }

        await ShowTopLevelViewAsync("Tracks");
        var rows = (ContentDataGrid.ItemsSource as IEnumerable<ContentRow>)?.ToList() ?? [];
        var row = rows.FirstOrDefault(item =>
            string.Equals(item.FilePath, entry.Path, StringComparison.OrdinalIgnoreCase));
        if (row is null)
            return;

        ContentDataGrid.SelectedItem = row;
        ContentDataGrid.ScrollIntoView(row, null);
        await PlayTrackFromRowsAsync(row, rows);
    }

    private static bool IsPlayableHistoryEntry(DailyHistoryEntry entry) =>
        string.Equals(entry.MediaType, "track", StringComparison.OrdinalIgnoreCase) &&
        (IsAvailableLocalTrack(entry.Path) || IsHttpUrl(entry.Path));

    private bool CanOpenHistoryArtist(DailyHistoryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.Artist) &&
        (entry.ArtistId.HasValue || TryGetOrynivoHistoryTarget(entry, out _, out _));

    private async Task OpenHistoryArtistAsync(DailyHistoryEntry entry)
    {
        if (entry.ArtistId is long localArtistId)
        {
            await ShowArtistAlbumsAsync(
                localArtistId,
                entry.Artist ?? LocalizationManager.Current.Unknown);
            return;
        }

        var remoteRow = await ResolveOrynivoHistoryTrackRowAsync(entry);
        if (remoteRow is not { OrynivoServer: { } server, ArtistId: long remoteArtistId })
            return;

        _activeOrynivoServer = server;
        await OpenOrynivoArtistAlbumsAsync(
            remoteArtistId,
            remoteRow.Artist ?? entry.Artist ?? LocalizationManager.Current.Unknown);
    }

    private async Task PlayHistoryEntryInPlaceAsync(DailyHistoryEntry entry)
    {
        if (!IsPlayableHistoryEntry(entry))
            return;

        var remoteRow = await ResolveOrynivoHistoryTrackRowAsync(entry);
        var queueItem = remoteRow is not null
            ? ToPlaylistItem(remoteRow)
            : new PlaylistItem(
                entry.Path,
                string.IsNullOrWhiteSpace(entry.Title) ? null : entry.Title,
                string.IsNullOrWhiteSpace(entry.Artist) ? null : entry.Artist,
                string.IsNullOrWhiteSpace(entry.Album) ? null : entry.Album,
                null,
                null,
                entry.DurationSeconds is > 0 ? TimeSpan.FromSeconds(entry.DurationSeconds.Value) : null);

        _queue.Clear();
        _queue.Add(queueItem);
        _queueIndex = 0;
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();

        try { await StartPlaybackAsync(queueItem.FilePath); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
        UpdateNowPlayingRowHighlights();
    }

    private async Task<ContentRow?> ResolveOrynivoHistoryTrackRowAsync(DailyHistoryEntry entry)
    {
        if (!TryGetOrynivoHistoryTarget(entry, out var server, out var trackId))
            return null;

        var streamUrl = OrynivoServerClient.GetStreamUrl(server, trackId);
        if (_orynivoTracksByUrl.TryGetValue(streamUrl, out var cached))
            return cached;

        var tracks = await _orynivoClient.GetTracksByIdsAsync(server, [trackId], CancellationToken.None);
        var track = tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null)
            return null;

        var row = ToOrynivoTrackContentRow(server, track);
        EnsureArtworkHydrated(row);
        return row;
    }
}



