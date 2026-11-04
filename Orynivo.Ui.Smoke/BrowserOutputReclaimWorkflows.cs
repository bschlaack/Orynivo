using System.Reflection;
using Avalonia.Controls;
using Orynivo.Library;
using Orynivo.Localization;

namespace Orynivo.Ui.Smoke;

/// <summary>Checks remote-output selection and detached resume decisions without starting playback or any device.</summary>
internal static class BrowserOutputReclaimWorkflows
{
    /// <summary>Uses the production picker and resume decision with an unshown window and synthetic source state.</summary>
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var window = new MainWindow();
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        var settings = (AppSettings)typeof(MainWindow).GetField("_settings", flags)!.GetValue(window)!;
        var originalProfiles = settings.OutputProfiles!.ToArray();
        var savedName = settings.SelectedOutputProfileName;
        Set("_browserAudioClientId", Guid.NewGuid().ToString("N"));
        Call("RefreshOutputPickerSelection");
        var picker = window.FindControl<ComboBox>("OutputPickerComboBox")!;
        if (picker.SelectedItem is not OutputProfile selected || selected.Name != LocalizationManager.Current.BrowserAudioOutput ||
            originalProfiles.Contains(selected) || !settings.OutputProfiles.SequenceEqual(originalProfiles) || settings.SelectedOutputProfileName != savedName)
            throw new InvalidOperationException("Remote output was not presented as a transient selected device.");
        var radio = new RadioStationRecord(1, "fixture", "Synthetic station", "https://example.invalid/audio", null, null, "DE", "MP3", 128, null);
        var type = typeof(MainWindow).GetNestedType("BrowserOutputResume", BindingFlags.NonPublic)!;
        var source = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, args: ["synthetic-source", radio, null, TimeSpan.FromSeconds(37), false], culture: null)!;
        Set("_browserOutputResume", source);
        var resume = Call("GetOutputResumeForSelection", true, true)!;
        if (resume is null || (TimeSpan)type.GetProperty("Position")!.GetValue(resume)! != TimeSpan.FromSeconds(37) ||
            !ReferenceEquals(type.GetProperty("Radio")!.GetValue(resume), radio) || (bool)type.GetProperty("WasPaused")!.GetValue(resume)!)
            throw new InvalidOperationException("Closed browser lost its source/position or failed to resume local playback.");
        if (Call("GetOutputResumeForSelection", false, true) is not null)
            throw new InvalidOperationException("Unrelated native selection revived browser context.");
        // Idle Stop is permitted here: there is no attached player or preparation.
        Call("StopPlayback");
        if (Call("GetOutputResumeForSelection", true, true) is not null)
            throw new InvalidOperationException("Explicit Stop retained a browser resume offer.");
        Set("_browserAudioClientId", null);
        Call("RefreshOutputPickerSelection");
        if (picker.SelectedItem is not OutputProfile native || !originalProfiles.Contains(native))
            throw new InvalidOperationException("Native output could not be selected after remote mode.");
        Console.WriteLine("PASS: transient remote device picker, detached source/position resume and idle Stop isolation; no playback or devices.");
    }
}
