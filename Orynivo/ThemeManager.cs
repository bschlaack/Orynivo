using Avalonia.Media;
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
        var resources = AvaloniaApp.Current!.Resources;
        var dark = theme == AppTheme.Dark;

        resources["AppSidebarBrush"]             = Brush(dark ? "#13142A" : "#F4F4F8");
        resources["AppHeaderBrush"]              = Brush(dark ? "#13142A" : "#EAEAF5");
        resources["AppHeaderBorderBrush"]        = Brush(dark ? "#1E1F38" : "#D8D8EE");
        resources["AppContentBrush"]             = Brush(dark ? "#18192E" : "#F2F2FA");
        resources["AppSurfaceBrush"]             = Brush(dark ? "#20213A" : "#F8F8FD");
        resources["AppSurfaceHoverBrush"]        = Brush(dark ? "#292B47" : "#EAEAFF");
        resources["AppSurfaceSelectedBrush"]     = Brush(dark ? "#343654" : "#DDDDF5");
        resources["AppNowPlayingRowBrush"]       = Brush(dark ? "#302B5A" : "#E4E1FF");
        resources["AppNavHoverBrush"]            = Brush(dark ? "#1F2038" : "#E7E7F3");
        resources["AppNavSelectedBrush"]         = Brush(dark ? "#252640" : "#DDDDF5");
        resources["AppNavTextBrush"]             = Brush(dark ? "#9999BB" : "#5C5C7A");
        resources["AppNavHoverTextBrush"]        = Brush(dark ? "#CCCCEE" : "#35354E");
        resources["AppNavSelectedTextBrush"]     = Brush(dark ? "#FFFFFF" : "#13142A");
        resources["AppPrimaryTextBrush"]         = Brush(dark ? "#F7F7FF" : "#1A1A2E");
        resources["AppSecondaryTextBrush"]       = Brush(dark ? "#A0A0C8" : "#666688");
        resources["AppMutedTextBrush"]           = Brush(dark ? "#7777AA" : "#9999BB");
        resources["AppGridLineBrush"]            = Brush(dark ? "#2A2C46" : "#E4E4F0");
        resources["AppColumnHeaderBrush"]        = Brush(dark ? "#20213A" : "#EAEAF5");
        resources["AppTransportBrush"]           = Brush(dark ? "#13142A" : "#EAEAF5");
        resources["AppTransportBorderBrush"]     = Brush(dark ? "#1E1F38" : "#D8D8EE");
        resources["AppTransportPrimaryTextBrush"] = Brush(dark ? "#FFFFFF" : "#13142A");
        resources["AppTransportButtonBrush"]     = Brush(dark ? "#252640" : "#DDDDF5");
        resources["AppTransportButtonHoverBrush"] = Brush(dark ? "#343654" : "#D0D0E8");
        resources["AppTransportButtonDisabledBrush"] = Brush(dark ? "#20213A" : "#E3E3F0");
        resources["AppTransportButtonTextBrush"] = Brush(dark ? "#CCCCEE" : "#4B4B78");
        resources["AppTransportButtonHoverTextBrush"] = Brush(dark ? "#FFFFFF" : "#13142A");
        resources["AppTransportButtonDisabledTextBrush"] = Brush(dark ? "#3A3A55" : "#AAAAC0");
        resources["AppTransportPlayBrush"]       = Brush("#6C63FF");
        resources["AppTransportPlayHoverBrush"]  = Brush(dark ? "#7B73FF" : "#5D55F4");
        resources["AppTransportPlayDisabledBrush"] = Brush(dark ? "#2A2B45" : "#D7D7EA");
        resources["AppTransportPlayDisabledTextBrush"] = Brush(dark ? "#3A3A55" : "#A8A8BE");
        resources["AppArtworkPlaceholderBrush"]  = Brush(dark ? "#252640" : "#DADAE8");
        resources["AppScrollbarTrackBrush"]      = Brush(dark ? "#20213A" : "#ECECF4");
        resources["AppScrollbarThumbBrush"]      = Brush(dark ? "#343654" : "#C9C9DA");
        resources["AppScrollbarThumbHoverBrush"] = Brush(dark ? "#45486B" : "#B5B5CA");
        resources["AppButtonBrush"]              = Brush(dark ? "#252640" : "#E1E1F0");
        resources["AppButtonHoverBrush"]         = Brush(dark ? "#343654" : "#D4D4EA");
        resources["AppButtonPressedBrush"]       = Brush(dark ? "#3E4162" : "#C8C8E2");
        resources["AppButtonTextBrush"]          = Brush(dark ? "#F7F7FF" : "#13142A");
        resources["AppButtonBorderBrush"]        = Brush(dark ? "#343654" : "#CFCFE0");
        resources["AppInputBrush"]               = Brush(dark ? "#20213A" : "#FFFFFF");
        resources["AppInputBorderBrush"]         = Brush(dark ? "#343654" : "#CFCFE0");
        resources["TextControlBackground"]       = Brush(dark ? "#20213A" : "#FFFFFF");
        resources["TextControlBackgroundPointerOver"] = Brush(dark ? "#20213A" : "#FFFFFF");
        resources["TextControlBackgroundFocused"] = Brush(dark ? "#20213A" : "#FFFFFF");
        resources["TextControlForeground"]       = Brush(dark ? "#F7F7FF" : "#1A1A2E");
        resources["TextControlForegroundPointerOver"] = Brush(dark ? "#F7F7FF" : "#1A1A2E");
        resources["TextControlForegroundFocused"] = Brush(dark ? "#F7F7FF" : "#1A1A2E");
        resources["TextControlBorderBrush"]      = Brush(dark ? "#343654" : "#CFCFE0");
        resources["TextControlBorderBrushPointerOver"] = Brush(dark ? "#45486B" : "#B5B5CA");
        resources["TextControlBorderBrushFocused"] = Brush("#6C63FF");
        resources["TextControlPlaceholderForeground"] = Brush(dark ? "#7777AA" : "#777790");
        resources["TextControlPlaceholderForegroundPointerOver"] = Brush(dark ? "#7777AA" : "#777790");
        resources["TextControlPlaceholderForegroundFocused"] = Brush(dark ? "#7777AA" : "#777790");
        resources["AppDsdSupportedBrush"]        = Brush(dark ? "#F7F7FF" : "#13142A");
        resources["AppDsdUnsupportedBrush"]      = Brush(dark ? "#5B5D79" : "#B0B0C0");
        resources["TreeViewItemForeground"] = Brush(dark ? "#A0A0C8" : "#666688");
        resources["TreeViewItemForegroundPointerOver"] = Brush(dark ? "#F7F7FF" : "#1A1A2E");
        resources["TreeViewItemForegroundPressed"] = Brush(dark ? "#F7F7FF" : "#1A1A2E");
        resources["TreeViewItemForegroundSelected"] = Brush(dark ? "#FFFFFF" : "#13142A");
        resources["TreeViewItemBackgroundSelected"] = Brush(dark ? "#343654" : "#DDDDF5");
        resources["TreeViewItemBackgroundPointerOver"] = Brush(dark ? "#292B47" : "#EAEAFF");
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
