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
            if (OperatingSystem.IsWindows())
                SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            AppPaths.MigrateLegacyData();

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
            var settings = new SettingsStore().Load();
            ThemeManager.Apply(settings.Theme);
            LocalizationManager.Apply(settings.Language);

            var ffmpegAvailable = await FfmpegLocator.EnsureAvailableAsync(
                new Progress<string>(status => startup.Status = status));
            if (!ffmpegAvailable)
                await AppMessageBox.ShowAsync(LocalizationManager.Current.FfmpegDownloadFailed);

            startup.Status = LocalizationManager.Current.StartupPreparingLibrary;
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                if (!TrackSearchIndex.IsCurrent())
                    TrackSearchIndex.Rebuild(db.GetAll());
            });

            var main = new MainWindow();
            desktop.MainWindow = main;
            main.Show();
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
