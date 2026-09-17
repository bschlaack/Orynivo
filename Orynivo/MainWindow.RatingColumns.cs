using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using Orynivo.Audio;
using Orynivo.Controls;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;
using Windows.Media;

namespace Orynivo;

/// <summary>
/// Personal and MusicBrainz community rating columns for the shared track tables.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the interactive five-star personal track-rating column.</summary>
    /// <returns>A rating column that persists local and Orynivo Server values.</returns>
    private DataGridColumn CreatePersonalRatingColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = LocalizationManager.Current.PersonalRating,
            SortMemberPath = nameof(ContentRow.UserRating),
            Width = new DataGridLength(118),
            CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
            {
                var panel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 0,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                var buttons = new List<Button>(5);
                for (var value = 1; value <= 5; value++)
                {
                    var starValue = value;
                    var button = new Button
                    {
                        Content = "★",
                        Width = 21,
                        Height = 25,
                        MinWidth = 0,
                        MinHeight = 0,
                        Padding = new Thickness(0),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = FindResource<IBrush>("AppFavoriteBrush"),
                        FontFamily = new FontFamily("Segoe UI Symbol"),
                        FontSize = ResolveFontSize("FontSizeBodyStrong"),
                        Cursor = new Cursor(StandardCursorType.Hand)
                    };
                    ToolTip.SetTip(button, $"{LocalizationManager.Current.RatingSetHint}: {value}/5");
                    button.Click += async (_, e) =>
                    {
                        e.Handled = true;
                        await SetPersonalTrackRatingAsync(row, row.UserRating == starValue ? 0 : starValue);
                    };
                    buttons.Add(button);
                    panel.Children.Add(button);
                }

                void RefreshStars()
                {
                    for (var index = 0; index < buttons.Count; index++)
                        buttons[index].Opacity = index < row.UserRating ? 1 : 0.28;
                }
                RefreshStars();
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ContentRow.UserRating))
                        RefreshStars();
                };
                return panel;
            })
        };
        column.Tag = "personalRating";
        return column;
    }

    /// <summary>Creates the optional cached MusicBrainz rating column with an explicit refresh action.</summary>
    /// <returns>A community-rating column.</returns>
    private DataGridColumn CreateMusicBrainzRatingColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = LocalizationManager.Current.MusicBrainzRating,
            SortMemberPath = nameof(ContentRow.MusicBrainzRating),
            Width = new DataGridLength(160),
            CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
            {
                var button = new Button
                {
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                button.Bind(Button.ContentProperty, new Binding(nameof(ContentRow.MusicBrainzRatingDisplay)));
                ToolTip.SetTip(button, LocalizationManager.Current.MusicBrainzRating);
                button.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await RefreshMusicBrainzTrackRatingForegroundAsync(row);
                };
                return button;
            })
        };
        column.Tag = "musicBrainzRating";
        return column;
    }
}
