using Avalonia.Controls;
using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class MainWindow
{
    /// <summary>
    /// Reviews Library Doctor duplicate groups and removes the files the user
    /// explicitly confirms, optionally deleting them from disk as well.
    /// </summary>
    /// <returns>A task representing the review and removal.</returns>
    internal async Task OpenDuplicateResolutionAsync()
    {
        var groups = await Task.Run(() =>
        {
            using var database = AudioDatabase.OpenDefault();
            return LibraryMetadataRepairService.FindDuplicateGroups(database.GetMetadataRepairTracks());
        });

        if (groups.Count == 0)
        {
            StatusTextBlock.Text = LocalizationManager.Current.DuplicateResolutionNoneFound;
            return;
        }

        var dialog = new DuplicateResolutionDialog(groups);
        if (await dialog.ShowDialog<bool>(this) != true)
            return;

        var selected = dialog.SelectedPaths;
        var deleteFiles = dialog.DeleteFiles;
        var removed = await Task.Run(() => LibraryScanner.RemoveTracksByPaths(selected, deleteFiles));
        InvalidateUnifiedLibraryViewCache();
        StatusTextBlock.Text = string.Format(
            LocalizationManager.Current.DuplicateResolutionRemoved,
            removed.Count);
    }

    private async Task OpenMetadataRepairAsync(MetadataFolderCandidate candidate)
    {
        var dialog = new MetadataRepairDialog(candidate);
        if (await dialog.ShowDialog<bool>(this) != true || dialog.SelectedMatch is null)
            return;

        var overrides = LibraryMetadataRepairService.CreateOverrides(candidate, dialog.SelectedMatch);
        await Task.Run(() =>
        {
            using var database = AudioDatabase.OpenDefault();
            database.ApplyTrackMetadataOverrides(overrides);
            TrackSearchIndex.Rebuild(database.GetAll());
        });
        StatusTextBlock.Text = LocalizationManager.Current.MetadataRepairSuccess;
    }

    private async Task OpenMetadataRepairForFolderAsync(string folderPath)
    {
        var candidate = await Task.Run(() =>
        {
            using var database = AudioDatabase.OpenDefault();
            return LibraryMetadataRepairService
                .Analyze(database.GetMetadataRepairTracks(), includeHealthy: true)
                .FirstOrDefault(item =>
                    string.Equals(item.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
        });
        if (candidate is not null)
            await OpenMetadataRepairAsync(candidate);
    }

    private MenuItem CreateIdentifyFolderMenuItem(string folderPath)
    {
        var item = CreateFlyoutMenuItem(LocalizationManager.Current.IdentifyFolderAsAlbum);
        item.Tag = folderPath;
        item.Click += IdentifyFolderMenuItem_OnClick;
        return item;
    }

    private async void IdentifyFolderMenuItem_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string folderPath })
            await OpenMetadataRepairForFolderAsync(folderPath);
    }
}
