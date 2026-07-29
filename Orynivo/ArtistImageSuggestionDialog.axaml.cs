using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>Displays an automatically discovered artist image for explicit approval.</summary>
public partial class ArtistImageSuggestionDialog : Window
{
    /// <summary>Initializes a runtime-loader instance with an empty preview.</summary>
    public ArtistImageSuggestionDialog()
        : this(string.Empty, string.Empty, string.Empty, [], false, false)
    {
    }

    /// <summary>Initializes an artist-image suggestion dialog.</summary>
    /// <param name="artistName">Artist whose image is being proposed.</param>
    /// <param name="sourceName">Name of the image provider.</param>
    /// <param name="progressText">Current batch progress and remaining-time estimate.</param>
    /// <param name="imageData">Raw preview image bytes.</param>
    /// <param name="canAutoAcceptFanartTv">
    /// Whether the Fanart.tv automatic-accept option is available because an API key is configured.
    /// </param>
    /// <param name="autoAcceptFanartTv">Current automatic-accept selection.</param>
    public ArtistImageSuggestionDialog(
        string artistName,
        string sourceName,
        string progressText,
        byte[] imageData,
        bool canAutoAcceptFanartTv,
        bool autoAcceptFanartTv)
    {
        InitializeComponent();
        ArtistNameTextBlock.Text = artistName;
        SourceTextBlock.Text = string.Format(LocalizationManager.Current.ArtistImageSuggestionSource, sourceName);
        ProgressTextBlock.Text = progressText;
        AutoAcceptFanartTvCheckBox.IsVisible = canAutoAcceptFanartTv;
        AutoAcceptFanartTvCheckBox.IsChecked = canAutoAcceptFanartTv && autoAcceptFanartTv;
        if (imageData.Length > 0)
        {
            using var stream = new MemoryStream(imageData);
            SuggestionImage.Source = new Bitmap(stream);
        }
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }

    /// <summary>Gets whether future Fanart.tv results should be accepted automatically.</summary>
    public bool AutoAcceptFanartTv => AutoAcceptFanartTvCheckBox.IsChecked == true;

    private void RejectButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(false);

    private void AcceptButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Close(true);
}
