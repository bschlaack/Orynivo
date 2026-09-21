using Orynivo.Visualization;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Verifies that user presets are discovered below a folder, including its subfolders, that they
/// are parsed only when they are shown, and that a broken file never costs the user the
/// visualizer.
/// </summary>
public sealed class VisualizerPresetLibraryTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "orynivo-presets-" + Guid.NewGuid().ToString("N"))).FullName;

    private static int BuiltInCount => VisualizerPresets.BuiltIn.Count;

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

    /// <summary>Presets in subfolders are found, because collections are sorted into folders.</summary>
    [Fact]
    public void Reload_DiscoversPresetsInSubfolders()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_directory, "pack", "warp"));
        File.WriteAllText(Path.Combine(nested.FullName, "deep.milk"), "name=Deep");
        File.WriteAllText(Path.Combine(_directory, "top.oryvis"), "name=Top");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Equal(BuiltInCount + 2, library.Count);
        Assert.Equal("Deep", library.At(BuiltInCount).Name);
        Assert.Equal("Top", library.At(BuiltInCount + 1).Name);
        Assert.Empty(library.RejectedFiles);
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

        Assert.Equal(BuiltInCount + 2, library.Count);
        Assert.Equal("First", library.At(BuiltInCount).Name);
        Assert.Equal("Second", library.At(BuiltInCount + 1).Name);
        Assert.Empty(library.RejectedFiles);
    }

    /// <summary>A preset is compiled on first use and then reused, not recompiled.</summary>
    [Fact]
    public void At_ParsesOnDemandAndCachesTheResult()
    {
        File.WriteAllText(Path.Combine(_directory, "mine.oryvis"), "name=Mine");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        Assert.Same(library.At(BuiltInCount), library.At(BuiltInCount));
    }

    /// <summary>
    /// A broken preset is only reported once it is shown, which is what keeps a large collection
    /// from being compiled when the window opens.
    /// </summary>
    [Fact]
    public void At_ReportsWhyAPresetWasSkipped()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.oryvis"), "name=Broken\nper_pixel_1=x = ;");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        Assert.Empty(library.RejectedReasons);
        var preset = library.At(BuiltInCount);

        Assert.Contains("broken.oryvis", library.RejectedFiles);
        var reason = Assert.Single(library.RejectedReasons);
        Assert.Contains("broken.oryvis", reason, StringComparison.Ordinal);
        Assert.Contains("position", reason, StringComparison.OrdinalIgnoreCase);
        // The window keeps working: a broken preset falls back to the first built-in.
        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, preset.Name);
        // A second visit does not report the same failure again.
        library.At(BuiltInCount);
        Assert.Single(library.RejectedReasons);
    }

    /// <summary>Preset files are added after the built-ins.</summary>
    [Fact]
    public void Reload_AddsUserPresetsAfterTheBuiltIns()
    {
        File.WriteAllText(Path.Combine(_directory, "mine.oryvis"), "name=Mine\nper_pixel_1=x = -x;");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Equal(BuiltInCount + 1, library.Count);
        Assert.Equal("Mine", library.At(BuiltInCount).Name);
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

        Assert.Equal(BuiltInCount + 2, library.Count);
        Assert.DoesNotContain("Ignored", new[] { library.At(BuiltInCount).Name, library.At(BuiltInCount + 1).Name });
    }

    /// <summary>A file with an invalid expression is skipped and reported.</summary>
    [Fact]
    public void Reload_SkipsAndReportsBrokenPresets()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.oryvis"), "name=Broken\nper_pixel_1=x = unknown(1);");
        File.WriteAllText(Path.Combine(_directory, "good.oryvis"), "name=Good");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);
        library.At(BuiltInCount);

        Assert.Equal(BuiltInCount + 2, library.Count);
        Assert.Contains("broken.oryvis", library.RejectedFiles);
    }

    /// <summary>A missing folder leaves the built-ins available.</summary>
    [Fact]
    public void Reload_HandlesAMissingFolder()
    {
        var library = new VisualizerPresetLibrary();

        library.Reload(Path.Combine(_directory, "does-not-exist"));

        Assert.Equal(BuiltInCount, library.Count);
    }

    /// <summary>Indexing wraps around the combined list.</summary>
    [Fact]
    public void At_WrapsAroundTheCombinedList()
    {
        File.WriteAllText(Path.Combine(_directory, "mine.oryvis"), "name=Mine");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        var count = library.Count;
        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, library.At(0).Name);
        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, library.At(count).Name);
        Assert.Equal("Mine", library.At(-1).Name);
    }
}
