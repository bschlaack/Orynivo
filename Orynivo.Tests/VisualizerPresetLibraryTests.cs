using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies that user presets are loaded from a folder and that a broken file never costs
/// the user the visualizer.
/// </summary>
public sealed class VisualizerPresetLibraryTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "orynivo-presets-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Every section of a multi-preset .milk file becomes its own preset.</summary>
    [Fact]
    public void Reload_LoadsEverySectionOfAMilkFile()
    {
        File.WriteAllText(
            Path.Combine(_directory, "two.milk"),
            "[preset00]\nname=First\nper_pixel_1=x = -x;\n[preset01]\nname=Second\nper_pixel_1=y = -y;\n");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        var names = library.Presets.Select(preset => preset.Name).ToList();
        Assert.Contains("First", names);
        Assert.Contains("Second", names);
        Assert.Empty(library.RejectedFiles);
    }

    /// <summary>A broken preset is skipped with the reason that made it fail.</summary>
    [Fact]
    public void Reload_ReportsWhyAPresetWasSkipped()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.oryvis"), "name=Broken\nper_pixel_1=x = ;");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Contains("broken.oryvis", library.RejectedFiles);
        var reason = Assert.Single(library.RejectedReasons);
        Assert.Contains("broken.oryvis", reason, StringComparison.Ordinal);
        Assert.Contains("position", reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Preset files are added after the built-ins.</summary>
    [Fact]
    public void Reload_AddsUserPresetsAfterTheBuiltIns()
    {
        File.WriteAllText(Path.Combine(_directory, "mine.oryvis"), "name=Mine\nper_pixel_1=x = -x;");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Equal(VisualizerPresets.BuiltIn.Count + 1, library.Presets.Count);
        Assert.Equal("Mine", library.Presets[^1].Name);
        Assert.Empty(library.RejectedFiles);
    }

    /// <summary>Both supported extensions are read, other files are ignored.</summary>
    [Fact]
    public void Reload_ReadsSupportedExtensionsOnly()
    {
        File.WriteAllText(Path.Combine(_directory, "one.oryvis"), "name=One");
        File.WriteAllText(Path.Combine(_directory, "two.milk"), "name=Two");
        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "name=Ignored");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Equal(VisualizerPresets.BuiltIn.Count + 2, library.Presets.Count);
        Assert.DoesNotContain(library.Presets, preset => preset.Name == "Ignored");
    }

    /// <summary>A file with an invalid expression is skipped and reported.</summary>
    [Fact]
    public void Reload_SkipsAndReportsBrokenPresets()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.oryvis"), "name=Broken\nper_pixel_1=x = unknown(1);");
        File.WriteAllText(Path.Combine(_directory, "good.oryvis"), "name=Good");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Equal(VisualizerPresets.BuiltIn.Count + 1, library.Presets.Count);
        Assert.Contains("broken.oryvis", library.RejectedFiles);
    }

    /// <summary>A missing folder leaves the built-ins available.</summary>
    [Fact]
    public void Reload_HandlesAMissingFolder()
    {
        var library = new VisualizerPresetLibrary();

        library.Reload(Path.Combine(_directory, "does-not-exist"));

        Assert.Equal(VisualizerPresets.BuiltIn.Count, library.Presets.Count);
    }

    /// <summary>Indexing wraps around the combined list.</summary>
    [Fact]
    public void At_WrapsAroundTheCombinedList()
    {
        File.WriteAllText(Path.Combine(_directory, "mine.oryvis"), "name=Mine");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        var count = library.Presets.Count;
        Assert.Equal(library.Presets[0].Name, library.At(count).Name);
        Assert.Equal(library.Presets[^1].Name, library.At(-1).Name);
    }
}
