using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;

namespace Orynivo;

/// <summary>
/// Applies themed Win32 DWM title-bar colours to an Avalonia window on Windows 10/11.
/// Safe to call on non-Windows platforms — does nothing.
/// </summary>
internal static class WindowChrome
{
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int cb);

    public static void ApplyTheme(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) return;
        try
        {
            var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            var dark = IsDarkTheme();
            var caption = dark ? Rgb(0x13, 0x14, 0x2A) : Rgb(0xEA, 0xEA, 0xF5);
            var text    = dark ? Rgb(0xFF, 0xFF, 0xFF) : Rgb(0x13, 0x14, 0x2A);
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            DwmSetWindowAttribute(hwnd, DwmwaTextColor,    ref text,    sizeof(int));
        }
        catch { }
    }

    private static bool IsDarkTheme()
    {
        if (Avalonia.Application.Current?.Resources["AppHeaderBrush"] is SolidColorBrush b)
            return b.Color.R < 128;
        return true;
    }

    private static int Rgb(byte r, byte g, byte b) => r | (g << 8) | (b << 16);
}
