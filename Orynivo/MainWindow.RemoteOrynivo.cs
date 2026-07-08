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
    private async void LoadOrynivoServerNavigation()
    {
        var loadVersion = ++_orynivoNavigationLoadVersion;
        _orynivoPlaylistsByTag.Clear();
        foreach (var item in NavListBox.Items
                     .OfType<ListBoxItem>()
                     .Where(item => item.Tag is string tag &&
                                    (tag.StartsWith("OrynivoServer:", StringComparison.Ordinal) ||
                                     tag.StartsWith("OrynivoServerPlaylist:", StringComparison.Ordinal) ||
                                     tag.StartsWith("LibraryGroup:OrynivoServer", StringComparison.Ordinal)))
                     .ToList())
            NavListBox.Items.Remove(item);

        // Orynivo Server libraries are shown in the shared Artists, Albums, and
        // Tracks views. Keep the server playlist cache populated for existing
        // server-owned playlist actions, but do not render separate server
        // library branches in the sidebar.
        foreach (var server in _settings.OrynivoServers ?? [])
        {
            if (loadVersion != _orynivoNavigationLoadVersion)
                return;
            try
            {
                var playlists = await _orynivoClient.GetPlaylistsAsync(server);
                if (loadVersion != _orynivoNavigationLoadVersion)
                    return;
                foreach (var playlist in playlists)
                {
                    var tag = $"OrynivoServerPlaylist:{server.Id}:{playlist.Id}";
                    _orynivoPlaylistsByTag[tag] = playlist;
                }
            }
            catch { }
        }

        ApplySidebarNavigationSettings();
    }

    private int GetOrynivoServerInsertIndex()
    {
        var insertIndex = NavListBox.Items.IndexOf(FoldersNavItem);
        if (insertIndex < 0)
            return -1;

        for (var index = insertIndex + 1; index < NavListBox.Items.Count; index++)
        {
            if (NavListBox.Items[index] is ListBoxItem { Tag: string tag } &&
                (tag == "LibraryGroup:LocalPlaylists" ||
                 tag.StartsWith("Playlist:", StringComparison.Ordinal)))
            {
                insertIndex = index;
            }
        }

        return insertIndex + 1;
    }

    private StackPanel CreateSmartPlaylistSidebarContent(string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = "⚡ ",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0x00))
        });
        sp.Children.Add(CreateSidebarEntryText(text));
        return sp;
    }

    private int InsertOrynivoServerNavItem(int index, string serverId, string view, string title, bool isEnabled = true)
    {
        var text = CreateSidebarEntryText(title);
        text.Margin = new Thickness(16, 0, 0, 0);
        NavListBox.Items.Insert(index, new ListBoxItem
        {
            Content = text,
            Tag = $"OrynivoServer:{serverId}:{view}",
            IsEnabled = isEnabled,
            Theme = FindResource<ControlTheme>("NavItemTheme")
        });
        return index + 1;
    }
}



