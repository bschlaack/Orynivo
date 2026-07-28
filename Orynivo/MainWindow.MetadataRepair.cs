using Avalonia.Controls;
using Avalonia.Interactivity;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

public partial class MainWindow
{
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
