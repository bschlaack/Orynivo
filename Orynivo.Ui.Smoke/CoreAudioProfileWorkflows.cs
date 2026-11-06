using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Orynivo.Audio;
using Orynivo.Localization;

namespace Orynivo.Ui.Smoke;

/// <summary>Checks the native profile dialog using synthetic devices and a simulated Mac policy only.</summary>
internal static class CoreAudioProfileWorkflows
{
    /// <summary>Checks localized layout, restored choices, bypass policy and actual Save routing.</summary>
    internal static void Run()
    {
        try
        {
            foreach (var language in Enum.GetValues<Language>())
            {
                LocalizationManager.Apply(language);
                var profile = new OutputProfile { Name = "Synthetic native output", Backend = OutputBackend.CoreAudio,
                    SelectedWasapiDeviceId = "fixture-second", SelectedWasapiDeviceName = "Synthetic device two",
                    CoreAudioMatchRate = false, CoreAudioExclusive = false, CoreAudioRestoreRate = false };
                var enumerations = 0;
                IReadOnlyList<WasapiDeviceInfo> Devices(OutputBackend backend)
                {
                    if (backend is not (OutputBackend.CoreAudio or OutputBackend.Wasapi))
                        throw new InvalidOperationException("Unexpected synthetic output backend.");
                    Interlocked.Increment(ref enumerations);
                    return [new("fixture-first", "Synthetic device one"), new("fixture-second", "Synthetic device two")];
                }
                var dialog = new OutputProfileDialog([], profile, true, Devices);
                try
                {
                    dialog.Show();
                    Finish((Task)typeof(OutputProfileDialog).GetMethod("LoadDevicesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(dialog, null)!);
                    var picker = dialog.FindControl<ComboBox>("BackendComboBox")!;
                    var labels = picker.Items.Cast<object>().Select(item => item.ToString()).ToArray();
                    Check(labels.Contains(LocalizationManager.Current.CoreAudio) && labels.Contains(LocalizationManager.Current.OpenAl),
                        "Mac output choice did not include CoreAudio and OpenAL.");
                    Check(dialog.FindControl<ComboBox>("DeviceComboBox")!.SelectedItem is WasapiDeviceInfo { Id: "fixture-second" },
                        "Native UID was not restored after enumeration.");
                    var panel = dialog.FindControl<StackPanel>("CoreAudioOptionsPanel")!;
                    Check(panel.IsVisible && panel.Bounds.Height > 0, "Native options were not laid out.");
                    var match = dialog.FindControl<CheckBox>("CoreAudioMatchRateCheckBox")!;
                    var exclusive = dialog.FindControl<CheckBox>("CoreAudioExclusiveCheckBox")!;
                    var restore = dialog.FindControl<CheckBox>("CoreAudioRestoreRateCheckBox")!;
                    var bypass = dialog.FindControl<CheckBox>("CoreAudioBypassCheckBox")!;
                    Check(match.IsChecked == false && exclusive.IsChecked == false && restore.IsChecked == false,
                        "Native profile options were not restored.");
                    foreach (var box in new[] { match, exclusive, restore, bypass })
                    {
                        Check(!string.IsNullOrEmpty(AutomationProperties.GetName(box)) && box.Focus(),
                            "Native option lost its accessible name or keyboard focus.");
                        Check(box.Content is TextBlock { TextWrapping: Avalonia.Media.TextWrapping.Wrap },
                            "Native option caption cannot wrap.");
                    }
                    foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
                    foreach (var width in new[] { 440d, 560d })
                    {
                        ThemeManager.Apply(theme);
                        dialog.Width = width;
                        Dispatcher.UIThread.RunJobs();
                        foreach (var box in new[] { match, exclusive, restore, bypass })
                        {
                            var caption = (TextBlock)box.Content!;
                            Check(caption.Bounds.Width > 0 && caption.Bounds.Width <= box.Bounds.Width &&
                                caption.Bounds.Height + 1 >= caption.DesiredSize.Height,
                                "Native option caption was clipped at a supported width.");
                        }
                        ExportFrame(dialog, $"coreaudio-{theme}-{language}-{(int)width}");
                    }
                    bypass.IsChecked = true;
                    Check(match.IsChecked == true && exclusive.IsChecked == true && !match.IsEnabled && !exclusive.IsEnabled,
                        "Bypass did not enforce matching and exclusive access.");
                    restore.IsChecked = true;
                    dialog.FindControl<Button>("SaveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(dialog.Result is { Backend: OutputBackend.CoreAudio, SelectedWasapiDeviceId: "fixture-second",
                        CoreAudioMatchRate: true, CoreAudioExclusive: true, CoreAudioRestoreRate: true, CoreAudioBypass: true },
                        "Native Save lost the selected UID or options.");
                    Check(enumerations > 0, "Synthetic device boundary was not exercised.");
                }
                finally { dialog.Close(); Dispatcher.UIThread.RunJobs(); }
            }
        }
        finally
        {
            LocalizationManager.Apply(Language.English);
            ThemeManager.Apply(AppTheme.Dark);
        }
        Console.WriteLine("PASS: CoreAudio/OpenAL profile choices, UID/options Save and seven-language accessible layout; synthetic Mac policy, no devices.");
    }

    private static void ExportFrame(Window dialog, string name)
    {
        var directory = Environment.GetEnvironmentVariable("ORYNIVO_SMOKE_RENDER_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        using var bitmap = dialog.CaptureRenderedFrame();
        Check(bitmap is not null, "Native profile frame was not rendered.");
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        bitmap!.Save(file, PngBitmapEncoderOptions.Default);
    }

    private static void Finish(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(5);
        }
        Check(task.IsCompleted, "Synthetic native profile load timed out.");
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
