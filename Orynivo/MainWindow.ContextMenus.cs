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
/// Playlist and album-artwork context menus and radio genre pruning.
/// </summary>
public partial class MainWindow : Window
{
    // ------------------------------------------------------------------
    // Playlist-Kontextmenü
    // ------------------------------------------------------------------

    private void PlaylistContextItem_OnPreviewMouseRightButtonDown(
        object? sender,
        PointerPressedEventArgs e)
    {
        if (sender is not Control target ||
            !e.GetCurrentPoint(target).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (target is DataGridRow dataRow)
        {
            SetPlaylistContextFlyout(dataRow);
            if (dataRow.ContextFlyout is not PopupFlyoutBase)
                return;
            if (FindAncestor<DataGrid>(dataRow) is { } dataGrid)
                dataGrid.SelectedItem = dataRow.DataContext;
        }
        else if (target is TreeViewItem treeItem)
        {
            var isPlexTrack = treeItem.Tag is PlexFolderTag
                {
                    IsTrack: true,
                    Track: not null
                };
            var paths = isPlexTrack
                ? [((PlexFolderTag)treeItem.Tag!).Track!.FilePath]
                : GetPathsForFolderItem(treeItem);
            var localFolderPath = treeItem.Tag is FolderTag
                {
                    IsFile: false,
                    Server: null,
                    FolderPath.Length: > 0
                } folderTag
                ? folderTag.FolderPath
                : null;
            if (paths.Count == 0 && localFolderPath is null)
                return;
            treeItem.IsSelected = true;
            var treeFlyout = isPlexTrack
                ? BuildQueueContextFlyout(paths)
                : BuildPlaylistContextFlyout(paths);
            if (localFolderPath is not null)
            {
                treeFlyout.Items.Insert(0, new Separator());
                treeFlyout.Items.Insert(0, CreateIdentifyFolderMenuItem(localFolderPath));
            }
            treeItem.ContextFlyout = treeFlyout;
        }
        else
        {
            return;
        }

        if (target.ContextFlyout is not PopupFlyoutBase flyout)
            return;
        e.Handled = true;
        flyout.ShowAt(target, showAtPointer: true);
    }

    private void AlbumArtworkContextMenu_OnOpened(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var row = (menu.PlacementTarget as Control)?.DataContext as ContentRow;
        if (row?.Id is null) return;

        // Vorherige dynamisch hinzugefügte Playlist-Einträge entfernen (erste 2: DeleteCover, ReassignCover)
        while (menu.Items.Count > 2)
            menu.Items.RemoveAt(menu.Items.Count - 1);

        var isRemoteAlbum = row.EntityType == "OrynivoAlbum";
        if (menu.Items.Count > 0 && menu.Items[0] is MenuItem deleteItem)
            deleteItem.IsVisible = !isRemoteAlbum;
        if (menu.Items.Count > 1 && menu.Items[1] is MenuItem reassignItem)
        {
            reassignItem.Header = isRemoteAlbum
                ? LocalizationManager.Current.SearchCover
                : LocalizationManager.Current.ReassignCover;
            reassignItem.IsVisible = true;
        }

        if (isRemoteAlbum)
            return;

        AppendPlaylistItems(menu, GetPathsForRow(row));
    }

    private void PruneRadioGenreSelection()
    {
        var available = string.IsNullOrWhiteSpace(RadioSearchTextBox.Text)
            ? _radioGenreCatalog.Select(option => option.Value)
            : _radioSearchResults.SelectMany(row => row.Genres);
        var values = available.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedRadioGenres.RemoveWhere(value => !values.Contains(value));
    }
}
