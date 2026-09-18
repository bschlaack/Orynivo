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
/// Dashboard listening statistics, trend chart, top genres/albums/artists, and quick access.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Arranges the listening-statistics blocks: a shared period selector governs the
    /// genre, album, and artist analytics cards, while the calendar keeps its own month
    /// navigation. Blocks sit side by side on wide dashboards and stack on narrow ones.
    /// </summary>
    /// <param name="calendarData">Per-day playback aggregates for the current month.</param>
    /// <param name="topGenres">Ranked genres by play time in the selected period.</param>
    /// <param name="topAlbums">Ranked albums by play time in the selected period.</param>
    /// <param name="topArtists">Ranked artists by play time in the selected period.</param>
    private void DashboardBuildStatsSection(
        List<CalendarDayData> calendarData,
        List<(string Genre, double Seconds)> topGenres,
        List<TopAlbumStat> topAlbums,
        List<TopArtistStat> topArtists,
        double totalListeningSeconds,
        double previousListeningSeconds,
        IReadOnlyList<double> listeningTrend,
        DashboardLibrarySummary librarySummary)
    {
        var overview = new Grid();
        var wide = _dashboardTwoColumnLayout == true;
        if (wide)
        {
            for (var column = 0; column < 7; column++)
                overview.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = column % 2 == 0
                        ? new GridLength(1, GridUnitType.Star)
                        : new GridLength(12)
                });
            overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        else
        {
            overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            overview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var cards = new Control[]
        {
            DashboardBuildListeningSummaryCard(totalListeningSeconds, previousListeningSeconds, listeningTrend),
            DashboardWrapOverviewCard(
                LocalizationManager.Current.TopGenres,
                DashboardBuildTopGenresCard(topGenres.Take(5).ToList())),
            DashboardBuildCompactCalendarCard(calendarData),
            DashboardBuildQuickAccessCard(librarySummary)
        };
        for (var i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], wide ? i * 2 : (i % 2) * 2);
            Grid.SetRow(cards[i], wide ? 0 : (i / 2) * 2);
            overview.Children.Add(cards[i]);
        }
        DashboardPanel.Children.Add(overview);

        var details = new Grid();
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var albumsCard = DashboardWrapOverviewCard(
            LocalizationManager.Current.TopAlbums,
            DashboardBuildTopAlbumsCard(topAlbums));
        details.Children.Add(albumsCard);
        var artistsCard = DashboardWrapOverviewCard(
            LocalizationManager.Current.TopArtists,
            DashboardBuildTopArtistsCard(topArtists));
        Grid.SetColumn(artistsCard, 2);
        details.Children.Add(artistsCard);
        DashboardPanel.Children.Add(details);

        var yearInReview = new Button
        {
            Content = LocalizationManager.Current.YearInReview,
            Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0)
        };
        yearInReview.Click += YearInReviewButton_OnClick;
        DashboardPanel.Children.Add(yearInReview);
    }

    private Border DashboardBuildListeningSummaryCard(
        double totalListeningSeconds,
        double previousListeningSeconds,
        IReadOnlyList<double> listeningTrend)
    {
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = Math.Round(totalListeningSeconds / 60).ToString("N0", CultureInfo.CurrentCulture),
            FontSize = ResolveFontSize("FontSizeHeadline"),
            FontWeight = FontWeight.Bold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
        });
        content.Children.Add(new TextBlock
        {
            Text = LocalizationManager.Current.DashboardTotalMinutes,
            FontSize = ResolveFontSize("FontSizeCaption"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush")
        });

        if (previousListeningSeconds > 0)
        {
            var change = (totalListeningSeconds - previousListeningSeconds) / previousListeningSeconds * 100;
            content.Children.Add(new TextBlock
            {
                Text = $"{(change >= 0 ? "▲" : "▼")} {Math.Abs(change):0}%  ·  {LocalizationManager.Current.PeriodPrevious}",
                FontSize = ResolveFontSize("FontSizeMeta"),
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(change >= 0 ? "#4FD58A" : "#FF806C")),
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        content.Children.Add(DashboardBuildListeningChart(listeningTrend));
        var periodBox = new ComboBox
        {
            MinWidth = 138,
            Height = 30,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[]
            {
                LocalizationManager.Current.PeriodAllTime,
                LocalizationManager.Current.PeriodThisYear,
                LocalizationManager.Current.PeriodThisMonth,
                LocalizationManager.Current.PeriodLast30Days,
                LocalizationManager.Current.PeriodLast7Days
            },
            SelectedIndex = (int)_dashboardStatsPeriod
        };
        periodBox.SelectionChanged += DashboardStatsPeriod_OnSelectionChanged;
        return DashboardWrapOverviewCard(LocalizationManager.Current.ListeningStats, content, periodBox);
    }

    private Control DashboardBuildListeningChart(IReadOnlyList<double> listeningTrend)
    {
        var values = listeningTrend.Count > 1
            ? listeningTrend.ToArray()
            : new double[] { 0, 0, 0, 0, 0, 0, 0 };
        var peakMinutes = Math.Max(1, values.Max() / 60);
        var maxMinutes = ListeningTrendGeometry.NiceAxisMaximum(peakMinutes);
        var maxSeconds = maxMinutes * 60;
        const double plotWidth = 300;
        const double plotHeight = 82;
        const double topPadding = 5;
        const double bottomY = 76;

        var outer = new Grid { Height = 124, Margin = new Thickness(0, 8, 0, 0) };
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(94) });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });

        var yLegend = new Grid { Margin = new Thickness(0, 0, 6, 0) };
        yLegend.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        yLegend.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        yLegend.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var yLabels = new[] { maxMinutes, maxMinutes / 2, 0d };
        for (var i = 0; i < yLabels.Length; i++)
        {
            var label = new TextBlock
            {
                Text = yLabels[i].ToString("0", CultureInfo.CurrentCulture),
                FontSize = 9,
                Foreground = FindResource<IBrush>("AppMutedTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center
            };
            Grid.SetRow(label, i);
            yLegend.Children.Add(label);
        }
        outer.Children.Add(yLegend);

        var plot = new Grid { ClipToBounds = true };
        for (var i = 0; i < 3; i++)
            plot.Children.Add(new Border
            {
                Height = 1,
                Background = FindResource<IBrush>("AppGridLineBrush"),
                Opacity = 0.65,
                VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 2 ? VerticalAlignment.Bottom : VerticalAlignment.Center
            });

        var points = values.Select((value, index) => new Point(
            index * plotWidth / Math.Max(1, values.Length - 1),
            bottomY - value / maxSeconds * (bottomY - topPadding))).ToList();
        var smoothLine = ListeningTrendGeometry.BuildSmoothCurve(points);
        // Anchor the geometry at the logical chart origin. Without this zero-length
        // subpath Avalonia's Stretch.Fill normalizes the curve's own highest point
        // to the top edge, which visually changes the Y scale whenever no value
        // reaches the configured axis maximum.
        var areaData = $"M 0,0 L 0,0 {smoothLine} L {plotWidth.ToString(CultureInfo.InvariantCulture)},{plotHeight.ToString(CultureInfo.InvariantCulture)} L 0,{plotHeight.ToString(CultureInfo.InvariantCulture)} Z";
        plot.Children.Add(new AvaloniaPath
        {
            Data = Geometry.Parse(areaData),
            Stretch = Stretch.Fill,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x88, 0x20, 0xD9, 0xE8), 0),
                    new GradientStop(Color.FromArgb(0x3D, 0x20, 0xD9, 0xE8), 0.55),
                    new GradientStop(Color.FromArgb(0x05, 0x20, 0xD9, 0xE8), 1)
                }
            },
            Stroke = FindResource<IBrush>("AppAccentBrush"),
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        });

        var hoverZones = new Grid { Background = Brushes.Transparent };
        var pointDates = DashboardListeningDates(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            hoverZones.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var hitZone = new Border { Background = Brushes.Transparent };
            ToolTip.SetTip(
                hitZone,
                $"{pointDates[index].ToString("d", CultureInfo.CurrentCulture)} · " +
                $"{values[index] / 60:N0} {LocalizationManager.Current.DashboardMinutesShort}");
            Grid.SetColumn(hitZone, index);
            hoverZones.Children.Add(hitZone);
        }
        plot.Children.Add(hoverZones);
        Grid.SetColumn(plot, 1);
        outer.Children.Add(plot);

        var unit = new TextBlock
        {
            Text = LocalizationManager.Current.DashboardMinutesShort,
            FontSize = 9,
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 6, 0)
        };
        Grid.SetRow(unit, 1);
        outer.Children.Add(unit);

        var xLegend = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        var xLabels = DashboardListeningLegendLabels(Math.Min(7, values.Length));
        for (var i = 0; i < xLabels.Count; i++)
        {
            xLegend.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock
            {
                Text = xLabels[i],
                FontSize = 9,
                Foreground = FindResource<IBrush>("AppMutedTextBrush"),
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == xLabels.Count - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center
            };
            Grid.SetColumn(label, i);
            xLegend.Children.Add(label);
        }
        Grid.SetColumn(xLegend, 1);
        Grid.SetRow(xLegend, 1);
        outer.Children.Add(xLegend);
        return outer;
    }

    private static int DashboardListeningBucketCount(StatsPeriod period) => period switch
    {
        StatsPeriod.Last7Days => 7,
        StatsPeriod.Last30Days => 30,
        StatsPeriod.ThisMonth => Math.Max(2, DateTime.Now.Day),
        StatsPeriod.ThisYear => 12,
        _ => 12
    };

    private IReadOnlyList<string> DashboardListeningLegendLabels(int count)
        => DashboardListeningDates(count)
            .Select(date => _dashboardStatsPeriod == StatsPeriod.ThisYear
                ? date.ToString("MMM", CultureInfo.CurrentCulture)
                : date.ToString("dd.MM", CultureInfo.CurrentCulture))
            .ToList();

    private IReadOnlyList<DateTime> DashboardListeningDates(int count)
    {
        var now = DateTime.Now.Date;
        DateTime start = _dashboardStatsPeriod switch
        {
            StatsPeriod.ThisYear => new DateTime(now.Year, 1, 1),
            StatsPeriod.ThisMonth => new DateTime(now.Year, now.Month, 1),
            StatsPeriod.Last7Days => now.AddDays(-7),
            StatsPeriod.Last30Days => now.AddDays(-30),
            _ => now.AddMonths(-Math.Max(1, count - 1))
        };
        return Enumerable.Range(0, count)
            .Select(i => start.AddTicks((now - start).Ticks * i / Math.Max(1, count - 1)))
            .ToList();
    }

    private Border DashboardBuildCompactCalendarCard(List<CalendarDayData> calendarData)
    {
        var content = DashboardBuildCalendarCard(calendarData);
        var title = string.Format(
            LocalizationManager.Current.Calendar,
            new DateTime(_dashboardYear, _dashboardMonth, 1).ToString("MMMM yyyy"));
        var card = DashboardWrapOverviewCard(title, content);
        if (card.Child is Grid layout && layout.Children.FirstOrDefault() is Grid header)
        {
            var nav = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var prev = CreateCalNavButton("‹");
            var next = CreateCalNavButton("›");
            prev.Click += CalendarPrev_OnClick;
            next.Click += CalendarNext_OnClick;
            nav.Children.Add(prev);
            nav.Children.Add(next);
            Grid.SetColumn(nav, 1);
            header.Children.Add(nav);
        }
        return card;
    }

    private Border DashboardBuildQuickAccessCard(DashboardLibrarySummary summary)
    {
        var rows = new StackPanel { Spacing = 7, HorizontalAlignment = HorizontalAlignment.Stretch };
        rows.Children.Add(DashboardQuickAccessButton("♡", LocalizationManager.Current.Favorites, async () =>
        {
            ClearTrackFacetFilters();
            _trackFavoritesOnly = true;
            await DashboardNavigateAsync("Tracks");
        }, "#FF5D7A", $"{summary.FavoriteCount:N0} {LocalizationManager.Current.Tracks}"));
        rows.Children.Add(DashboardQuickAccessButton("≡", LocalizationManager.Current.UpNext,
            () => DashboardNavigateAsync("Queue"), "#F59E42", $"{_queue.Count:N0} {LocalizationManager.Current.Tracks}"));
        rows.Children.Add(DashboardQuickAccessButton("◷", LocalizationManager.Current.RecentlyPlayed,
            ShowAllRecentlyPlayedAsync, "#43D7C8", LocalizationManager.Current.ShowAll));
        rows.Children.Add(DashboardQuickAccessButton("⤨", LocalizationManager.Current.DashboardRandomPlayback,
            DashboardPlayRandomAsync, "#20D9E8", LocalizationManager.Current.LocalLibrary));
        return DashboardWrapOverviewCard(LocalizationManager.Current.DashboardQuickAccess, rows);
    }

    private Button DashboardQuickAccessButton(string icon, string label, Func<Task> action, string accent, string subtitle)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.Children.Add(new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromArgb(
                0x28, Color.Parse(accent).R, Color.Parse(accent).G, Color.Parse(accent).B)),
            Child = new TextBlock
            {
                Text = icon,
                Foreground = new SolidColorBrush(Color.Parse(accent)),
                FontSize = ResolveFontSize("FontSizeBodyStrong"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        var textStack = new StackPanel { Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = ResolveFontSize("FontSizeCaption"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        textStack.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = ResolveFontSize("FontSizeMeta"),
            Foreground = FindResource<IBrush>("AppMutedTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(textStack, 1);
        content.Children.Add(textStack);
        var arrow = new TextBlock
        {
            Text = "›",
            FontSize = ResolveFontSize("FontSizeSubtitle"),
            Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(arrow, 2);
        content.Children.Add(arrow);
        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(7, 5),
            Background = FindResource<IBrush>("AppSurfaceHoverBrush"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(9),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        button.Click += async (_, e) => { e.Handled = true; await action(); };
        return button;
    }

    private Border DashboardWrapOverviewCard(string title, Control content, Control? headerAction = null)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = ResolveFontSize("FontSizeBodyStrong"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (headerAction is not null)
        {
            Grid.SetColumn(headerAction, 1);
            header.Children.Add(headerAction);
        }
        layout.Children.Add(header);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        return new Border
        {
            MinHeight = 250,
            Padding = new Thickness(14),
            Background = FindResource<IBrush>("AppSurfaceBrush"),
            BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Child = layout
        };
    }

    /// <summary>Builds the statistics section header carrying the shared period selector.</summary>
    /// <returns>The header control with an accent underline and a right-aligned period dropdown.</returns>
    private Control DashboardCreateStatsPeriodHeader()
    {
        var container = new StackPanel();

        var grid = new Grid { Margin = new Thickness(0, 24, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tb = new TextBlock
        {
            Text = LocalizationManager.Current.ListeningStats,
            FontSize = ResolveFontSize("FontSizeSubtitle"),
            FontWeight = FontWeight.SemiBold,
            Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 0);
        grid.Children.Add(tb);

        var periodBox = new ComboBox
        {
            MinWidth = 170,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[]
            {
                LocalizationManager.Current.PeriodAllTime,
                LocalizationManager.Current.PeriodThisYear,
                LocalizationManager.Current.PeriodThisMonth,
                LocalizationManager.Current.PeriodLast30Days,
                LocalizationManager.Current.PeriodLast7Days
            },
            SelectedIndex = (int)_dashboardStatsPeriod
        };
        periodBox.SelectionChanged += DashboardStatsPeriod_OnSelectionChanged;
        Grid.SetColumn(periodBox, 1);
        grid.Children.Add(periodBox);

        container.Children.Add(grid);
        container.Children.Add(new Border
        {
            Height = 3,
            Width = 34,
            Background = FindResource<IBrush>("AppAccentBrush"),
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 14)
        });
        return container;
    }

    /// <summary>Rebuilds the dashboard when the statistics period changes.</summary>
    /// <param name="sender">The period selector.</param>
    /// <param name="e">The selection change event data.</param>
    private async void DashboardStatsPeriod_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedIndex: >= 0 and var index })
            return;
        var period = (StatsPeriod)index;
        if (period == _dashboardStatsPeriod)
            return;
        _dashboardStatsPeriod = period;
        await BuildDashboardAsync();
    }

    /// <summary>Returns the inclusive Unix-second lower bound for a statistics period.</summary>
    /// <param name="period">The selected statistics period.</param>
    /// <returns>The lower bound, or <see langword="null"/> for all-time.</returns>
    private static long? StatsPeriodSinceUnix(StatsPeriod period)
    {
        var now = DateTime.Now;
        return period switch
        {
            StatsPeriod.ThisYear => new DateTimeOffset(new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Local))
                .ToUnixTimeSeconds(),
            StatsPeriod.ThisMonth => new DateTimeOffset(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Local))
                .ToUnixTimeSeconds(),
            StatsPeriod.Last30Days => new DateTimeOffset(now.Date.AddDays(-30), TimeZoneInfo.Local.GetUtcOffset(now.Date.AddDays(-30)))
                .ToUnixTimeSeconds(),
            StatsPeriod.Last7Days => new DateTimeOffset(now.Date.AddDays(-7), TimeZoneInfo.Local.GetUtcOffset(now.Date.AddDays(-7)))
                .ToUnixTimeSeconds(),
            _ => null
        };
    }

    /// <summary>Builds the modern top-genres analytics card for the dashboard.</summary>
    /// <param name="genres">Ranked genres with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopGenresCard(List<(string Genre, double Seconds)> genres)
    {
        if (genres.Count == 0)
            return new TextBlock
            {
                Text       = LocalizationManager.Current.NoData,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                Margin     = new Thickness(0, 4, 0, 0)
            };

        double maxSecs = genres.Max(item => item.Seconds);
        var rows = new StackPanel { Spacing = 8 };

        for (int i = 0; i < genres.Count; i++)
        {
            var (genre, secs) = genres[i];
            var row = new Grid { MinHeight = 31 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(62) });
            var labelButton = new Button
            {
                Content = genre,
                FontSize = ResolveFontSize("FontSizeCaption"),
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = genre
            };
            labelButton.Click += DashboardGenreButton_OnClick;
            row.Children.Add(labelButton);

            var barHost = DashboardBuildStatBar(
                secs, maxSecs, FindResource<IBrush>("AppAccentBrush") ?? Brushes.Cyan);
            barHost.Margin = new Thickness(8, 0);
            barHost.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(barHost, 1);
            row.Children.Add(barHost);

            var durationTb = new TextBlock
            {
                Text = Math.Round(secs / 60).ToString("N0", CultureInfo.CurrentCulture),
                FontSize = ResolveFontSize("FontSizeCaption"),
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(durationTb, 2);
            row.Children.Add(durationTb);
            rows.Children.Add(row);
        }
        return rows;
    }

    /// <summary>Builds the most-listened-albums analytics card for the dashboard.</summary>
    /// <param name="albums">Ranked albums with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopAlbumsCard(List<TopAlbumStat> albums)
    {
        if (albums.Count == 0)
            return DashboardNoDataText();

        double maxSecs = albums[0].Seconds;
        var rows = new StackPanel { Spacing = 10 };

        for (int i = 0; i < albums.Count; i++)
        {
            var album = albums[i];
            var row = new Grid { MinHeight = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            var cover = DashboardBuildTopAlbumCover(album);
            row.Children.Add(cover);

            var textStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            var titleButton = new Button
            {
                Content = album.Title,
                FontSize = ResolveFontSize("FontSizeBody"),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            titleButton.Click += async (_, e) => { e.Handled = true; await OpenTopAlbumAsync(album); };
            textStack.Children.Add(titleButton);

            if (!string.IsNullOrWhiteSpace(album.Artist))
            {
                var artistButton = new Button
                {
                    Content = album.Artist,
                    FontSize = ResolveFontSize("FontSizeMeta"),
                    Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Left
                };
                artistButton.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await OpenTopArtistAsync(new TopArtistStat(
                        album.Artist, album.Seconds, album.LocalArtistId, album.ExternalId, album.Path));
                };
                textStack.Children.Add(artistButton);
            }
            Grid.SetColumn(textStack, 1);
            row.Children.Add(textStack);

            var bar = DashboardBuildStatBar(
                album.Seconds, maxSecs, FindResource<IBrush>("AppAccentBrush") ?? Brushes.Cyan);
            bar.Margin = new Thickness(12, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var durationTb = new TextBlock
            {
                Text = Math.Round(album.Seconds / 60).ToString("N0", CultureInfo.CurrentCulture),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(durationTb, 3);
            row.Children.Add(durationTb);
            rows.Children.Add(row);
        }

        return rows;
    }

    /// <summary>Builds the most-listened-artists analytics card for the dashboard.</summary>
    /// <param name="artists">Ranked artists with total play seconds.</param>
    /// <returns>The analytics card control.</returns>
    private Control DashboardBuildTopArtistsCard(List<TopArtistStat> artists)
    {
        if (artists.Count == 0)
            return DashboardNoDataText();

        double maxSecs = artists[0].Seconds;
        var rows = new StackPanel { Spacing = 10 };

        for (int i = 0; i < artists.Count; i++)
        {
            var artist = artists[i];
            var color = _genreColors[i % _genreColors.Length];
            var row = new Grid { MinHeight = 40 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });

            var rankChip = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(0x42, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.CurrentCulture),
                    FontSize = ResolveFontSize("FontSizeMeta"),
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(color),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(rankChip, 0);
            row.Children.Add(rankChip);

            var labelButton = new Button
            {
                Content = artist.Name,
                FontSize = ResolveFontSize("FontSizeBody"),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush"),
                Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            labelButton.Click += async (_, e) => { e.Handled = true; await OpenTopArtistAsync(artist); };
            Grid.SetColumn(labelButton, 1);
            row.Children.Add(labelButton);

            var bar = DashboardBuildStatBar(
                artist.Seconds, maxSecs, FindResource<IBrush>("AppAccentBrush") ?? Brushes.Cyan);
            bar.Margin = new Thickness(12, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 2);
            row.Children.Add(bar);

            var durationTb = new TextBlock
            {
                Text = Math.Round(artist.Seconds / 60).ToString("N0", CultureInfo.CurrentCulture),
                FontSize = ResolveFontSize("FontSizeCaption"),
                FontWeight = FontWeight.Medium,
                Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(durationTb, 3);
            row.Children.Add(durationTb);
            rows.Children.Add(row);
        }

        return rows;
    }

    /// <summary>Builds a small album cover for a top-albums row: local thumbnail, remote artwork, or initials.</summary>
    /// <param name="album">The ranked album.</param>
    /// <returns>A fixed-size cover control.</returns>
    private Control DashboardBuildTopAlbumCover(TopAlbumStat album)
    {
        const double size = 40;
        var host = new Border
        {
            Width = size,
            Height = size,
            Background = FindResource<IBrush>("AppArtworkPlaceholderBrush"),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        var content = new Grid();
        content.Children.Add(new Orynivo.Controls.InitialsAvatar
        {
            DisplayName = string.IsNullOrWhiteSpace(album.Title) ? album.Artist : album.Title,
            FontSize = 14,
            IsHitTestVisible = false
        });

        if (!string.IsNullOrEmpty(album.ThumbPath))
        {
            var image = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            content.Children.Add(image);
            _ = LoadDashboardLocalArtworkAsync(image, album.ThumbPath!);
        }
        else if (TryGetTopAlbumRemoteArtUrl(album, out var artUrl))
        {
            var image = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
            content.Children.Add(image);
            _ = LoadDashboardRemoteArtworkAsync(image, artUrl);
        }

        host.Child = content;
        return host;
    }

    /// <summary>Resolves a remote Orynivo Server track-artwork URL for a top-albums row, when applicable.</summary>
    /// <param name="album">The ranked album.</param>
    /// <param name="artUrl">The resolved authenticated artwork URL.</param>
    /// <returns><see langword="true"/> when the album maps to a configured Orynivo Server track.</returns>
    private bool TryGetTopAlbumRemoteArtUrl(TopAlbumStat album, out string artUrl)
    {
        artUrl = string.Empty;
        var pseudo = MakeStatHistoryEntry(album.ExternalId, album.Path, album.Title, album.Artist, album.Title, null, null);
        if (TryGetOrynivoHistoryTarget(pseudo, out var server, out var trackId))
        {
            artUrl = OrynivoServerClient.GetTrackArtworkUrl(server, trackId, 96);
            return true;
        }
        return false;
    }

    /// <summary>Builds the thin proportional stat bar shared by the album and artist cards.</summary>
    /// <param name="seconds">This row's play seconds.</param>
    /// <param name="maxSeconds">The largest row's play seconds, used to scale the fill.</param>
    /// <param name="fill">The fill brush.</param>
    /// <returns>The bar control.</returns>
    private Control DashboardBuildStatBar(double seconds, double maxSeconds, IBrush fill)
    {
        double fraction = maxSeconds > 0 ? seconds / maxSeconds : 0;
        var barBg = new Border
        {
            Height = 8,
            Background = FindResource<IBrush>("AppGridLineBrush"),
            CornerRadius = new CornerRadius(4)
        };
        var barGrid = new Grid();
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
        barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
        var barFill = new Border { Height = 8, Background = fill, CornerRadius = new CornerRadius(4) };
        Grid.SetColumn(barFill, 0);
        barGrid.Children.Add(barFill);

        var barHost = new Grid();
        barHost.Children.Add(barBg);
        barHost.Children.Add(barGrid);
        return barHost;
    }

    /// <summary>Builds the bordered surface container shared by the analytics cards.</summary>
    /// <returns>An empty surface border ready to host its content.</returns>
    private Border DashboardSurfaceCard() => new()
    {
        Background = FindResource<IBrush>("AppSurfaceBrush"),
        BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(16, 14, 16, 8),
        Margin = new Thickness(0, 0, 0, 4)
    };

    /// <summary>Builds the muted "no data" placeholder shown when an analytics card is empty.</summary>
    /// <returns>A muted text block.</returns>
    private Control DashboardNoDataText(string? text = null) => new TextBlock
    {
        Text = text ?? LocalizationManager.Current.NoData,
        Foreground = FindResource<IBrush>("AppSecondaryTextBrush"),
        Margin = new Thickness(0, 4, 0, 0)
    };

    /// <summary>Opens a ranked album, staying in whichever library (local, remote, Plex) it belongs to.</summary>
    /// <param name="album">The ranked album to open.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private Task OpenTopAlbumAsync(TopAlbumStat album) =>
        OpenHistoryAlbumAsync(MakeStatHistoryEntry(
            album.ExternalId, album.Path, album.Title, album.Artist, album.Title,
            album.LocalAlbumId, album.LocalArtistId));

    /// <summary>Opens a ranked artist, staying in whichever library (local, remote, Plex) it belongs to.</summary>
    /// <param name="artist">The ranked artist to open.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>
    private Task OpenTopArtistAsync(TopArtistStat artist) =>
        artist.LocalArtistId.HasValue ||
        artist.ExternalId?.StartsWith("orynivo:", StringComparison.OrdinalIgnoreCase) == true
            ? ShowUnifiedArtistAlbumsAsync(artist.Name)
            : OpenHistoryArtistAsync(MakeStatHistoryEntry(
                artist.ExternalId, artist.Path, artist.Name, artist.Name, null, null, artist.LocalArtistId));

    /// <summary>Builds a synthetic playback-history entry used to route statistics-card clicks through the shared open logic.</summary>
    /// <param name="externalId">Representative external identifier.</param>
    /// <param name="path">Representative playback path.</param>
    /// <param name="title">Display title.</param>
    /// <param name="artist">Artist display name.</param>
    /// <param name="album">Album display title.</param>
    /// <param name="albumId">Local album identifier, or <see langword="null"/>.</param>
    /// <param name="artistId">Local artist identifier, or <see langword="null"/>.</param>
    /// <returns>A minimal <see cref="DailyHistoryEntry"/> carrying the identity fields.</returns>
    private static DailyHistoryEntry MakeStatHistoryEntry(
        string? externalId,
        string? path,
        string title,
        string? artist,
        string? album,
        long? albumId,
        long? artistId) =>
        new(0, null, path ?? string.Empty, DateTime.Now, 0, null, "track", title, artist, album, artistId, albumId, externalId);

    /// <summary>Chooses black or white text for readable contrast on a colored background.</summary>
    /// <param name="background">The background color.</param>
    /// <returns>A near-black or white brush, whichever contrasts better.</returns>
    private static IBrush PickContrastBrush(Color background)
    {
        // Relative luminance (sRGB approximation); bright backgrounds get dark text.
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance > 0.6
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
            : Brushes.White;
    }

    private async void DashboardGenreButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string genre } || string.IsNullOrWhiteSpace(genre))
            return;
        e.Handled = true;

        ClearTrackFacetFilters();
        _selectedTrackGenres.Add(genre);
        _expandedTrackFilterSections.Add(LocalizationManager.Current.Genre);
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = "Tracks";

        var tracksItem = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(item =>
                string.Equals(item.Tag as string, "Tracks", StringComparison.Ordinal));
        if (tracksItem is null)
            return;
        if (ReferenceEquals(NavListBox.SelectedItem, tracksItem))
            await ShowTopLevelViewAsync("Tracks");
        else
            NavListBox.SelectedItem = tracksItem;
    }

    private void ClearTrackFacetFilters()
    {
        _trackFavoritesOnly = false;
        _selectedTrackGenres.Clear();
        _selectedTrackFormats.Clear();
        _selectedTrackBitrates.Clear();
        _selectedTrackSources.Clear();
    }

    private async Task DashboardNavigateAsync(string tag)
    {
        PushCurrentNavigationState();
        ResetDrilldownState(clearNavigationHistory: false);
        _settings.LastMainView = tag;
        var item = NavListBox.Items
            .OfType<ListBoxItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag as string, tag, StringComparison.Ordinal));
        if (item is null || ReferenceEquals(NavListBox.SelectedItem, item))
            await ShowTopLevelViewAsync(tag);
        else
            NavListBox.SelectedItem = item;
    }

    private async void DashboardRandomPlayback_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await DashboardPlayRandomAsync();
    }

    private async Task DashboardPlayRandomAsync()
    {
        StopInfiniteMix();
        var path = await Task.Run(() =>
        {
            using var db = AudioDatabase.OpenDefault();
            var candidates = db.GetTracksLite()
                .Where(track => File.Exists(track.SourcePath))
                .ToList();
            return candidates.Count == 0
                ? null
                : candidates[Random.Shared.Next(candidates.Count)].Path;
        });
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusTextBlock.Text = LocalizationManager.Current.NoData;
            return;
        }

        _queue.Clear();
        _queue.Add(CreatePlaylistItem(path));
        _queueIndex = 0;
        ResetQueuePlaybackState();
        PersistPlaybackQueue();
        RefreshQueueNavigationButtons();
        try { await StartPlaybackAsync(path); }
        catch (OperationCanceledException) { StatusTextBlock.Text = LocalizationManager.Current.PlaybackStopped; }
        catch (Exception ex) { StopPlayback(); StatusTextBlock.Text = ex.Message; }
    }

    private static string FormatDashboardDuration(TimeSpan value) =>
        $"{(int)value.TotalHours:D2}:{value.Minutes:D2}:{value.Seconds:D2}";

    /// <summary>Opens the album track list for a local or Orynivo Server playback-history entry.</summary>
    /// <param name="entry">The history entry whose album should be opened.</param>
    /// <returns>A task representing the asynchronous navigation.</returns>

    private async void CalendarPrev_OnClick(object? sender, RoutedEventArgs e)
    {
        _dashboardMonth--;
        if (_dashboardMonth < 1) { _dashboardMonth = 12; _dashboardYear--; }
        await RefreshCalendarSectionAsync();
    }

    private async void CalendarNext_OnClick(object? sender, RoutedEventArgs e)
    {
        _dashboardMonth++;
        if (_dashboardMonth > 12) { _dashboardMonth = 1; _dashboardYear++; }
        await RefreshCalendarSectionAsync();
    }

    private async Task RefreshCalendarSectionAsync()
    {
        // Rebuild the whole dashboard — section header title contains the month/year
        await BuildDashboardAsync();
    }
}
