using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Orynivo.Audio;
using Orynivo.Library;
using Orynivo.Localization;
using System.Runtime.InteropServices;

namespace Orynivo;

public partial class App : Application
{
    private const string AppUserModelId = "Orynivo.AudioPlayer";
    private int _fatalExceptionHandling;

    public override void Initialize()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            RegisterCrashHandlers();
            StartupTimingLog.Start();
            StartupDiagnostics.Log = StartupTimingLog.Write;
            if (OperatingSystem.IsWindows())
            {
                using var appIdTiming = StartupTimingLog.Time("Set AppUserModelID");
                SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            }
            using (StartupTimingLog.Time("AppPaths.MigrateLegacyData"))
                AppPaths.MigrateLegacyData();

            ThemeManager.Apply(AppTheme.Dark);
            var startup = new StartupWindow();
            startup.Show();

            _ = InitializeAsync(desktop, startup);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        StartupWindow startup)
    {
        try
        {
            using var totalTiming = StartupTimingLog.Time("InitializeAsync before main window");
            AppSettings settings;
            using (StartupTimingLog.Time("SettingsStore.Load"))
                settings = new SettingsStore().Load();
            using (StartupTimingLog.Time("ThemeManager.Apply"))
                ThemeManager.Apply(settings.Theme);
            using (StartupTimingLog.Time("LocalizationManager.Apply"))
                LocalizationManager.Apply(settings.Language);

            bool ffmpegAvailable;
            using (StartupTimingLog.Time("FfmpegLocator.EnsureAvailableAsync"))
            {
                ffmpegAvailable = await FfmpegLocator.EnsureAvailableAsync(
                    new Progress<string>(status =>
                    {
                        StartupTimingLog.Write($"FFmpeg status: {status}");
                        startup.Status = status;
                    }));
            }
            if (!ffmpegAvailable)
                await AppMessageBox.ShowAsync(LocalizationManager.Current.FfmpegDownloadFailed);

            startup.Status = LocalizationManager.Current.StartupPreparingLibrary;
            using (StartupTimingLog.Time("AudioDatabase.OpenDefault startup preparation"))
            {
                await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                });
            }
            StartupDiagnostics.Log = null;

            using (StartupTimingLog.Time("MainWindow constructor/show"))
            {
                var main = new MainWindow();
                desktop.MainWindow = main;
                main.Show();

                _ = EnsureSearchIndexAsync(main);
            }
        }
        catch (Exception ex)
        {
            HandleFatalException(ex, "Startup");
        }
        finally
        {
            startup.Close();
        }
    }

    private static async Task EnsureSearchIndexAsync(MainWindow main)
    {
        try
        {
            main.SetStatusText(LocalizationManager.Current.StartupCheckingSearchIndex);
            bool searchIndexCurrent;
            using (StartupTimingLog.Time("TrackSearchIndex.IsCurrent background"))
                searchIndexCurrent = await Task.Run(TrackSearchIndex.IsCurrent);
            if (searchIndexCurrent)
            {
                main.ClearStatusText(LocalizationManager.Current.StartupCheckingSearchIndex);
                return;
            }

            using (StartupTimingLog.Time("TrackSearchIndex.Rebuild background"))
            {
                await Task.Run(() =>
                {
                    using var db = AudioDatabase.OpenDefault();
                    TrackSearchIndex.Rebuild(db.GetAll(), (current, total, _) =>
                    {
                        var status = string.Format(
                            LocalizationManager.Current.SearchIndexRebuilding,
                            current,
                            total);
                        Dispatcher.UIThread.Post(() => main.SetStatusText(status));
                        if (current == total || current % 5000 == 0)
                            StartupTimingLog.Write($"Search index rebuild progress: {current}/{total}");
                    });
                });
            }

            main.SetStatusText(LocalizationManager.Current.SearchIndexReady);
            await Task.Delay(TimeSpan.FromSeconds(5));
            main.ClearStatusText(LocalizationManager.Current.SearchIndexReady);
        }
        catch (Exception ex)
        {
            CrashLogger.Log(ex, "Search index rebuild");
            main.SetStatusText(string.Format(
                LocalizationManager.Current.SearchIndexFailed,
                ex.Message));
        }
        finally
        {
            StartupTimingLog.Write("Startup diagnostics completed");
            StartupDiagnostics.Log = null;
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    private void RegisterCrashHandlers()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleFatalException(e.Exception, "Avalonia UI Thread");
    }

    private void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new Exception($"Unhandled non-Exception object: {e.ExceptionObject}");
        CrashLogger.Log(exception, "AppDomain");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogger.Log(e.Exception, "Unobserved task");
        e.SetObserved();
    }

    private void HandleFatalException(Exception exception, string source)
    {
        if (Interlocked.Exchange(ref _fatalExceptionHandling, 1) != 0)
            return;

        var logPath = CrashLogger.Log(exception, source);
        try
        {
            var message = string.IsNullOrWhiteSpace(logPath)
                ? LocalizationManager.Current.CrashMessageWithoutLog
                : string.Format(LocalizationManager.Current.CrashMessage, logPath);
            _ = AppMessageBox.ShowAsync(message, LocalizationManager.Current.CrashTitle);
        }
        catch { }
        finally
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(-1);
            else
                Environment.Exit(-1);
        }
    }
}
