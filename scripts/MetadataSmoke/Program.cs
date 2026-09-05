using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Orynivo;
using Orynivo.Library;
using Orynivo.Localization;
using System.Reflection;

// Synthetic UI-only fixtures: no real library, server, audio device, or external lookup.
Environment.SetEnvironmentVariable("ORYNIVO_DATA_DIR", Path.Combine(Path.GetTempPath(), "orynivo-metadata-ui-" + Guid.NewGuid().ToString("N")));
AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
var tracks = new[] {
    new MetadataRepairTrack(2, "two.flac", "two.flac", "Second title", "Artist", "Album", "Artist", 180, 2, 1),
    new MetadataRepairTrack(1, "one.flac", "one.flac", "First title", "Artist", "Album", "Artist", 180, 1, 1)
};
foreach (var language in Enum.GetValues<Language>())
{
  foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
  {
    ThemeManager.Apply(theme);
    LocalizationManager.Apply(language);
    var dialog = new MetadataRepairDialog(new("Fixture album", tracks, 1, 1, 0, 0, false), searchOnOpen: false);
    dialog.Show();
    Dispatcher.UIThread.RunJobs();
    var grid = dialog.FindControl<DataGrid>("CurrentTracksDataGrid")!;
    var labels = grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
    if (!labels.Contains("First title") || !labels.Contains("Second title"))
        throw new Exception("Current track bindings did not render titles.");
    var matches = new List<MetadataReleaseMatch> {
        new("release", "Proposal", "Artist", null, 2000, 1, 1,
            [new(1, "New first", "Artist", null, null), new(2, "New second", "Artist", null, null)], 1)
    };
    typeof(MetadataRepairDialog).GetField("_matches", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dialog, matches);
    var list = dialog.FindControl<ListBox>("ReleaseListBox")!;
    list.ItemsSource = new[] { "Proposal" };
    list.SelectedIndex = 0;
    Dispatcher.UIThread.RunJobs();
    var preview = dialog.FindControl<DataGrid>("PreviewDataGrid")!;
    labels = preview.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
    if (!labels.Any(t => t?.Contains("New first") == true))
        throw new Exception("Selected release preview did not render proposed titles.");
    matches.Add(matches[0] with { Title = "Other edition", Tracks = [new(1, "Other first", "Artist", null, null), new(2, "Other second", "Artist", null, null)] });
    list.ItemsSource = new[] { "Proposal", "Other edition" };
    list.SelectedIndex = 1;
    Dispatcher.UIThread.RunJobs();
    labels = preview.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
    if (!labels.Any(t => t?.Contains("Other first") == true) || labels.Any(t => t?.Contains("New first") == true))
        throw new Exception("Switching releases retained previous track data.");
    if (language == Language.German)
    {
        Directory.CreateDirectory("out");
        using var frame = dialog.CaptureRenderedFrame();
        frame?.Save($"out/metadata-review-{theme}.png");
    }
    list.SelectedIndex = -1;
    if (preview.ItemsSource is not null || dialog.FindControl<Button>("ApplyButton")!.IsEnabled)
        throw new Exception("Clearing selection retained a stale proposal.");
    dialog.Width = 760;
    dialog.Height = 560;
    Dispatcher.UIThread.RunJobs();
    if (grid.Bounds.Height <= 0 || preview.Bounds.Height <= 0)
        throw new Exception("Track tables collapse at the minimum dialog size.");
    dialog.Close();
  }
}
Console.WriteLine("Metadata dialog track bindings, proposal selection, and clearing passed in all four languages.");

LocalizationManager.Apply(Language.German);
var settingsType = typeof(App).Assembly.GetType("Orynivo.SettingsView")!;
var settings = (Control)Activator.CreateInstance(settingsType)!;
settingsType.GetField("_metadataAnalysisLoaded", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(settings, true);
settingsType.GetMethod("InitializeMetadataFilters", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, null);
settingsType.GetMethod("NavigateToSection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, ["Metadata"]);
var analysis = (Task)settingsType.GetMethod("LoadMetadataProblemsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, null)!;
var deadline = DateTime.UtcNow.AddSeconds(15);
while (!analysis.IsCompleted && DateTime.UtcNow < deadline)
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(5);
}
if (!analysis.IsCompleted)
    throw new Exception("Quick review did not finish for an empty fixture library.");
analysis.GetAwaiter().GetResult();
if (!settings.FindControl<Button>("RefreshMetadataAnalysisButton")!.IsEnabled ||
    settings.FindControl<StackPanel>("MetadataProgressPanel")!.IsVisible)
    throw new Exception("Analysis did not restore the action/progress state.");
foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
{
    ThemeManager.Apply(theme);
    var window = new Window { Width = 1100, Height = 900, Content = settings };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame();
    frame?.Save($"out/metadata-settings-{theme}.png");
    window.Content = null;
    window.Close();
}
settingsType.GetMethod("Deactivate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(settings, null);
