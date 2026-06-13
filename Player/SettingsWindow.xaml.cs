using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Color  = System.Windows.Media.Color;
using Player.Audio;
using Player.Library;
using Player.Localization;
using UiLanguage = Player.Localization.Language;
using System.Runtime.InteropServices;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using WpfMessageBox = System.Windows.MessageBox;

namespace Player;

public partial class SettingsWindow : Window
{
    private sealed record SettingChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly AppSettings _settings;
    private readonly List<string> _libraryPaths = [];
    private readonly Dictionary<string, CancellationTokenSource> _activeScans = [];
    private readonly Action<List<string>>? _onLibraryPathsChanged;

    public SettingsWindow(AppSettings settings, Action<List<string>>? onLibraryPathsChanged = null)
    {
        InitializeComponent();
        _settings = settings;
        _onLibraryPathsChanged = onLibraryPathsChanged;
        OutputBackendComboBox.ItemsSource = Enum.GetValues<OutputBackend>();
        OutputBackendComboBox.SelectedItem = settings.OutputBackend;
        var themeChoices = new[]
        {
            new SettingChoice<AppTheme>(AppTheme.Light, LocalizationManager.Current.ThemeLight),
            new SettingChoice<AppTheme>(AppTheme.Dark, LocalizationManager.Current.ThemeDark)
        };
        ThemeComboBox.ItemsSource = themeChoices;
        ThemeComboBox.SelectedItem = themeChoices.First(choice => choice.Value == settings.Theme);
        var languageChoices = new[]
        {
            new SettingChoice<UiLanguage>(UiLanguage.German, LocalizationManager.Current.LanguageGerman),
            new SettingChoice<UiLanguage>(UiLanguage.English, LocalizationManager.Current.LanguageEnglish),
            new SettingChoice<UiLanguage>(UiLanguage.French, LocalizationManager.Current.LanguageFrench),
            new SettingChoice<UiLanguage>(UiLanguage.Spanish, LocalizationManager.Current.LanguageSpanish)
        };
        LanguageComboBox.ItemsSource = languageChoices;
        LanguageComboBox.SelectedItem = languageChoices.First(choice => choice.Value == settings.Language);
        ArtistInfoSourceComboBox.ItemsSource = Enum.GetValues<ArtistInfoSource>();
        ArtistInfoSourceComboBox.SelectedItem = settings.ArtistInfoSource;
        LastFmApiKeyTextBox.Text = settings.LastFmApiKey ?? string.Empty;
        LastFmPanel.Visibility = settings.ArtistInfoSource == ArtistInfoSource.LastFm
            ? Visibility.Visible
            : Visibility.Collapsed;
        _libraryPaths.AddRange(settings.LibraryPaths);
        RebuildDirectoryList();
        LoadDrivers();
        NavListBox.SelectedIndex = 1; // "Ausgabegerät" als Standard
    }

    public string? SelectedDriverName => DriverComboBox.SelectedItem as string;
    public string? SelectedWasapiDeviceId =>
        DriverComboBox.SelectedItem is WasapiDeviceInfo device ? device.Id : null;
    public string? SelectedWasapiDeviceName =>
        DriverComboBox.SelectedItem is WasapiDeviceInfo device ? device.Name : null;
    public OutputBackend SelectedOutputBackend =>
        OutputBackendComboBox.SelectedItem is OutputBackend backend
            ? backend
            : OutputBackend.Asio;
    public IReadOnlyList<string> SelectedLibraryPaths => _libraryPaths.AsReadOnly();
    public AppTheme SelectedTheme =>
        ThemeComboBox.SelectedItem is SettingChoice<AppTheme> theme ? theme.Value : AppTheme.Dark;
    public UiLanguage SelectedLanguage =>
        LanguageComboBox.SelectedItem is SettingChoice<UiLanguage> language ? language.Value : UiLanguage.German;
    public ArtistInfoSource SelectedArtistInfoSource =>
        ArtistInfoSourceComboBox.SelectedItem is ArtistInfoSource src ? src : ArtistInfoSource.Wikipedia;
    public string SelectedLastFmApiKey => LastFmApiKeyTextBox.Text.Trim();

