using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Orynivo.Localization;
using Orynivo.Streaming;
using Orynivo.Updates;
using System.Diagnostics;
using System.Reflection;

namespace Orynivo;

public partial class AboutWindow : Window
{
    private readonly string _version;
    private readonly string _updatePublicKey;
    private readonly IReadOnlyList<OrynivoServerSettings> _servers;
    private ReleaseAssetInfo? _availableInstaller;

    /// <summary>Initializes the About window and displays the build-time release version.</summary>
    public AboutWindow() : this([])
    {
    }

    /// <summary>Initializes the About window with servers that should follow a desktop update.</summary>
    /// <param name="servers">Configured Orynivo Servers eligible for a matching signed update.</param>
    internal AboutWindow(IReadOnlyList<OrynivoServerSettings> servers)
    {
        InitializeComponent();
        _servers = servers;
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
            var verified = await updates.GetLatestManifestBundleAsync();
            var installer = verified.Manifest.Assets.FirstOrDefault(asset =>
                asset.Component == "desktop" && asset.OperatingSystem == "windows" &&
                asset.Architecture == "x64" && asset.Type == "installer" &&
                asset.File == _availableInstaller.File)
                ?? throw new InvalidOperationException("The selected desktop installer is no longer available.");
            var installerPath = await updates.DownloadAssetAsync(installer);
            var failedServers = await UpdateServersAsync(updates, verified);
            if (failedServers.Count > 0)
            {
                var proceed = await AppMessageBox.ConfirmAsync(
                    string.Format(
                        LocalizationManager.Current.ServerUpdatesFailedContinue,
                        string.Join(", ", failedServers)),
                    LocalizationManager.Current.Updates,
                    this);
                if (!proceed)
                {
                    File.Delete(installerPath);
                    CheckForUpdatesButton.IsEnabled = true;
                    InstallUpdateButton.IsEnabled = true;
                    UpdateStatusTextBlock.Text = LocalizationManager.Current.UpdateFailed;
                    return;
                }
            }
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

    /// <summary>Relays the matching signed package to every reachable update-enabled server.</summary>
    /// <param name="updates">Release service used to download verified package assets.</param>
    /// <param name="verified">Verified manifest shared with the desktop update.</param>
    /// <returns>Names of servers whose required update failed.</returns>
    private async Task<IReadOnlyList<string>> UpdateServersAsync(
        ReleaseUpdateService updates,
        VerifiedReleaseManifest verified)
    {
        if (_servers.Count == 0)
            return [];

        var failures = new List<string>();
        var packages = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var client = new OrynivoServerClient();
            foreach (var server in _servers)
            {
                UpdateStatusTextBlock.Text = string.Format(
                    LocalizationManager.Current.UpdatingNamedServer,
                    server.Name);
                try
                {
                    var info = await client.TestConnectionAsync(server);
                    var status = await client.GetUpdateStatusAsync(server);
                    if (info is null || status is not { Enabled: true, Supported: true })
                        continue;
                    if (!ReleaseUpdateService.IsNewer(info.Version, verified.Manifest.Version))
                        continue;
                    var asset = verified.Manifest.Assets.FirstOrDefault(candidate =>
                        candidate.Component == "server" && candidate.OperatingSystem == "linux" &&
                        candidate.Architecture == status.Architecture && candidate.Type == status.InstallType);
                    if (asset is null)
                    {
                        failures.Add(server.Name);
                        continue;
                    }
                    if (!packages.TryGetValue(asset.File, out var packagePath))
                    {
                        packagePath = await updates.DownloadAssetAsync(asset);
                        packages.Add(asset.File, packagePath);
                    }
                    await client.UploadUpdateAsync(server, verified, packagePath, asset.File);
                    await client.ApplyUpdateAsync(server);
                }
                catch (Exception exception)
                {
                    CrashLogger.Log(exception, $"Server update relay ({server.Name})");
                    failures.Add(server.Name);
                }
            }
        }
        finally
        {
            foreach (var packagePath in packages.Values)
                File.Delete(packagePath);
        }
        return failures;
    }
}
