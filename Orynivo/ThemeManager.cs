using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaApp = Avalonia.Application;

namespace Orynivo;

/// <summary>
/// Applies a light or dark colour scheme by writing named <see cref="SolidColorBrush"/> resources
/// into <see cref="Avalonia.Application.Current"/> resources so all controls pick them up dynamically.
/// </summary>
public static class ThemeManager
{
    /// <summary>Switches the global Avalonia resource dictionary to the specified theme.</summary>
    public static void Apply(AppTheme theme)
    {
        var app = AvaloniaApp.Current!;
        var resources = app.Resources;
        var dark = theme == AppTheme.Dark;

        // Align the built-in Fluent theme variant with the selected scheme so that
        // any control still using default Fluent resources (e.g. code-created
        // checkboxes) resolves dark-appropriate colours instead of light-variant
        // borders that would appear near-black on the dark background.
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        resources["AppAccentBrush"]              = Brush(dark ? "#4FDDBD" : "#2563EB");
        resources["AppAccentTextBrush"]          = Brush(dark ? "#102033" : "#FFFFFF");
        resources["AppAccentSoftBrush"]          = Brush(dark ? "#163E3C" : "#DDEBFF");
        resources["AppSidebarBrush"]             = Brush(dark ? "#0E1116" : "#F5F7FA");
        resources["AppHeaderBrush"]              = Brush(dark ? "#11151B" : "#FFFFFF");
        resources["AppHeaderBorderBrush"]        = Brush(dark ? "#20262E" : "#E3E8EF");
        resources["AppContentBrush"]             = Brush(dark ? "#141922" : "#F7F9FC");
        resources["AppSurfaceBrush"]             = Brush(dark ? "#1A2029" : "#FFFFFF");
        resources["AppSurfaceHoverBrush"]        = Brush(dark ? "#222B36" : "#EEF4FF");
        resources["AppSurfaceSelectedBrush"]     = Brush(dark ? "#26333E" : "#E4EDFF");
        resources["AppNowPlayingRowBrush"]       = Brush(dark ? "#173B38" : "#DDF7F0");
        resources["AppNavHoverBrush"]            = Brush(dark ? "#171D25" : "#EAF0F7");
        resources["AppNavSelectedBrush"]         = Brush(dark ? "#1B2F35" : "#DDEBFF");
        resources["AppNavTextBrush"]             = Brush(dark ? "#98A2B3" : "#566374");
        resources["AppNavHoverTextBrush"]        = Brush(dark ? "#D6DEE8" : "#111827");
        resources["AppNavSelectedTextBrush"]     = Brush(dark ? "#FFFFFF" : "#102033");
        resources["AppPrimaryTextBrush"]         = Brush(dark ? "#F4F7FA" : "#111827");
        resources["AppSecondaryTextBrush"]       = Brush(dark ? "#AAB4C3" : "#566374");
        resources["AppMutedTextBrush"]           = Brush(dark ? "#6F7B8B" : "#8A96A8");
        resources["AppGridLineBrush"]            = Brush(dark ? "#27313D" : "#E3E8EF");
        resources["AppColumnHeaderBrush"]        = Brush(dark ? "#161D26" : "#F1F5F9");
        resources["AppTransportBrush"]           = Brush(dark ? "#0E1116" : "#FFFFFF");
        resources["AppTransportBorderBrush"]     = Brush(dark ? "#20262E" : "#E3E8EF");
        resources["AppTransportPrimaryTextBrush"] = Brush(dark ? "#FFFFFF" : "#111827");
        resources["AppTransportButtonBrush"]     = Brush(dark ? "#1A2029" : "#F1F5F9");
        resources["AppTransportButtonHoverBrush"] = Brush(dark ? "#26313D" : "#E2E8F0");
        resources["AppTransportButtonDisabledBrush"] = Brush(dark ? "#171D25" : "#F4F6FA");
        resources["AppTransportButtonTextBrush"] = Brush(dark ? "#D6DEE8" : "#334155");
        resources["AppTransportButtonHoverTextBrush"] = Brush(dark ? "#FFFFFF" : "#111827");
        resources["AppTransportButtonDisabledTextBrush"] = Brush(dark ? "#465568" : "#B0BAC7");
        resources["AppTransportPlayBrush"]       = Brush(dark ? "#4FDDBD" : "#2563EB");
        resources["AppTransportPlayHoverBrush"]  = Brush(dark ? "#67E8CC" : "#1D4ED8");
        resources["AppTransportPlayDisabledBrush"] = Brush(dark ? "#26313D" : "#E2E8F0");
        resources["AppTransportPlayDisabledTextBrush"] = Brush(dark ? "#465568" : "#AAB4C3");
        resources["AppArtworkPlaceholderBrush"]  = Brush(dark ? "#222A35" : "#E5EAF1");
        resources["AppScrollbarTrackBrush"]      = Brush(dark ? "#171D25" : "#EEF2F7");
        resources["AppScrollbarThumbBrush"]      = Brush(dark ? "#344150" : "#C4CDD8");
        resources["AppScrollbarThumbHoverBrush"] = Brush(dark ? "#465568" : "#9DAABC");
        resources["AppButtonBrush"]              = Brush(dark ? "#1B232D" : "#F1F5F9");
        resources["AppButtonHoverBrush"]         = Brush(dark ? "#26313D" : "#E2E8F0");
        resources["AppButtonPressedBrush"]       = Brush(dark ? "#31404E" : "#D8E0EA");
        resources["AppButtonTextBrush"]          = Brush(dark ? "#F4F7FA" : "#111827");
        resources["AppButtonBorderBrush"]        = Brush(dark ? "#344150" : "#CBD5E1");
        resources["AppInputBrush"]               = Brush(dark ? "#111820" : "#FFFFFF");
        resources["AppInputBorderBrush"]         = Brush(dark ? "#344150" : "#CBD5E1");
        resources["TextControlBackground"]       = Brush(dark ? "#111820" : "#FFFFFF");
        resources["TextControlBackgroundPointerOver"] = Brush(dark ? "#141C25" : "#FFFFFF");
        resources["TextControlBackgroundFocused"] = Brush(dark ? "#111820" : "#FFFFFF");
        resources["TextControlForeground"]       = Brush(dark ? "#F4F7FA" : "#111827");
        resources["TextControlForegroundPointerOver"] = Brush(dark ? "#F4F7FA" : "#111827");
        resources["TextControlForegroundFocused"] = Brush(dark ? "#F4F7FA" : "#111827");
        resources["TextControlBorderBrush"]      = Brush(dark ? "#344150" : "#CBD5E1");
        resources["TextControlBorderBrushPointerOver"] = Brush(dark ? "#465568" : "#94A3B8");
        resources["TextControlBorderBrushFocused"] = Brush(dark ? "#4FDDBD" : "#2563EB");
        resources["TextControlPlaceholderForeground"] = Brush(dark ? "#6F7B8B" : "#8A96A8");
        resources["TextControlPlaceholderForegroundPointerOver"] = Brush(dark ? "#6F7B8B" : "#8A96A8");
        resources["TextControlPlaceholderForegroundFocused"] = Brush(dark ? "#6F7B8B" : "#8A96A8");
        resources["AppDsdSupportedBrush"]        = Brush(dark ? "#F4F7FA" : "#111827");
        resources["AppDsdUnsupportedBrush"]      = Brush(dark ? "#566374" : "#AAB4C3");
        resources["TreeViewItemForeground"] = Brush(dark ? "#AAB4C3" : "#566374");
        resources["TreeViewItemForegroundPointerOver"] = Brush(dark ? "#F4F7FA" : "#111827");
        resources["TreeViewItemForegroundPressed"] = Brush(dark ? "#F4F7FA" : "#111827");
        resources["TreeViewItemForegroundSelected"] = Brush(dark ? "#FFFFFF" : "#111827");
        resources["TreeViewItemBackgroundSelected"] = Brush(dark ? "#293442" : "#E4EDFF");
        resources["TreeViewItemBackgroundPointerOver"] = Brush(dark ? "#222A35" : "#EEF4FF");
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
