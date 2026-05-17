using System.Windows;
using System.IO;
using Microsoft.Win32;
using Player.Audio;
using System.Windows.Threading;
using System.Collections.ObjectModel;

namespace Player;

public partial class MainWindow : Window
{
    private IAudioPlayer? _player;
    private CancellationTokenSource? _playbackCts;
    private readonly SettingsStore _settingsStore = new();
    private AppSettings _settings = new();
    private readonly DispatcherTimer _transportTimer;
    private bool _isSeekingWithSlider;
    private readonly ObservableCollection<PlaylistItem> _playlist = [];
    private int _currentPlaylistIndex = -1;
    private static readonly HashSet<string> SupportedExtensions =
    [
        ".dsf", ".dff", ".flac", ".mp3", ".wav", ".aiff", ".aif",
        ".m4a", ".aac", ".ogg", ".opus", ".wma"
    ];

    public MainWindow()
    {
        InitializeComponent();
        _transportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _transportTimer.Tick += (_, _) => RefreshTransport();
        PlaylistDataGrid.ItemsSource = _playlist;
        LoadSettings();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPlayback();
        base.OnClosed(e);
    }

    private void LoadSettings()
    {
        _settings = _settingsStore.Load();
        RefreshSelectedDriverText();
        StatusTextBlock.Text = _settings.OutputBackend switch
        {
            OutputBackend.Asio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                "Bitte zuerst in den Einstellungen ein ASIO-Gerät auswählen.",
            OutputBackend.Wasapi when string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId) =>
                "Bitte zuerst in den Einstellungen ein WASAPI-Gerät auswählen.",
            OutputBackend.Asio =>
                "Bereit. DSF läuft nativ als DSD über ASIO, wenn der Treiber DSD unterstützt; andere Formate laufen über den Decoder.",
            OutputBackend.Wasapi =>
                "Bereit. WASAPI ist aktiv; PCM läuft über den Windows-Audioendpunkt.",
            _ =>
                $"{_settings.OutputBackend} ist ausgewählt, aber noch nicht als Wiedergabe-Backend implementiert."
        };
    }

    private void ChooseFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Audiodatei auswählen",
            Filter = "Audiodateien|*.dsf;*.dff;*.flac;*.mp3;*.wav;*.aiff;*.aif;*.m4a;*.aac;*.ogg;*.opus;*.wma|Alle Dateien|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            FilePathTextBox.Text = dialog.FileName;
            FileInfoTextBlock.Text = "Datei ausgewählt. Beim Start lese ich Codec und Samplerate aus.";
            ReplacePlaylist([dialog.FileName]);
        }
    }

    private void ChooseFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Ordner mit Audiodateien auswählen"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var files = Directory
                .EnumerateFiles(dialog.SelectedPath, "*.*", SearchOption.TopDirectoryOnly)
                .Where(path => SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            ReplacePlaylist(files);
            StatusTextBlock.Text = files.Length == 0
                ? "Im ausgewählten Ordner wurden keine unterstützten Audiodateien gefunden."
                : $"{files.Length} Titel geladen.";
        }
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FilePathTextBox.Text))
        {
            StatusTextBlock.Text = "Bitte zuerst eine Audiodatei auswählen.";
            return;
        }

        try
        {
            await StartPlaybackAsync(FilePathTextBox.Text);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Wiedergabe gestoppt.";
        }
        catch (Exception ex)
        {
            StopPlayback();
            StatusTextBlock.Text = ex.Message;
        }
    }

    private async Task StartPlaybackAsync(string filePath)
    {
        StopPlayback();
        _playbackCts = new CancellationTokenSource();

            var extension = Path.GetExtension(filePath);
            IAudioPlayer player;
            AudioFileInfo info;
            if (_settings.OutputBackend == OutputBackend.Asio)
            {
                if (string.IsNullOrWhiteSpace(_settings.SelectedDriverName))
                {
                    StatusTextBlock.Text = "Bitte zuerst ein ASIO-Gerät auswählen.";
                    return;
                }

                if (extension.Equals(".dsf", StringComparison.OrdinalIgnoreCase))
                {
                    (player, info) = await DsfAudioPlayer.CreateAsync(
                        filePath,
                        _settings.SelectedDriverName,
                        _playbackCts.Token);
                }
                else if (extension.Equals(".dff", StringComparison.OrdinalIgnoreCase))
                {
                    (player, info) = await DffAudioPlayer.CreateAsync(
                        filePath,
                        _settings.SelectedDriverName,
                        _playbackCts.Token);
                }
                else
                {
                    (player, info) = await FfmpegAudioPlayer.CreateAsync(
                        filePath,
                        _settings.SelectedDriverName,
                        _playbackCts.Token);
                }
            }
            else if (_settings.OutputBackend == OutputBackend.Wasapi)
            {
                if (string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId))
                {
                    StatusTextBlock.Text = "Bitte zuerst ein WASAPI-Gerät auswählen.";
                    return;
                }

                (player, info) = await WasapiAudioPlayer.CreateAsync(
                    filePath,
                    _settings.SelectedWasapiDeviceId,
                    _playbackCts.Token);
            }
            else
            {
                StatusTextBlock.Text = $"{_settings.OutputBackend} ist noch nicht als Wiedergabe-Backend implementiert.";
                return;
            }

            _player = player;
            _player.Volume = (float)VolumeSlider.Value;
            FilePathTextBox.Text = filePath;
            PlayButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            PauseButton.IsEnabled = true;
            PositionSlider.IsEnabled = player.CanSeek;
            DurationTextBlock.Text = FormatTime(player.Duration);
            _transportTimer.Start();

            FileInfoTextBlock.Text = info.IsDsd && info.ContainerName is "dsf" or "dff"
                ? $"{info.ContainerName.ToUpperInvariant()} erkannt ({info.CodecName}, {info.SourceSampleRate:N0} Hz). Ausgabe nativ als DSD über ASIO."
                : info.IsDsd
                    ? $"DSD erkannt ({info.CodecName}, Quelle {info.SourceSampleRate:N0} Hz). Ausgabe derzeit als PCM mit {info.OutputSampleRate:N0} Hz."
                : $"{info.CodecName.ToUpperInvariant()} · {info.SourceSampleRate:N0} Hz · {info.Channels} Kanal/Kanäle";

            StatusTextBlock.Text = _settings.OutputBackend == OutputBackend.Asio
                ? $"Wiedergabe über {_settings.SelectedDriverName} läuft."
                : $"Wiedergabe über {_settings.SelectedWasapiDeviceName} läuft.";

            await player.WaitForCompletionAsync();

            if (_player == player)
            {
                if (!await TryPlayNextAsync())
                {
                    StopPlayback();
                    StatusTextBlock.Text = "Wiedergabe beendet.";
                }
            }
    }

    private void StopButton_OnClick(object sender, RoutedEventArgs e)
    {
        StopPlayback();
        StatusTextBlock.Text = "Wiedergabe gestoppt.";
    }

    private void StopPlayback()
    {
        _playbackCts?.Cancel();
        _playbackCts?.Dispose();
        _playbackCts = null;

        _player?.Dispose();
        _player = null;

        PlayButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        PauseButton.Content = "Pause";
        PositionSlider.IsEnabled = false;
        _transportTimer.Stop();
    }

    private async void PlaylistDataGrid_OnMouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PlaylistDataGrid.SelectedItem is not PlaylistItem item)
        {
            return;
        }

        _currentPlaylistIndex = _playlist.IndexOf(item);
        try
        {
            await StartPlaybackAsync(item.FilePath);
        }
        catch (Exception ex)
        {
            StopPlayback();
            StatusTextBlock.Text = ex.Message;
        }
    }

    private async Task<bool> TryPlayNextAsync()
    {
        if (_currentPlaylistIndex < 0 || _currentPlaylistIndex + 1 >= _playlist.Count)
        {
            return false;
        }

        _currentPlaylistIndex++;
        var next = _playlist[_currentPlaylistIndex];
        PlaylistDataGrid.SelectedItem = next;
        PlaylistDataGrid.ScrollIntoView(next);
        await StartPlaybackAsync(next.FilePath);
        return true;
    }

    private void ReplacePlaylist(IEnumerable<string> files)
    {
        _playlist.Clear();
        foreach (var file in files)
        {
            _playlist.Add(new PlaylistItem(file));
        }

        _currentPlaylistIndex = _playlist.Count == 1 ? 0 : -1;
        PlaylistDataGrid.SelectedIndex = _currentPlaylistIndex;
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_player is null) return;
        if (_player.IsPaused)
        {
            _player.Resume();
            PauseButton.Content = "Pause";
        }
        else
        {
            _player.Pause();
            PauseButton.Content = "Fortsetzen";
        }
    }

    private async void PositionSlider_OnPreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_player is null || !_player.CanSeek) return;
        await _player.SeekAsync(TimeSpan.FromSeconds(PositionSlider.Value));
        _isSeekingWithSlider = false;
        RefreshTransport();
    }

    private async void PositionSlider_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_player is null || !_player.CanSeek) return;
        _isSeekingWithSlider = true;

        var slider = (System.Windows.Controls.Slider)sender;
        var point = e.GetPosition(slider);
        var ratio = slider.ActualWidth <= 0 ? 0 : Math.Clamp(point.X / slider.ActualWidth, 0, 1);
        slider.Value = slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
        await _player.SeekAsync(TimeSpan.FromSeconds(slider.Value));
        RefreshTransport();
    }

    private void PositionSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSeekingWithSlider)
        {
            CurrentTimeTextBlock.Text = FormatTime(TimeSpan.FromSeconds(PositionSlider.Value));
        }
    }

    private void RefreshTransport()
    {
        if (_player is null) return;
        CurrentTimeTextBlock.Text = FormatTime(_player.Position);
        DurationTextBlock.Text = FormatTime(_player.Duration);
        PositionSlider.Maximum = Math.Max(1, _player.Duration.TotalSeconds);
        if (!_isSeekingWithSlider)
        {
            PositionSlider.Value = Math.Min(PositionSlider.Maximum, _player.Position.TotalSeconds);
        }
    }

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VolumeValueTextBlock is null)
        {
            return;
        }

        VolumeValueTextBlock.Text = $"{Math.Round(VolumeSlider.Value * 100):N0} %";
        if (_player is not null)
        {
            _player.Volume = (float)VolumeSlider.Value;
        }
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            _settings.OutputBackend = window.SelectedOutputBackend;
            _settings.SelectedDriverName = window.SelectedDriverName;
            _settings.SelectedWasapiDeviceId = window.SelectedWasapiDeviceId;
            _settings.SelectedWasapiDeviceName = window.SelectedWasapiDeviceName;
            _settingsStore.Save(_settings);
            RefreshSelectedDriverText();
            StatusTextBlock.Text = _settings.OutputBackend switch
            {
                OutputBackend.Asio when string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                    "Bitte zuerst in den Einstellungen ein ASIO-Gerät auswählen.",
                OutputBackend.Wasapi when string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceId) =>
                    "Bitte zuerst in den Einstellungen ein WASAPI-Gerät auswählen.",
                OutputBackend.KernelStreaming =>
                    "KernelStreaming ist ausgewählt, aber noch nicht als Wiedergabe-Backend implementiert.",
                _ => "Einstellungen gespeichert."
            };
        }
    }

    private void RefreshSelectedDriverText()
    {
        SelectedDriverTextBlock.Text = _settings.OutputBackend switch
        {
            OutputBackend.Asio when !string.IsNullOrWhiteSpace(_settings.SelectedDriverName) =>
                $"{_settings.SelectedDriverName} (ASIO)",
            OutputBackend.Wasapi when !string.IsNullOrWhiteSpace(_settings.SelectedWasapiDeviceName) =>
                $"{_settings.SelectedWasapiDeviceName} (WASAPI)",
            OutputBackend.KernelStreaming =>
                "KernelStreaming ausgewählt.",
            _ =>
                "Kein Gerät ausgewählt."
        };
    }
}
