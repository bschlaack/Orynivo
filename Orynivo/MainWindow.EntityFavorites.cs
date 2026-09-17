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
/// Favorites-only toggling and reload for the shared entity views.
/// </summary>
public partial class MainWindow : Window
{
    private async void EntityFavoritesOnlyCheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingEntityFavoritesFilter)
            return;

        var tag = GetActiveEntityFavoritesView();
        if (tag is null)
            return;

        var isChecked = EntityFavoritesOnlyCheckBox.IsChecked == true;
        if (tag.StartsWith("Orynivo:", StringComparison.Ordinal))
        {
            _trackFavoritesOnly = isChecked;
            await LoadOrynivoViewAsync();
            return;
        }

        if (tag == "Artists")
            _artistFavoritesOnly = isChecked;
        else
            _albumFavoritesOnly = isChecked;

        await ReloadEntityRowsAsync(tag);
    }

    private string? GetActiveEntityFavoritesView()
    {
        if (_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
            _activeOrynivoView is "Artists" or "Albums" or "Tracks")
        {
            return $"Orynivo:{_activeOrynivoView}";
        }

        if (_activeAlbumFilterId is null &&
            _activeArtistFilterId is long &&
            AlbumViewModeBorder.IsVisible)
        {
            return "Albums";
        }

        return _currentTopLevelTag is "Artists" or "Albums"
            ? _currentTopLevelTag
            : null;
    }

    private void UpdateEntityFavoritesFilterToggle(string? tag)
    {
        var visible = tag is "Artists" or "Albums" ||
                      (tag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true &&
                       _activeOrynivoView is "Artists" or "Albums" or "Tracks");
        _updatingEntityFavoritesFilter = true;
        try
        {
            EntityFavoritesOnlyCheckBox.IsVisible = visible;
            EntityFavoritesOnlyCheckBox.IsChecked = tag switch
            {
                "Artists" => _artistFavoritesOnly,
                "Albums" => _albumFavoritesOnly,
                _ when tag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true => _trackFavoritesOnly,
                _ => false
            };
        }
        finally
        {
            _updatingEntityFavoritesFilter = false;
        }
    }

    private async Task ReloadEntityRowsAsync(string tag)
    {
        var artworkMode = tag == "Albums"
            ? _showAlbumArtworkView
            : _showArtistArtworkView;

        ContentDataGrid.IsVisible = !artworkMode;
        AlbumArtworkListBox.IsVisible = tag == "Albums" && artworkMode;
        ArtistArtworkListBox.IsVisible = tag == "Artists" && artworkMode;

        await BindLocalRowsAndStartRemoteAppendAsync(tag);
    }
}
