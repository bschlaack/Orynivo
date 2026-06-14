using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class App : System.Windows.Application
{
    private const string AppUserModelId = "Orynivo.AudioPlayer";
    private int _fatalExceptionHandling;

    protected override async void OnStartup(StartupEventArgs e)
    {
        RegisterCrashHandlers();
        base.OnStartup(e);
        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        AppPaths.MigrateLegacyData();

        var startup = new StartupWindow();
        startup.Show();

        try
        {
            var settings = new SettingsStore().Load();
            ThemeManager.Apply(settings.Theme);
            LocalizationManager.Apply(settings.Language);
            startup.Status = LocalizationManager.Current.StartupPreparingLibrary;
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                if (!TrackSearchIndex.IsCurrent())
                    TrackSearchIndex.Rebuild(db.GetAll());
            });

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
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
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleFatalException(e.Exception, "WPF Dispatcher");
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
            System.Windows.MessageBox.Show(
                message,
                LocalizationManager.Current.CrashTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
        finally
        {
            Shutdown(-1);
        }
    }
}
