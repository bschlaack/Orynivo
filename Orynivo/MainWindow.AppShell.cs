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
using Orynivo.Visualization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Display formatting, server-settings equality, library-import preparation, and About.
/// </summary>
public partial class MainWindow : Window
{
    private static string GetContentColumnWidthKey(string view) =>
        view.StartsWith("Playlist:", StringComparison.Ordinal)
            ? "Content.Playlist"
            : $"Content.{view}";

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");

    private static bool PlexServerSettingsEqual(
        IReadOnlyList<PlexServerSettings>? left,
        IReadOnlyList<PlexServerSettings>? right)
    {
        left ??= [];
        right ??= [];
        return left.Count == right.Count &&
               left.Zip(right).All(pair =>
                   string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal) &&
                   string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
                   string.Equals(pair.First.BaseUrl, pair.Second.BaseUrl, StringComparison.Ordinal));
    }

    private static bool OrynivoServerSettingsEqual(
        IReadOnlyList<OrynivoServerSettings>? left,
        IReadOnlyList<OrynivoServerSettings>? right)
    {
        left ??= [];
        right ??= [];
        return left.Count == right.Count &&
               left.Zip(right).All(pair =>
                   string.Equals(pair.First.Id, pair.Second.Id, StringComparison.Ordinal) &&
                   string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
                   string.Equals(pair.First.BaseUrl, pair.Second.BaseUrl, StringComparison.Ordinal) &&
                   string.Equals(pair.First.ApiKey, pair.Second.ApiKey, StringComparison.Ordinal));
    }

    internal void PrepareForLibraryImport()
    {
        _searchTimer.Stop();
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        _visualizerWindow?.Close();
        _visualizerWindow = null;
        StopPlayback();
    }

    private void VisualizerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // Non-modal like the karaoke window: playback keeps running behind it.
        var window = new VisualizerWindow(
            0,
            new VisualizerRenderOptions(
                _settings.VisualizerRenderWidth,
                _settings.VisualizerRenderHeight,
                _settings.VisualizerFrameRate,
                _settings.ReduceMotion,
                _settings.VisualizerAlwaysShowOverlay,
                _settings.VisualizerPresetDirectory),
            new VisualizerTransport(
                Previous: () => PreviousButton_OnClick(this, new RoutedEventArgs()),
                PlayPause: () => PlayButton_OnClick(this, new RoutedEventArgs()),
                Next: () => NextButton_OnClick(this, new RoutedEventArgs()),
                IsPlaying: () => _isPlaying,
                NowPlaying: () => (NowPlayingTitleBlock.Text, NowPlayingArtistBlock.Text)));
        window.Closed += (_, _) => _visualizerWindow = null;
        _visualizerWindow = window;
        window.Show(this);
    }

    private async void AboutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseEmbeddedSettings();
        await new AboutWindow(_settings.OrynivoServers ?? []).ShowDialog<object?>(this);
    }
}
