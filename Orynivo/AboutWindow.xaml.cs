using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Orynivo.Localization;
using Orynivo.Updates;
using System.Diagnostics;
using System.Reflection;

namespace Orynivo;

public partial class AboutWindow : Window
{
    private readonly string _version;
    private readonly string _updatePublicKey;
    private ReleaseAssetInfo? _availableInstaller;

    /// <summary>Initializes the About window and displays the build-time release version.</summary>
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = typeof(AboutWindow).Assembly;
        _version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        _updatePublicKey = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "OrynivoUpdatePublicKey")?.Value ?? string.Empty;
        VersionTextBlock.Text = string.Format(LocalizationManager.Current.VersionLabel, _version);
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }

    private async void CheckForUpdatesButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsVisible = false;
        UpdateStatusTextBlock.Text = LocalizationManager.Current.CheckingForUpdates;
        try
        {
            using var updates = new ReleaseUpdateService(_updatePublicKey);
            var manifest = await updates.GetLatestManifestAsync();
            _availableInstaller = manifest.Assets.FirstOrDefault(asset =>
                asset.Component == "desktop" && asset.OperatingSystem == "windows" &&
                asset.Architecture == "x64" && asset.Type == "installer");
            var available = _availableInstaller is not null && ReleaseUpdateService.IsNewer(_version, manifest.Version);
            UpdateStatusTextBlock.Text = available
                ? string.Format(LocalizationManager.Current.UpdateAvailable, manifest.Version)
                : LocalizationManager.Current.UpToDate;
            InstallUpdateButton.IsVisible = available;
        }
        catch (InvalidOperationException) when (string.IsNullOrWhiteSpace(_updatePublicKey))
        {
            UpdateStatusTextBlock.Text = LocalizationManager.Current.UpdateUnavailable;
        }
        catch
        {
            UpdateStatusTextBlock.Text = LocalizationManager.Current.UpdateFailed;
        }
        finally
        {
            CheckForUpdatesButton.IsEnabled = true;
        }
    }

    private async void InstallUpdateButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_availableInstaller is null)
            return;
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatusTextBlock.Text = LocalizationManager.Current.DownloadingUpdate;
        try
        {
            using var updates = new ReleaseUpdateService(_updatePublicKey);
            var installerPath = await updates.DownloadAssetAsync(_availableInstaller);
            Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch
        {
            UpdateStatusTextBlock.Text = LocalizationManager.Current.UpdateFailed;
            CheckForUpdatesButton.IsEnabled = true;
            InstallUpdateButton.IsEnabled = true;
        }
    }
}
