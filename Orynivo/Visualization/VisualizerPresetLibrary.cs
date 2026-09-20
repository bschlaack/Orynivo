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

    /// <summary>Gets the available presets, built-ins first.</summary>
    public IReadOnlyList<VisualizerPreset> Presets => _presets;

    /// <summary>Gets the file names that could not be loaded.</summary>
    public IReadOnlyList<string> RejectedFiles => _rejected;

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
                    var preset = VisualizerPreset.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file));
                    _presets.Add(preset);
                }
                catch (Exception exception) when (exception is PresetExpressionException or IOException or UnauthorizedAccessException)
                {
                    _rejected.Add(Path.GetFileName(file));
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
