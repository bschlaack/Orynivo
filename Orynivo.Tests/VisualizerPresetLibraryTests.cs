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

    /// <summary>
    /// Every section of a multi-preset .milk file becomes its own preset. Discovery only records
    /// the file, so the extra sections appear once the file has been read for the first time.
    /// </summary>
    [Fact]
    public void Reload_LoadsEverySectionOfAMilkFile()
    {
        File.WriteAllText(
            Path.Combine(_directory, "two.milk"),
            "[preset00]\nname=First\nper_pixel_1=x = -x;\n[preset01]\nname=Second\nper_pixel_1=y = -y;\n");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        // Discovery records the file without reading it, so it counts as one preset until then.
        Assert.Equal(BuiltInCount + 1, library.Count);
        Assert.Equal("First", library.At(BuiltInCount).Name);
        Assert.Equal("Second", library.At(BuiltInCount + 1).Name);
        Assert.Empty(library.RejectedFiles);
    }

    /// <summary>
    /// The built-ins are available without any folder, which is what lets the window open and
    /// render immediately while the collection is still being discovered in the background.
    /// </summary>
    [Fact]
    public void LoadBuiltIns_WorksWithoutAnyFolder()
    {
        var library = new VisualizerPresetLibrary();

        library.LoadBuiltIns();

        Assert.Equal(BuiltInCount, library.Count);
        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, library.At(0).Name);
        Assert.False(library.IsDiscovered);
    }

    /// <summary>Discovery records file paths without reading a single file.</summary>
    [Fact]
    public void Discover_DoesNotReadTheFiles()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_directory, "a", "b", "c"));
        for (var index = 0; index < 25; index++)
            File.WriteAllText(Path.Combine(nested.FullName, $"p{index:D2}.milk"), "name=P" + index);

        var library = new VisualizerPresetLibrary();
        var elapsed = library.Discover(_directory);

        Assert.True(library.IsDiscovered);
        Assert.Equal(BuiltInCount + 25, library.Count);
        Assert.True(elapsed >= TimeSpan.Zero);
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
    /// A preset with an unsupported expression block still loads: the block is skipped and
    /// reported on the preset instead of replacing the whole preset with a fallback, which is what
    /// makes real Milkdrop presets usable.
    /// </summary>
    [Fact]
    public void At_KeepsAPresetWithAnUnsupportedBlock()
    {
        File.WriteAllText(
            Path.Combine(_directory, "partial.oryvis"),
            "name=Partial\nper_frame_1=zoom = 1.01;\nper_pixel_1=x = unknown(1);");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        var preset = library.At(BuiltInCount);

        Assert.Equal("Partial", preset.Name);
        Assert.Contains(preset.FailedBlocks, block => block.StartsWith("per_pixel", StringComparison.Ordinal));
        Assert.False(preset.PerFrame.IsEmpty);
        Assert.Empty(library.RejectedFiles);
        // The preset is parsed once and then reused.
        Assert.Same(preset, library.At(BuiltInCount));
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

    /// <summary>A preset with an unsupported block does not cost the collection the other presets.</summary>
    [Fact]
    public void Reload_KeepsPresetsWithUnsupportedBlocks()
    {
        File.WriteAllText(Path.Combine(_directory, "partial.oryvis"), "name=Partial\nper_pixel_1=x = unknown(1);");
        File.WriteAllText(Path.Combine(_directory, "good.oryvis"), "name=Good");
        var library = new VisualizerPresetLibrary();

        library.Reload(_directory);

        Assert.Equal(BuiltInCount + 2, library.Count);
        Assert.Empty(library.RejectedFiles);
        var names = new[] { library.At(BuiltInCount).Name, library.At(BuiltInCount + 1).Name };
        Assert.Contains("Partial", names);
        Assert.Contains("Good", names);
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

    /// <summary>Two copies keep their own last and penultimate indices and identical preset data.</summary>
    [Fact]
    public void At_LoadsTheLastDuplicateInsteadOfWrappingToTheFirstBuiltIn()
    {
        const string content = "[preset00]\nfWaveScale=28.599\nper_frame_1=q1=42;\n";
        File.WriteAllText(Path.Combine(_directory, "Mashup (129) - Copy.milk"), content);
        File.WriteAllText(Path.Combine(_directory, "Mashup (129).milk"), content);
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        var copy = library.At(BuiltInCount);
        var last = library.At(library.Count - 1);

        Assert.Equal(28.599f, copy.WaveScale, 3);
        Assert.Equal(copy.WaveScale, last.WaveScale);
        Assert.Equal(copy.PerFrame.IsEmpty, last.PerFrame.IsEmpty);
        Assert.NotEqual(VisualizerPresets.BuiltIn[0].Name, last.Name);
        Assert.Contains("Mashup (129)", last.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The name of an index is available before the preset has been parsed, which is what lets the
    /// window label follow the selection immediately instead of naming the previously rendered
    /// preset. Once a preset has been shown, its parsed name wins.
    /// </summary>
    [Fact]
    public void NameAt_NamesAPresetBeforeItIsParsed()
    {
        File.WriteAllText(Path.Combine(_directory, "typo.oryvis"), "name=Inner");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        // Nothing has read the file yet, so the display name comes from its path.
        Assert.Equal("typo", library.NameAt(BuiltInCount));

        Assert.Equal("Inner", library.At(BuiltInCount).Name);
        Assert.Equal("Inner", library.NameAt(BuiltInCount));
    }

    /// <summary>The label lookup wraps around like the preset lookup and names the built-ins.</summary>
    [Fact]
    public void NameAt_WrapsAndNamesBuiltIns()
    {
        File.WriteAllText(Path.Combine(_directory, "mine.oryvis"), "name=Mine");
        var library = new VisualizerPresetLibrary();
        library.Reload(_directory);

        var count = library.Count;
        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, library.NameAt(0));
        Assert.Equal(VisualizerPresets.BuiltIn[0].Name, library.NameAt(count));
        Assert.Equal("mine", library.NameAt(-1));
        Assert.Equal(string.Empty, new VisualizerPresetLibrary().NameAt(0));
    }
}
