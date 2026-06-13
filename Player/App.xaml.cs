using System.Windows;
using Player.Library;
using Player.Localization;

namespace Player;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startup = new StartupWindow();
        startup.Show();

        try
        {
            startup.Status = "Bibliothek wird vorbereitet …";
            var settings = new SettingsStore().Load();
            ThemeManager.Apply(settings.Theme);
            LocalizationManager.Apply(settings.Language);
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
}