    protected override void OnClosed(EventArgs e)
    {
        foreach (var cts in _activeScans.Values)
            cts.Cancel();
        base.OnClosed(e);
    }

    private void SettingsWindow_OnSourceInitialized(object sender, EventArgs e)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var captionColor = _settings.Theme == AppTheme.Dark
                ? ColorRef(0x13, 0x14, 0x2A)
                : ColorRef(0xEA, 0xEA, 0xF5);
            var textColor = _settings.Theme == AppTheme.Dark
                ? ColorRef(0xFF, 0xFF, 0xFF)
                : ColorRef(0x13, 0x14, 0x2A);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch { }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // ------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------

    private void NavListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavListBox.SelectedItem is not ListBoxItem { Tag: string tag })
            return;

        AudioDevicePanel.Visibility = tag == "AudioDevice" ? Visibility.Visible : Visibility.Collapsed;
        LibraryPanel.Visibility     = tag == "Library"      ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility  = tag == "Appearance"   ? Visibility.Visible : Visibility.Collapsed;
        ArtistInfoPanel.Visibility  = tag == "ArtistInfo"   ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ArtistInfoSourceComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LastFmPanel.Visibility = SelectedArtistInfoSource == ArtistInfoSource.LastFm
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------
    // Geräte
    // ------------------------------------------------------------------

    private void LoadDrivers()
    {
        try
        {
            if (SelectedOutputBackend == OutputBackend.Wasapi)
            {
                var devices = WasapiDeviceProvider.GetRenderDevices();
                DriverComboBox.ItemsSource = devices;
                DriverComboBox.DisplayMemberPath = nameof(WasapiDeviceInfo.Name);
                DriverComboBox.SelectedItem = devices.FirstOrDefault(device =>
                    string.Equals(device.Id, _settings.SelectedWasapiDeviceId, StringComparison.Ordinal))
                    ?? devices.FirstOrDefault();
                DeviceLabelTextBlock.Text = LocalizationManager.Current.WasapiOutputDevice;
                StatusTextBlock.Text = devices.Count == 0
                    ? LocalizationManager.Current.NoWasapiDevices
                    : LocalizationManager.Current.SelectAndSave;
            }
            else
            {
                var drivers = SteinbergAsioStream.GetDriverNames();
                DriverComboBox.ItemsSource = drivers;
                DriverComboBox.DisplayMemberPath = string.Empty;
                DriverComboBox.SelectedItem = drivers.FirstOrDefault(name =>
                    string.Equals(name, _settings.SelectedDriverName, StringComparison.Ordinal))
                    ?? drivers.FirstOrDefault(name =>
                        name.Contains("TOPPING", StringComparison.OrdinalIgnoreCase))
                    ?? drivers.FirstOrDefault();
                DeviceLabelTextBlock.Text = SelectedOutputBackend == OutputBackend.Asio
                    ? LocalizationManager.Current.AsioOutputDevice
                    : LocalizationManager.Current.OutputDevice;
                StatusTextBlock.Text = drivers.Count == 0
                    ? LocalizationManager.Current.NoAsioDrivers
                    : LocalizationManager.Current.SelectAndSave;
            }
        }
        catch (DllNotFoundException)
        {
            StatusTextBlock.Text = LocalizationManager.Current.AsioBridgeMissing;
        }
    }

    private void DriverComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DeviceInfoButton.Visibility = DriverComboBox.SelectedItem is string or WasapiDeviceInfo
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OutputBackendComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var backend = SelectedOutputBackend;
        DriverComboBox.IsEnabled = backend != OutputBackend.KernelStreaming;
        DeviceInfoButton.IsEnabled = backend is OutputBackend.Asio or OutputBackend.Wasapi;
        LoadDrivers();
        if (backend == OutputBackend.KernelStreaming)
        {
            DriverComboBox.ItemsSource = null;
            StatusTextBlock.Text = LocalizationManager.Current.KernelStreamingUnavailable;
        }
    }

    private void DeviceInfoButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DriverComboBox.SelectedItem is string driverName)
            {
                var info = SteinbergAsioStream.GetDeviceInfo(driverName);
                new DeviceInfoWindow(info) { Owner = this }.ShowDialog();
            }
            else if (DriverComboBox.SelectedItem is WasapiDeviceInfo wasapiDevice)
            {
                var info = WasapiDeviceProvider.GetCapabilities(wasapiDevice.Id);
                new DeviceInfoWindow(info) { Owner = this }.ShowDialog();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = string.Format(LocalizationManager.Current.DeviceInfoFailed, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Bibliothek
    // ------------------------------------------------------------------

    private void AddDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizationManager.Current.AddMusicDirectory,
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        var path = dialog.SelectedPath;
        if (_libraryPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            return;

        _libraryPaths.Add(path);
        RebuildDirectoryList();
        _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
    }

    private void RebuildDirectoryList()
    {
        DirectoriesPanel.Children.Clear();
        foreach (var path in _libraryPaths)
            DirectoriesPanel.Children.Add(BuildDirectoryRow(path));
    }

    private static async Task RefreshCountAsync(string path, TextBlock countBlock)
    {
        try
        {
            var count = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return db.CountByDirectory(path);
            });
            countBlock.Text = LocalizationManager.FormatTrackCount(count);
        }
        catch
        {
            countBlock.Text = string.Empty;
        }
    }

    private UIElement BuildDirectoryRow(string path)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

        // --- Kopfzeile: Pfad | Scannen | × ---
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathBlock = new TextBlock
        {
            Text = path,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = path
        };
        Grid.SetColumn(pathBlock, 0);

        var countBlock = new TextBlock
        {
            Text = "…",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 10, 0),
            ToolTip = LocalizationManager.Current.TrackCountTooltip
        };
        Grid.SetColumn(countBlock, 1);

        var scanBtn = new Button
        {
            Content = LocalizationManager.Current.Scan,
            Width = 80,
            Height = 26,
            Margin = new Thickness(0, 0, 4, 0),
            Style = (Style)FindResource("SettingsButtonStyle")
        };
        Grid.SetColumn(scanBtn, 2);

        var removeBtn = new Button
        {
            Content = "×",
            Width = 26,
            Height = 26,
            FontSize = 14,
            ToolTip = LocalizationManager.Current.RemoveDirectory,
            Style = (Style)FindResource("SettingsButtonStyle")
        };
        Grid.SetColumn(removeBtn, 3);

        grid.Children.Add(pathBlock);
        grid.Children.Add(countBlock);
        grid.Children.Add(scanBtn);
        grid.Children.Add(removeBtn);

        _ = RefreshCountAsync(path, countBlock);

        // --- Statuszeile ---
        var statusBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed
        };

        // --- Scan-Handler ---
        scanBtn.Click += async (_, _) =>
        {
            if (_activeScans.ContainsKey(path))
            {
                _activeScans[path].Cancel();
                return;
            }

            if (!Directory.Exists(path))
            {
                statusBlock.Visibility = Visibility.Visible;
                statusBlock.Text = LocalizationManager.Current.FolderNotFound;
                return;
            }

            var cts = new CancellationTokenSource();
            _activeScans[path] = cts;
            UpdateBackupButtonAvailability();
            scanBtn.Content = LocalizationManager.Current.Cancel;
            statusBlock.Visibility = Visibility.Visible;
            statusBlock.Text = LocalizationManager.Current.ScanRunning;

            var progress = new Progress<ScanProgress>(p =>
                statusBlock.Text = $"{p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");

            try
            {
                var result = await LibraryScanner.ScanAsync(path, progress, cts.Token);
                var failed = result.Failed > 0
                    ? $" · {string.Format(LocalizationManager.Current.ScanFailed, result.Failed)}"
                    : string.Empty;
                statusBlock.Text = string.Format(
                    LocalizationManager.Current.ScanCompleted,
                    result.Total,
                    result.Added,
                    result.Updated,
                    failed);
            }
            catch (OperationCanceledException)
            {
                statusBlock.Text = LocalizationManager.Current.ScanCanceled;
            }
            catch (Exception ex)
            {
                statusBlock.Text = string.Format(LocalizationManager.Current.ScanFailed, ex.Message);
            }
            finally
            {
                _activeScans.Remove(path);
                cts.Dispose();
                scanBtn.Content = LocalizationManager.Current.Scan;
                UpdateBackupButtonAvailability();
                _ = RefreshCountAsync(path, countBlock);
            }
        };

        // --- Entfernen-Handler ---
        removeBtn.Click += (_, _) =>
        {
            if (_activeScans.TryGetValue(path, out var cts))
            {
                cts.Cancel();
                _activeScans.Remove(path);
                UpdateBackupButtonAvailability();
            }
            _libraryPaths.Remove(path);
            RebuildDirectoryList();
            _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
        };

        outer.Children.Add(grid);
        outer.Children.Add(statusBlock);
        return outer;
    }

    private async void OptimizeDatabaseButton_OnClick(object sender, RoutedEventArgs e)
    {
        OptimizeDatabaseButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.DatabaseOptimizing;
        try
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.Optimize();
            });
            DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.DatabaseOptimized;
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(LocalizationManager.Current.DatabaseOptimizeFailed, ex.Message);
        }
        finally
        {
            OptimizeDatabaseButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void RepairAlbumArtworkButton_OnClick(object sender, RoutedEventArgs e)
    {
        RepairAlbumArtworkButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.AlbumArtworkRepairing;
        var progress = new Progress<ScanProgress>(p =>
            DatabaseMaintenanceStatusTextBlock.Text =
                $"{LocalizationManager.Current.AlbumArtworkRepairing} {p.Current}/{p.Total} – {Path.GetFileName(p.CurrentFile)}");
        try
        {
            var repaired = await LibraryScanner.RepairMissingAlbumArtworkAsync(progress);
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.AlbumArtworkRepaired, repaired);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(LocalizationManager.Current.AlbumArtworkRepairFailed, ex.Message);
        }
        finally
        {
            RepairAlbumArtworkButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void NormalizeArtistsButton_OnClick(object sender, RoutedEventArgs e)
    {
        NormalizeArtistsButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.ArtistsNormalizing;
        try
        {
            var result = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                var normalization = db.NormalizeArtists();
                TrackSearchIndex.Rebuild(db.GetAll().ToList());
                return normalization;
            });
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.ArtistsNormalized,
                result.MergedArtists,
                result.UpdatedTracks);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.ArtistNormalizationFailed,
                ex.Message);
        }
        finally
        {
            NormalizeArtistsButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void DownloadMissingArtworkButton_OnClick(object sender, RoutedEventArgs e)
    {
        DownloadMissingArtworkButton.IsEnabled = false;
        SetBackupButtonsEnabled(false);
        DatabaseMaintenanceStatusTextBlock.Text = LocalizationManager.Current.MissingArtworkDownloading;
        var progress = new Progress<ScanProgress>(p =>
            DatabaseMaintenanceStatusTextBlock.Text =
                $"{LocalizationManager.Current.MissingArtworkDownloading} {p.Current}/{p.Total}");
        try
        {
            var downloaded = await LibraryScanner.DownloadMissingAlbumArtworkAsync(progress);
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.MissingArtworkDownloaded, downloaded);
        }
        catch (Exception ex)
        {
            DatabaseMaintenanceStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.MissingArtworkDownloadFailed, ex.Message);
        }
        finally
        {
            DownloadMissingArtworkButton.IsEnabled = true;
            UpdateBackupButtonAvailability();
        }
    }

    private async void ExportLibraryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartLibraryBackupOperation())
            return;

        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.Current.ExportLibrary,
            Filter = LocalizationManager.Current.LibraryArchiveFilter,
            DefaultExt = ".zip",
            AddExtension = true,
            FileName = $"Player-library-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        SetLibraryOperationControlsEnabled(false);
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryExporting;
        LibraryExportProgressBar.Value = 0;
        LibraryExportProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<LibraryExportProgress>(value =>
        {
            LibraryExportProgressBar.Value = value.Percentage;
            LibraryBackupStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.LibraryExportProgress,
                value.Percentage,
                value.CurrentFile ?? string.Empty);
        });
        try
        {
            await LibraryBackupService.ExportAsync(dialog.FileName, _libraryPaths, progress);
            LibraryExportProgressBar.Value = 100;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryExported, dialog.FileName);
        }
        catch (Exception ex)
        {
            LibraryExportProgressBar.Visibility = Visibility.Collapsed;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryExportFailed, ex.Message);
        }
        finally
        {
            SetLibraryOperationControlsEnabled(true);
        }
    }

    private async void ImportLibraryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!CanStartLibraryBackupOperation())
            return;

        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Current.ImportLibrary,
            Filter = LocalizationManager.Current.LibraryArchiveFilter,
            DefaultExt = ".zip",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var confirmation = WpfMessageBox.Show(
            this,
            LocalizationManager.Current.LibraryImportConfirm,
            LocalizationManager.Current.ImportLibrary,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
            return;

        SetLibraryOperationControlsEnabled(false);
        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryImporting;
        LibraryExportProgressBar.Value = 0;
        LibraryExportProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<LibraryImportProgress>(value =>
        {
            LibraryExportProgressBar.Value = value.Percentage;
            LibraryBackupStatusTextBlock.Text = string.Format(
                LocalizationManager.Current.LibraryImportProgress,
                value.Percentage,
                value.CurrentFile ?? string.Empty);
        });
        try
        {
            if (Owner is MainWindow mainWindow)
                mainWindow.PrepareForLibraryImport();

            var importedPaths = await LibraryBackupService.ImportAsync(dialog.FileName, progress);
            LibraryExportProgressBar.Value = 100;
            _libraryPaths.Clear();
            _libraryPaths.AddRange(importedPaths);
            _settings.LibraryPaths = _libraryPaths.ToList();
            _onLibraryPathsChanged?.Invoke(_libraryPaths.ToList());
            RebuildDirectoryList();
            LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryImported;

            WpfMessageBox.Show(
                this,
                LocalizationManager.Current.LibraryImported,
                LocalizationManager.Current.ImportLibrary,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            LibraryExportProgressBar.Visibility = Visibility.Collapsed;
            LibraryBackupStatusTextBlock.Text =
                string.Format(LocalizationManager.Current.LibraryImportFailed, ex.Message);
            SetLibraryOperationControlsEnabled(true);
        }
    }

    private bool CanStartLibraryBackupOperation()
    {
        var maintenanceActive =
            !OptimizeDatabaseButton.IsEnabled ||
            !RepairAlbumArtworkButton.IsEnabled ||
            !NormalizeArtistsButton.IsEnabled ||
            !DownloadMissingArtworkButton.IsEnabled;
        if (_activeScans.Count == 0 && !maintenanceActive)
            return true;

        LibraryBackupStatusTextBlock.Text = LocalizationManager.Current.LibraryOperationScanActive;
        return false;
    }

    private void SetBackupButtonsEnabled(bool enabled)
    {
        ExportLibraryButton.IsEnabled = enabled;
        ImportLibraryButton.IsEnabled = enabled;
    }

    private void UpdateBackupButtonAvailability()
    {
        SetBackupButtonsEnabled(
            _activeScans.Count == 0 &&
            OptimizeDatabaseButton.IsEnabled &&
            RepairAlbumArtworkButton.IsEnabled &&
            NormalizeArtistsButton.IsEnabled &&
            DownloadMissingArtworkButton.IsEnabled);
    }

    private void SetLibraryOperationControlsEnabled(bool enabled)
    {
        AddDirectoryButton.IsEnabled = enabled;
        OptimizeDatabaseButton.IsEnabled = enabled;
        RepairAlbumArtworkButton.IsEnabled = enabled;
        NormalizeArtistsButton.IsEnabled = enabled;
        DownloadMissingArtworkButton.IsEnabled = enabled;
        SetBackupButtonsEnabled(enabled);
        DirectoriesPanel.IsEnabled = enabled;
    }

    // ------------------------------------------------------------------
    // Dialog
    // ------------------------------------------------------------------

    private void SaveButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
