using Orynivo.Visualization;

namespace Orynivo.Visualization;

/// <summary>
/// Collects the presets the visualizer offers: the built-in ones plus every <c>.oryvis</c> or
/// <c>.milk</c> file below the configured folder, including its subfolders, because preset
/// collections are usually sorted into directories. A file that cannot be parsed is skipped and
/// reported instead of breaking the window, so a single bad file never costs the user the
/// visualizer.
/// </summary>
/// <remarks>
/// User presets are discovered eagerly but parsed lazily, one at a time, the first time they are
/// shown. Compiling a preset means building and JIT-compiling its expression trees, which costs
/// milliseconds per preset, so a collection of several hundred presets must not be compiled when
/// the window opens. Discovery itself only reads and splits the files, which is cheap.
/// </remarks>
internal sealed class VisualizerPresetLibrary
{
    private readonly List<VisualizerPreset> _builtIn = [];
    private readonly List<PendingPreset> _pending = [];
    private readonly Dictionary<int, VisualizerPreset> _loaded = [];
    private readonly HashSet<int> _failed = [];
    private readonly List<string> _rejected = [];
    private readonly List<string> _rejectedReasons = [];

    /// <summary>Gets the number of available presets, built-ins first.</summary>
    public int Count => _builtIn.Count + _pending.Count;

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
    /// Loads the built-in presets and discovers every preset file below a folder, including its
    /// subfolders. The files are only read and split here, never compiled. Reading happens on the
    /// calling thread, so callers should invoke this away from the UI thread when the folder is
    /// large.
    /// </summary>
    /// <param name="directory">Preset folder, or <see langword="null"/> for the default one.</param>
    public void Reload(string? directory)
    {
        _builtIn.Clear();
        _pending.Clear();
        _loaded.Clear();
        _failed.Clear();
        _rejected.Clear();
        _rejectedReasons.Clear();
        _builtIn.AddRange(VisualizerPresets.BuiltIn);

        var folder = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory.Trim();
        try
        {
            if (!Directory.Exists(folder))
                return;

            var files = Directory
                .EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsPresetFile)
                .OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase);
            foreach (var file in files)
            {
                try
                {
                    // A .milk file usually holds several presets, one per [presetNN] section, so
                    // every section becomes its own preset instead of only the last one surviving.
                    var sections = VisualizerPreset.ParseSections(File.ReadAllText(file));
                    var name = Path.GetFileNameWithoutExtension(file);
                    for (var index = 0; index < sections.Count; index++)
                        _pending.Add(new PendingPreset(name, file, index, sections.Count));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    _rejected.Add(Path.GetFileName(file));
                    _rejectedReasons.Add($"{Path.GetFileName(file)}: {exception.Message}");
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _rejected.Add(Path.GetFileName(folder));
            _rejectedReasons.Add($"{Path.GetFileName(folder)}: {exception.Message}");
        }
    }

    /// <summary>
    /// Returns the preset at an index, wrapping around, and parses it on first use. A preset that
    /// fails to parse is reported and replaced by the first built-in, so a broken file can never
    /// stop the visualizer.
    /// </summary>
    /// <param name="index">Preset index.</param>
    /// <returns>The selected preset.</returns>
    public VisualizerPreset At(int index)
    {
        if (Count == 0)
            Reload(null);

        var wrapped = ((index % Count) + Count) % Count;
        if (wrapped < _builtIn.Count)
            return _builtIn[wrapped];

        if (_loaded.TryGetValue(wrapped, out var cached))
            return cached;

        var pending = _pending[wrapped - _builtIn.Count];
        var fallbackName = pending.SectionCount > 1
            ? $"{pending.FileName} ({pending.SectionIndex + 1})"
            : pending.FileName;
        if (_failed.Contains(wrapped))
            return Fallback();

        try
        {
            var sections = VisualizerPreset.ParseSections(File.ReadAllText(pending.Path));
            var text = pending.SectionIndex < sections.Count ? sections[pending.SectionIndex] : string.Empty;
            var preset = VisualizerPreset.Parse(text, fallbackName);
            _loaded[wrapped] = preset;
            return preset;
        }
        catch (Exception exception) when (exception is PresetExpressionException or IOException or UnauthorizedAccessException)
        {
            _failed.Add(wrapped);
            var fileName = Path.GetFileName(pending.Path);
            _rejected.Add(fileName);
            var where = pending.SectionCount > 1
                ? $"{fileName} section {pending.SectionIndex + 1}"
                : fileName;
            _rejectedReasons.Add($"{where}: {exception.Message}");
            return Fallback();
        }
    }

    /// <summary>Returns the preset used when a user preset cannot be parsed.</summary>
    /// <returns>The first built-in, or an empty preset when none is available.</returns>
    private VisualizerPreset Fallback() =>
        _builtIn.Count > 0 ? _builtIn[0] : VisualizerPreset.Create("Empty", null, null);

    private static bool IsPresetFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".oryvis", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".milk", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One preset discovered on disk but not parsed yet. The section index identifies which
    /// preset of a multi-preset file this entry stands for.
    /// </summary>
    /// <param name="FileName">File name without extension, used as the display name.</param>
    /// <param name="Path">Full path of the file.</param>
    /// <param name="SectionIndex">Zero-based index of the section inside the file.</param>
    /// <param name="SectionCount">Number of sections the file holds.</param>
    private sealed record PendingPreset(string FileName, string Path, int SectionIndex, int SectionCount);
}
