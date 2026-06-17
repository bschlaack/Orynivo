using Avalonia.Controls;

namespace Orynivo;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }
}
