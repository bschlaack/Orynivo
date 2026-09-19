using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;
using Orynivo.Streaming;

namespace Orynivo;

/// <summary>
/// Multi-selection bulk editing for the shared track tables. Local tracks are
/// updated through the transactional database bulk methods; remote Orynivo
/// Server tracks use the profile-scoped favorite container and the per-track
/// rating endpoint. Authenticated playback URLs are never persisted here.
/// </summary>
public partial class MainWindow
{
    /// <summary>Suppresses the rating selector while it is rebuilt programmatically.</summary>
    private bool _suppressBulkRatingChange;

    /// <summary>Refreshes the bulk action bar whenever the content table selection changes.</summary>
    /// <param name="sender">The content table.</param>
    /// <param name="e">Selection change details.</param>
    private void ContentDataGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateBulkEditBar();

    /// <summary>Applies the favorite state to every selected track.</summary>
    /// <param name="sender">The bulk favorite action.</param>
    /// <param name="e">Click details.</param>
    private void BulkFavoriteButton_OnClick(object? sender, RoutedEventArgs e)
        => _ = ApplyBulkFavoriteAsync(true);

    /// <summary>Clears the favorite state of every selected track.</summary>
    /// <param name="sender">The bulk unfavorite action.</param>
    /// <param name="e">Click details.</param>
    private void BulkUnfavoriteButton_OnClick(object? sender, RoutedEventArgs e)
        => _ = ApplyBulkFavoriteAsync(false);

    /// <summary>Applies the chosen personal rating to every selected track.</summary>
    /// <param name="sender">The rating selector.</param>
    /// <param name="e">Selection change details.</param>
    private void BulkRatingComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressBulkRatingChange)
            return;

        var rating = BulkRatingComboBox.SelectedIndex;
        if (rating < 0)
            return;

        _ = ApplyBulkRatingAsync(rating);
    }

    /// <summary>Recomputes bulk action bar visibility and its selection summary.</summary>
    private void UpdateBulkEditBar()
    {
        var rows = GetSelectedTrackRows();
        var visible = rows.Count > 1 && IsBulkTrackViewActive();
        BulkEditBar.IsVisible = visible;
        if (!visible)
            return;

        BulkSelectionTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Current.BulkSelectedCount,
            rows.Count);
        PopulateBulkRatingItems();
    }

    /// <summary>Indicates whether the active content view is a shared track table.</summary>
    /// <returns><see langword="true"/> for the local or a remote Tracks view.</returns>
    private bool IsBulkTrackViewActive()
        => _currentTopLevelTag == "Tracks"
           || (_currentTopLevelTag?.StartsWith("OrynivoServer:", StringComparison.Ordinal) == true
               && _activeOrynivoView == "Tracks");

    /// <summary>Returns the currently selected track rows of the content table.</summary>
    /// <returns>Selected rows whose entity type is a local or remote track.</returns>
    private List<ContentRow> GetSelectedTrackRows()
        => ContentDataGrid.SelectedItems
            .OfType<ContentRow>()
            .Where(row => row.EntityType is "Track" or "OrynivoTrack" && row.Id is long)
            .ToList();

    /// <summary>Rebuilds the rating selector so it always uses the active language.</summary>
    private void PopulateBulkRatingItems()
    {
        _suppressBulkRatingChange = true;
        try
        {
            BulkRatingComboBox.Items.Clear();
            BulkRatingComboBox.Items.Add(LocalizationManager.Current.BulkRatingNone);
            for (var stars = 1; stars <= 5; stars++)
                BulkRatingComboBox.Items.Add(new string('★', stars));
            BulkRatingComboBox.SelectedIndex = -1;
        }
        finally
        {
            _suppressBulkRatingChange = false;
        }
    }

    /// <summary>Applies a favorite state to every selected local and remote track.</summary>
    /// <param name="favorite">Requested favorite state.</param>
    /// <returns>A task representing local persistence and remote mirroring.</returns>
    private async Task ApplyBulkFavoriteAsync(bool favorite)
    {
        var rows = GetSelectedTrackRows();
        if (rows.Count == 0)
            return;

        var localIds = rows
            .Where(row => row.OrynivoServer is null)
            .Select(row => row.Id!.Value)
            .ToList();
        var remoteRows = rows
            .Where(row => row.OrynivoServer is not null)
            .ToList();

        if (localIds.Count > 0)
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.SetTrackFavorites(localIds, favorite);
            });
        }

        if (remoteRows.Count > 0)
        {
            foreach (var row in remoteRows)
            {
                var key = GetOrynivoFavoriteKey(row.OrynivoServer!.Id, "Track", row.Id!.Value);
                if (favorite)
                    ActiveUserProfile.OrynivoServerFavorites.Add(key);
                else
                    ActiveUserProfile.OrynivoServerFavorites.Remove(key);
            }

            await Task.Run(() => _settingsStore.Save(_settings));
        }

        InvalidateUnifiedLibraryViewCache();
        foreach (var row in rows)
            row.IsFavorite = favorite;

        foreach (var row in remoteRows)
            RefreshOrynivoFavoriteRows(row.OrynivoServer!, row.Id!.Value, favorite);

        StatusTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Current.BulkFavoriteUpdated,
            rows.Count);
    }

    /// <summary>Applies a personal rating to every selected local and remote track.</summary>
    /// <param name="rating">New zero-to-five-star rating.</param>
    /// <returns>A task representing local persistence and remote updates.</returns>
    private async Task ApplyBulkRatingAsync(int rating)
    {
        var rows = GetSelectedTrackRows();
        if (rows.Count == 0)
            return;

        var localIds = rows
            .Where(row => row.OrynivoServer is null)
            .Select(row => row.Id!.Value)
            .ToList();
        var remoteRows = rows
            .Where(row => row.OrynivoServer is not null)
            .ToList();

        if (localIds.Count > 0)
        {
            await Task.Run(() =>
            {
                using var db = AudioDatabase.OpenDefault();
                db.SetTrackUserRatings(localIds, rating);
            });
        }

        var failed = 0;
        foreach (var row in remoteRows)
        {
            var saved = await _orynivoClient.UpdateTrackRatingAsync(
                row.OrynivoServer!,
                row.Id!.Value,
                new OrynivoTrackRatingUpdate(UserRating: rating));
            if (!saved)
                failed++;
        }

        foreach (var row in rows)
            row.UserRating = rating;

        StatusTextBlock.Text = failed == 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Current.BulkRatingUpdated,
                rows.Count)
            : string.Format(
                CultureInfo.CurrentCulture,
                LocalizationManager.Current.BulkRatingPartiallyFailed,
                failed);

        _suppressBulkRatingChange = true;
        try
        {
            BulkRatingComboBox.SelectedIndex = -1;
        }
        finally
        {
            _suppressBulkRatingChange = false;
        }
    }
}
