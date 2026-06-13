using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Player;

public partial class NewPlaylistDialog : Window
{
    public string? PlaylistName { get; private set; }

    public NewPlaylistDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameTextBox.Focus();
    }

    private void NewPlaylistDialog_OnSourceInitialized(object sender, EventArgs e)
        => ApplyWindowTitleBarColors();

    private void NameTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => CreateButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);

    private void NameTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(NameTextBox.Text))
            Confirm();
    }

    private void CreateButton_OnClick(object sender, RoutedEventArgs e) => Confirm();

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Confirm()
    {
        var name = NameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;
        PlaylistName = name;
        DialogResult = true;
    }

    private void ApplyWindowTitleBarColors()
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            const int DwmwaCaptionColor = 35;
            const int DwmwaTextColor    = 36;
            var captionColor = ColorRef(0x13, 0x14, 0x2A);
            var textColor    = ColorRef(0xFF, 0xFF, 0xFF);
            _ = DwmSetWindowAttribute(handle, DwmwaCaptionColor, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaTextColor,    ref textColor,    sizeof(int));
        }
        catch { }
    }

    private static int ColorRef(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
