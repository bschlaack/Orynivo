using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Orynivo;

// Explicit opt-in public lookup, isolated from the user's database/settings.
if (!args.Contains("--live"))
{
    Console.WriteLine("Pass --live to measure the real cover dialog against public artwork services.");
    return;
}
Environment.SetEnvironmentVariable("ORYNIVO_DATA_DIR",
    Path.Combine(Path.GetTempPath(), "orynivo-cover-ui-" + Guid.NewGuid().ToString("N")));
AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().SetupWithoutStarting();
var dialog = new CoverSearchWindow("Lovers Rock", "Sade");
var watch = Stopwatch.StartNew();
dialog.Show();
var list = dialog.FindControl<ListBox>("ResultsListBox")!;
var status = dialog.FindControl<TextBlock>("StatusTextBlock")!;
var field = typeof(CoverSearchWindow).GetField("_searchCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!;
var search = typeof(CoverSearchWindow).GetMethod("SearchAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
long first = -1;
var loaded = false;
while (watch.Elapsed < TimeSpan.FromSeconds(45))
{
    Dispatcher.UIThread.RunJobs();
    if (field.GetValue(dialog) is { } operation && !loaded)
    {
        loaded = true;
        search.Invoke(dialog, null);
        if (!ReferenceEquals(operation, field.GetValue(dialog)))
            throw new Exception("Duplicate search replaced the active operation.");
    }
    if (list.ItemCount > 0 && first < 0)
    {
        first = watch.ElapsedMilliseconds;
        Console.WriteLine($"first_display_ms={first}");
    }
    if (loaded && field.GetValue(dialog) is null) break;
    Thread.Sleep(10);
}
Console.WriteLine($"complete_display_ms={watch.ElapsedMilliseconds} count={list.ItemCount} status_empty={string.IsNullOrEmpty(status.Text)}");
if (first < 0 || field.GetValue(dialog) is not null)
    throw new Exception("Live provider did not produce a completed result within the budget.");
// Restart a completed query, then supersede it and close during the next request.
search.Invoke(dialog, null);
var oldOperation = (CancellationTokenSource)field.GetValue(dialog)!;
dialog.FindControl<TextBox>("QueryTextBox")!.Text = "Love Deluxe";
search.Invoke(dialog, null);
if (!oldOperation.IsCancellationRequested)
    throw new Exception("Superseded search was not cancelled.");
var currentOperation = (CancellationTokenSource)field.GetValue(dialog)!;
dialog.Close();
if (!currentOperation.IsCancellationRequested)
    throw new Exception("Closing did not cancel the active search.");
var drain = Stopwatch.StartNew();
while (field.GetValue(dialog) is not null && drain.Elapsed < TimeSpan.FromSeconds(3))
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(10);
}
Dispatcher.UIThread.RunJobs();
if (list.ItemCount != 0) throw new Exception("Closing retained preview resources.");
if (field.GetValue(dialog) is not null) throw new Exception("Closed search did not terminate.");
Console.WriteLine("PASS: actual Avalonia dialog, background decode, incremental binding, duplicate-start guard, supersession and close cancellation/cleanup");
