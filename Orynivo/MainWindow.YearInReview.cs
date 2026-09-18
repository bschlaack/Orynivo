using Orynivo.Library;

namespace Orynivo;

/// <summary>
/// Dashboard entry point for the shareable year-in-review summary. Every value
/// comes from the existing playback history; no additional data is collected.
/// </summary>
public partial class MainWindow
{
    /// <summary>Opens the year-in-review summary for a year with listening history.</summary>
    /// <param name="sender">The dashboard action.</param>
    /// <param name="e">Click details.</param>
    private async void YearInReviewButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var currentYear = DateTime.Now.Year;
        IReadOnlyList<int> years;
        try
        {
            years = await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                return (IReadOnlyList<int>)db.GetListeningYears();
            }).ConfigureAwait(true);
        }
        catch
        {
            return;
        }

        if (years.Count == 0)
            years = [currentYear];

        var dialog = new YearInReviewDialog(years, currentYear, LoadYearInReviewAsync);
        await dialog.ShowDialog(this);
    }

    /// <summary>Loads one calendar-year summary off the UI thread.</summary>
    /// <param name="year">Four-digit calendar year.</param>
    /// <returns>The summary, or <see langword="null"/> when it cannot be loaded.</returns>
    private static Task<YearInReviewSummary?> LoadYearInReviewAsync(int year) =>
        Task.Run<YearInReviewSummary?>(() =>
        {
            try
            {
                using var db = AudioDatabase.OpenDefault();
                return db.GetYearInReview(year);
            }
            catch
            {
                return null;
            }
        });
}
