using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Orynivo;

public partial class EditArtistNameDialog : Window
{
    public string ArtistName => NameTextBox.Text.Trim();

    public EditArtistNameDialog(string artistName)
    {
        InitializeComponent();
        NameTextBox.Text = artistName;
        Loaded += (_, _) =>
        {
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void NameTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        RenameButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void NameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && RenameButton.IsEnabled)
            DialogResult = true;
    }

    private void RenameButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;
    private void EditArtistNameDialog_OnSourceInitialized(object sender, EventArgs e) =>
        ApplyTitleBarColors();

    private void ApplyTitleBarColors()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor = 36;
            var dark = System.Windows.Application.Current.Resources["AppHeaderBrush"] is System.Windows.Media.SolidColorBrush brush &&
                       brush.Color == System.Windows.Media.Color.FromRgb(0x13, 0x14, 0x2A);
            var captionColor = dark ? ColorRef(0x13, 0x14, 0x2A) : ColorRef(0xEA, 0xEA, 0xF5);
            var textColor = dark ? ColorRef(0xFF, 0xFF, 0xFF) : ColorRef(0x13, 0x14, 0x2A);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor, ref textColor, sizeof(int));
        }
        catch
        {
        }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int dwAttribute,
        ref int pvAttribute,
        int cbAttribute);
}
