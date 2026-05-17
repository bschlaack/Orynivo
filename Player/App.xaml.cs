using System.Windows;
using Player.Library;

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
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
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
