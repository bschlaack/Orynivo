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
/// Visual tree helpers, display-name resolution, and Orynivo Server playlist display.
/// </summary>
public partial class MainWindow : Window
{
    private T? FindResource<T>(string key) where T : class
    {
        if (TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) && value is T t)
            return t;
        if (Avalonia.Application.Current?.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out value) == true && value is T t2)
            return t2;
        return null;
    }

    /// <summary>Resolves one of the shared <c>FontSize…</c> typography tokens for code-built controls.</summary>
    /// <param name="key">The typography resource key (e.g. <c>FontSizeBody</c>).</param>
    /// <returns>The token's pixel size, or a body-text fallback when it is missing.</returns>
    private double ResolveFontSize(string key)
    {
        if (TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) && value is double d)
            return d;
        if (Avalonia.Application.Current?.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out value) == true && value is double d2)
            return d2;
        return 13;
    }

    private static T? FindAncestor<T>(Visual? current) where T : Visual
    {
        return current?.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault();
    }

    private static T? FindVisualChild<T>(Visual parent) where T : Visual
        => FindVisualChildren<T>(parent).FirstOrDefault();

    private static IEnumerable<T> FindVisualChildren<T>(Visual parent) where T : Visual
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is T match)
                yield return match;
            if (child is Visual v)
                foreach (var descendant in FindVisualChildren<T>(v))
                    yield return descendant;
        }
    }

    private string GetPlaylistName(string tag)
    {
        if (!long.TryParse(tag.AsSpan("Playlist:".Length), out long id))
            return "Playlist";
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetPlaylistById(id)?.Name ?? "Playlist";
        }
        catch { return "Playlist"; }
    }

    private string GetOrynivoPlaylistName(string tag)
        => _orynivoPlaylistsByTag.TryGetValue(tag, out var playlist)
            ? playlist.Name
            : LocalizationManager.Current.Playlists;

    private bool TryParseOrynivoPlaylistTag(
        string tag,
        out OrynivoServerSettings? server,
        out long playlistId)
    {
        server = null;
        playlistId = 0;
        if (!tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal))
            return false;

        var parts = tag.Split(':');
        if (parts.Length != 3 || !long.TryParse(parts[2], out playlistId))
            return false;

        server = _settings.OrynivoServers.FirstOrDefault(item => item.Id == parts[1]);
        return server is not null;
    }

    private string GetRadioName(string tag)
    {
        if (!long.TryParse(tag.AsSpan("Radio:".Length), out var id))
            return LocalizationManager.Current.InternetRadio;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetRadioStation(id)?.Name ?? LocalizationManager.Current.InternetRadio;
        }
        catch
        {
            return LocalizationManager.Current.InternetRadio;
        }
    }

    private string GetPodcastName(string tag)
    {
        if (!long.TryParse(tag.AsSpan("Podcast:".Length), out var id))
            return LocalizationManager.Current.Podcasts;
        try
        {
            using var db = AudioDatabase.OpenDefault();
            return db.GetPodcast(id)?.Name ?? LocalizationManager.Current.Podcasts;
        }
        catch
        {
            return LocalizationManager.Current.Podcasts;
        }
    }

    private async Task ShowOrynivoPlaylistAsync(string tag)
    {
        if (!TryParseOrynivoPlaylistTag(tag, out var server, out var playlistId) || server is null)
            return;

        ContentDataGrid.IsVisible = true;
        FolderTreeView.IsVisible = false;
        AlbumArtworkListBox.IsVisible = false;
        ArtistArtworkListBox.IsVisible = false;
        ApplyColumns(tag);
        StatusTextBlock.Text = LocalizationManager.Current.OrynivoLoading;

        // Smart playlists are resolved with the client's favourites because remote
        // favourite state lives client-side (settings.json), not on the server.
        var isSmart = _orynivoPlaylistsByTag.TryGetValue(tag, out var playlistInfo) &&
                      playlistInfo.IsSmartPlaylist;
        var entries = isSmart
            ? await _orynivoClient.ResolveSmartPlaylistTracksAsync(
                server,
                playlistId,
                GetOrynivoFavoriteTrackIds(server))
            : await _orynivoClient.GetPlaylistTracksAsync(server, playlistId);
        var rows = entries.Select((entry, index) =>
        {
            if (entry.Track is null)
            {
                return new ContentRow
                {
                    Nr = (index + 1).ToString(CultureInfo.CurrentCulture),
                    PlaylistEntryId = entry.PlaylistEntryId > 0 ? entry.PlaylistEntryId : null,
                    Title = Path.GetFileName(entry.Path),
                    FileName = Path.GetFileName(entry.Path),
                    FilePath = string.Empty,
                    EntityType = "OrynivoTrack",
                    OrynivoServer = server
                };
            }

            var row = ToOrynivoTrackContentRow(server, entry.Track);
            row.Nr = entry.Position.ToString(CultureInfo.CurrentCulture);
            row.PlaylistEntryId = entry.PlaylistEntryId > 0 ? entry.PlaylistEntryId : null;
            return row;
        }).ToList();

        ContentDataGrid.ItemsSource = rows;
        ContentCountTextBlock.Text = LocalizationManager.FormatTrackCount(rows.Count);
        StatusTextBlock.Text = string.Empty;
    }
}
