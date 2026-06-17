using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Orynivo;

/// <summary>Avalonia-native replacement for System.Windows.MessageBox.</summary>
internal static class AppMessageBox
{
    public static Task ShowAsync(string message, string title = "Orynivo", Window? owner = null) =>
        Dispatcher.UIThread.InvokeAsync(() => ShowInternal(message, title, owner));

    public static Task<bool> ConfirmAsync(string message, string title = "Orynivo", Window? owner = null) =>
        Dispatcher.UIThread.InvokeAsync(() => ConfirmInternal(message, title, owner));

    private static async Task<bool> ConfirmInternal(string message, string title, Window? owner)
    {
        var result = false;
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#18192E"))
        };

        var yes = new Button
        {
            Content = "OK",
            Width = 90,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#6C63FF")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.Parse("#6C63FF")),
            BorderThickness = new Thickness(1)
        };
        var no = new Button
        {
            Content = "Cancel",
            Width = 90,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.Parse("#252640")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.Parse("#343654")),
            BorderThickness = new Thickness(1)
        };
        yes.Click += (_, _) => { result = true; dlg.Close(); };
        no.Click += (_, _) => dlg.Close();

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Color.Parse("#F7F7FF")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 392
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { no, yes }
                }
            }
        };

        dlg.Opened += (_, _) => WindowChrome.ApplyTheme(dlg);

        if (owner is not null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();

        return result;
    }

    private static async Task ShowInternal(string message, string title, Window? owner)
    {
        var dlg = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = owner is null
                ? WindowStartupLocation.CenterScreen
                : WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.Parse("#18192E"))
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = new SolidColorBrush(Color.Parse("#252640")),
            Foreground = new SolidColorBrush(Colors.White),
            BorderBrush = new SolidColorBrush(Color.Parse("#343654")),
            BorderThickness = new Thickness(1)
        };
        ok.Click += (_, _) => dlg.Close();

        dlg.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    Foreground = new SolidColorBrush(Color.Parse("#F7F7FF")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 392
                },
                ok
            }
        };

        WindowChrome.ApplyTheme(dlg);
        dlg.Opened += (_, _) => WindowChrome.ApplyTheme(dlg);

        if (owner is not null)
            await dlg.ShowDialog(owner);
        else
            dlg.Show();
    }
}
