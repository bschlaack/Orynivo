using Orynivo.Visualization;

namespace Orynivo.Visualization;

/// <summary>
/// Collects the presets the visualizer offers: the built-in ones plus every
/// <c>.oryvis</c> or <c>.milk</c> file in the configured folder. A preset that cannot be
/// parsed is skipped and reported instead of breaking the window, so a single bad file in
/// the folder never costs the user the visualizer.
/// </summary>
internal sealed class VisualizerPresetLibrary
{
    private readonly List<VisualizerPreset> _presets = [];
    private readonly List<string> _rejected = [];
    private readonly List<string> _rejectedReasons = [];

    /// <summary>Gets the available presets, built-ins first.</summary>
    public IReadOnlyList<VisualizerPreset> Presets => _presets;

    /// <summary>Gets the file names that could not be loaded.</summary>
    public IReadOnlyList<string> RejectedFiles => _rejected;

    /// <summary>
    /// Gets one diagnostic entry per skipped preset, naming the file or section and the reason it
    /// failed. The entries stay local to the window and are never sent anywhere.
    /// </summary>
    public IReadOnlyList<string> RejectedReasons => _rejectedReasons;

    /// <summary>Returns the default preset folder below the per-user data directory.</summary>
    /// <returns>The absolute folder path.</returns>
    public static string DefaultDirectory => Path.Combine(AppPaths.DataRoot, "visualizer-presets");

    /// <summary>
    /// Loads the built-in presets and every preset file in a folder. Reading happens on the
    /// calling thread, so callers should invoke this away from the UI thread.
    /// </summary>
    /// <param name="directory">Preset folder, or <see langword="null"/> for the default one.</param>
    public void Reload(string? directory)
    {
        _presets.Clear();
        _rejected.Clear();
        _presets.AddRange(VisualizerPresets.BuiltIn);

        var folder = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory.Trim();
        try
        {
            if (!Directory.Exists(folder))
                return;

            foreach (var file in Directory
                         .EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(IsPresetFile)
                         .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    // A .milk file usually holds several presets, one per [presetNN] section, so
                    // every section becomes its own preset instead of only the last one surviving.
                    var sections = VisualizerPreset.ParseSections(File.ReadAllText(file));
                    for (var index = 0; index < sections.Count; index++)
                    {
                        var fallback = sections.Count > 1 ? $"{name} ({index + 1})" : name;
                        try
                        {
                            _presets.Add(VisualizerPreset.Parse(sections[index], fallback));
                        }
                        catch (PresetExpressionException exception)
                        {
                            _rejected.Add(Path.GetFileName(file));
                            var where = sections.Count > 1
                                ? $"{Path.GetFileName(file)} section {index + 1}"
                                : Path.GetFileName(file);
                            _rejectedReasons.Add($"{where}: {exception.Message}");
                        }
                    }
                }
                catch (Exception exception) when (exception is PresetExpressionException or IOException or UnauthorizedAccessException)
                {
                    _rejected.Add(Path.GetFileName(file));
                    _rejectedReasons.Add($"{Path.GetFileName(file)}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _rejected.Add(Path.GetFileName(folder));
        }
    }

    /// <summary>Returns the preset at an index, wrapping around.</summary>
    /// <param name="index">Preset index.</param>
    /// <returns>The selected preset.</returns>
    public VisualizerPreset At(int index)
    {
        if (_presets.Count == 0)
            Reload(null);

        var count = _presets.Count;
        return _presets[((index % count) + count) % count];
    }

    private static bool IsPresetFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".oryvis", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".milk", StringComparison.OrdinalIgnoreCase);
    }
}
