using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo;

/// <summary>
/// Presents Library Doctor duplicate groups for an explicit, user-confirmed
/// removal. Nothing is removed until the user selects files and confirms.
/// </summary>
public partial class DuplicateResolutionDialog : Window
{
    private readonly List<(string Path, CheckBox CheckBox)> _files = [];
    private readonly IReadOnlyList<LibraryDuplicateGroup> _groups;

    /// <summary>Initializes an empty designer instance.</summary>
    public DuplicateResolutionDialog()
        : this([])
    {
    }

    /// <summary>Initializes the dialog with the duplicate groups to review.</summary>
    /// <param name="groups">Duplicate groups returned by the Library Doctor.</param>
    public DuplicateResolutionDialog(IReadOnlyList<LibraryDuplicateGroup> groups)
    {
        InitializeComponent();
        _groups = groups;
        BuildGroups();
        UpdateSummary();
    }

    /// <summary>Gets the physical paths the user marked for removal.</summary>
    public IReadOnlyList<string> SelectedPaths =>
        _files.Where(file => file.CheckBox.IsChecked == true).Select(file => file.Path).ToList();

    /// <summary>Gets a value indicating whether the physical files should also be deleted.</summary>
    public bool DeleteFiles => DeleteFilesCheckBox.IsChecked == true;

    private void BuildGroups()
    {
        foreach (var group in _groups)
        {
            var panel = new StackPanel { Spacing = 4 };

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(
                    LocalizationManager.Current.DuplicateResolutionGroupHeader,
                    group.Kind == LibraryDuplicateKind.Exact
                        ? LocalizationManager.Current.DuplicateResolutionExact
                        : LocalizationManager.Current.DuplicateResolutionLikely,
                    group.Files.Count),
                FontWeight = FontWeight.SemiBold,
                Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
            });

            // The first file of each group is kept by default; the rest are proposed
            // for removal until the user changes the selection.
            for (var index = 0; index < group.Files.Count; index++)
            {
                var file = group.Files[index];
                var checkBox = new CheckBox
                {
                    Content = file.Path,
                    IsChecked = index > 0,
                    Tag = file.Path,
                    Foreground = FindResource<IBrush>("AppPrimaryTextBrush")
                };
                checkBox.IsCheckedChanged += (_, _) => UpdateSummary();
                _files.Add((file.Path, checkBox));
                panel.Children.Add(checkBox);
            }

            GroupsPanel.Children.Add(new Border
            {
                Background = FindResource<IBrush>("AppSurfaceBrush"),
                BorderBrush = FindResource<IBrush>("AppGridLineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Child = panel
            });
        }
    }

    private void UpdateSummary()
    {
        var selected = SelectedPaths.Count;
        RemoveButton.IsEnabled = selected > 0;
        SummaryTextBlock.Text = string.Format(
            LocalizationManager.Current.DuplicateResolutionSelected,
            selected);
    }

    private T? FindResource<T>(string key) where T : class =>
        Application.Current?.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var value) == true
            ? value as T
            : null;

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void RemoveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var selected = SelectedPaths;
        if (selected.Count == 0)
            return;

        var message = DeleteFiles
            ? string.Format(LocalizationManager.Current.DuplicateResolutionConfirmDeleteFiles, selected.Count)
            : string.Format(LocalizationManager.Current.DuplicateResolutionConfirmRemove, selected.Count);
        if (!await AppMessageBox.ConfirmAsync(
                message,
                LocalizationManager.Current.DuplicateResolutionTitle,
                this,
                LocalizationManager.Current.DuplicateResolutionRemove))
        {
            return;
        }

        Close(true);
    }
}
