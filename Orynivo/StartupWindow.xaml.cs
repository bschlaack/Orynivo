using Avalonia.Controls;

namespace Orynivo;

public partial class StartupWindow : Window
{
    public StartupWindow() => InitializeComponent();

    public string Status
    {
        set => StatusTextBlock.Text = value;
    }
}
