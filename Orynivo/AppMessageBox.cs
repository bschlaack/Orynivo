using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Avalonia-native replacement for System.Windows.MessageBox.</summary>
internal static class AppMessageBox
{
    /// <summary>Shows a themed informational message dialog.</summary>
    /// <param name="message">Message displayed by the dialog.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="owner">Optional owning window.</param>
    /// <returns>A task that completes when the dialog closes.</returns>
    public static Task ShowAsync(string message, string title = "Orynivo", Window? owner = null) =>
        Dispatcher.UIThread.InvokeAsync(() => ShowInternal(message, title, owner));

    /// <summary>Shows a themed confirmation dialog.</summary>
    /// <param name="message">Confirmation question displayed by the dialog.</param>
    /// <param name="title">Dialog title.</param>
    /// <param name="owner">Optional owning window.</param>
    /// <param name="confirmText">Optional label for the primary confirmation action.</param>
    /// <returns><see langword="true"/> when the user confirms; otherwise <see langword="false"/>.</returns>
    public static Task<bool> ConfirmAsync(
        string message,
        string title = "Orynivo",
        Window? owner = null,
        string? confirmText = null) =>
        Dispatcher.UIThread.InvokeAsync(() => ConfirmInternal(message, title, owner, confirmText));

    private static async Task<bool> ConfirmInternal(
        string message,
        string title,
        Window? owner,
        string? confirmText)
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
            Background = GetBrush("AppSurfaceBrush", "#18192E")
        };

        var yes = new Button
        {
            Content = string.IsNullOrWhiteSpace(confirmText) ? "OK" : confirmText,
            MinWidth = 90,
            Height = 32,
            Padding = new Thickness(12, 0),
            Background = GetBrush("AppTransportPlayBrush", "#6C63FF"),
            Foreground = GetBrush("AppButtonTextBrush", "#FFFFFF"),
            BorderBrush = GetBrush("AppTransportPlayBrush", "#6C63FF"),
            BorderThickness = new Thickness(1)
        };
        var no = new Button
        {
            Content = LocalizationManager.Current.Cancel,
            Width = 90,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            Background = GetBrush("AppButtonBrush", "#252640"),
            Foreground = GetBrush("AppButtonTextBrush", "#FFFFFF"),
            BorderBrush = GetBrush("AppButtonBorderBrush", "#343654"),
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
                    Foreground = GetBrush("AppPrimaryTextBrush", "#F7F7FF"),
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
            Background = GetBrush("AppSurfaceBrush", "#18192E")
        };

        var ok = new Button
        {
            Content = "OK",
            Width = 90,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = GetBrush("AppButtonBrush", "#252640"),
            Foreground = GetBrush("AppButtonTextBrush", "#FFFFFF"),
            BorderBrush = GetBrush("AppButtonBorderBrush", "#343654"),
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
                    Foreground = GetBrush("AppPrimaryTextBrush", "#F7F7FF"),
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

    private static IBrush GetBrush(string key, string fallback) =>
        Application.Current?.Resources.TryGetResource(key, null, out var value) == true
            && value is IBrush brush
            ? brush
            : new SolidColorBrush(Color.Parse(fallback));
}
