using System.Diagnostics;
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
/// Discovery only enumerates file paths, and parsing happens one file at a time the first time a
/// preset is shown. This matters: a real preset collection holds thousands of files and over a
/// hundred megabytes, and reading them up front blocked the interface for minutes. A file with
/// several <c>[presetNN]</c> sections exposes its remaining sections the first time one of them is
/// shown, so nothing is lost by not reading the files during discovery.
/// </remarks>
internal sealed class VisualizerPresetLibrary
{
    /// <summary>Upper bound on the files one discovery pass records.</summary>
    public const int MaxDiscoveredFiles = 20000;

    private readonly List<VisualizerPreset> _builtIn = [];
    private readonly List<PendingPreset> _pending = [];
    private readonly Dictionary<int, VisualizerPreset> _loaded = [];
    private readonly HashSet<int> _failed = [];
    private readonly List<string> _rejected = [];
    private readonly List<string> _rejectedReasons = [];
    private readonly object _gate = new();

    /// <summary>Gets the number of available presets, built-ins first.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _builtIn.Count + _pending.Count;
        }
    }

    /// <summary>Gets the file names that could not be loaded.</summary>
    public IReadOnlyList<string> RejectedFiles => _rejected;

    /// <summary>
    /// Gets one diagnostic entry per skipped preset, naming the file or section and the reason it
    /// failed. The entries stay local to the window and are never sent anywhere.
    /// </summary>
    public IReadOnlyList<string> RejectedReasons => _rejectedReasons;

    /// <summary>Gets a value indicating whether a discovery pass has completed.</summary>
    public bool IsDiscovered { get; private set; }

    /// <summary>Returns the default preset folder below the per-user data directory.</summary>
    /// <returns>The absolute folder path.</returns>
    public static string DefaultDirectory => Path.Combine(AppPaths.DataRoot, "visualizer-presets");

    /// <summary>Makes the built-in presets available immediately, without touching the disk.</summary>
    public void LoadBuiltIns()
    {
        lock (_gate)
        {
            _builtIn.Clear();
            _builtIn.AddRange(VisualizerPresets.BuiltIn);
        }
    }

    /// <summary>
    /// Records every preset file below a folder, including its subfolders, without reading any of
    /// them. Only the paths are enumerated, so this stays fast even for a collection with
    /// thousands of files; callers should still invoke it away from the UI thread.
    /// </summary>
    /// <param name="directory">Preset folder, or <see langword="null"/> for the default one.</param>
    /// <returns>How long the enumeration took.</returns>
    public TimeSpan Discover(string? directory)
    {
        var watch = Stopwatch.StartNew();
        // The built-ins must always be present, no matter which entry point a caller uses first.
        if (_builtIn.Count == 0)
            LoadBuiltIns();

        var folder = string.IsNullOrWhiteSpace(directory) ? DefaultDirectory : directory.Trim();
        var discovered = new List<PendingPreset>();
        try
        {
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    if (discovered.Count >= MaxDiscoveredFiles)
                        break;
                    if (IsPresetFile(file))
                        discovered.Add(new PendingPreset(file, 0, 1));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _rejected.Add(Path.GetFileName(folder));
            _rejectedReasons.Add($"{Path.GetFileName(folder)}: {exception.Message}");
        }

        discovered.Sort(static (left, right) =>
        {
            var byName = string.Compare(
                Path.GetFileName(left.Path),
                Path.GetFileName(right.Path),
                StringComparison.CurrentCultureIgnoreCase);
            return byName != 0
                ? byName
                : string.Compare(left.Path, right.Path, StringComparison.CurrentCultureIgnoreCase);
        });

        lock (_gate)
        {
            _pending.Clear();
            _pending.AddRange(discovered);
            _loaded.Clear();
            _failed.Clear();
        }

        IsDiscovered = true;
        watch.Stop();
        return watch.Elapsed;
    }

    /// <summary>Loads the built-ins and discovers the presets of a folder.</summary>
    /// <param name="directory">Preset folder, or <see langword="null"/> for the default one.</param>
    public void Reload(string? directory)
    {
        LoadBuiltIns();
        Discover(directory);
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
        PendingPreset pending;
        int wrapped;
        lock (_gate)
        {
            var count = _builtIn.Count + _pending.Count;
            if (count == 0)
            {
                _builtIn.AddRange(VisualizerPresets.BuiltIn);
                count = _builtIn.Count + _pending.Count;
            }

            wrapped = ((index % count) + count) % count;
            if (wrapped < _builtIn.Count)
                return _builtIn[wrapped];

            if (_loaded.TryGetValue(wrapped, out var cached))
                return cached;

            pending = _pending[wrapped - _builtIn.Count];
        }

        var fallbackName = pending.SectionCount > 1
            ? $"{Path.GetFileNameWithoutExtension(pending.Path)} ({pending.SectionIndex + 1})"
            : Path.GetFileNameWithoutExtension(pending.Path);
        lock (_gate)
        {
            if (_failed.Contains(wrapped))
                return Fallback();
        }

        try
        {
            var sections = VisualizerPreset.ParseSections(File.ReadAllText(pending.Path));
            var text = pending.SectionIndex < sections.Count ? sections[pending.SectionIndex] : string.Empty;
            var preset = VisualizerPreset.Parse(text, fallbackName);
            lock (_gate)
            {
                _loaded[wrapped] = preset;
                // A file with several sections exposes the rest the first time one of them is
                // shown, so a multi-preset file is complete without reading it during discovery.
                if (pending.SectionIndex == 0 && sections.Count > 1)
                {
                    for (var extra = 1; extra < sections.Count; extra++)
                        _pending.Add(new PendingPreset(pending.Path, extra, sections.Count));
                }
            }

            return preset;
        }
        catch (Exception exception) when (exception is PresetExpressionException or IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                _failed.Add(wrapped);
                var fileName = Path.GetFileName(pending.Path);
                _rejected.Add(fileName);
                var where = pending.SectionCount > 1
                    ? $"{fileName} section {pending.SectionIndex + 1}"
                    : fileName;
                _rejectedReasons.Add($"{where}: {exception.Message}");
            }

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
    /// One preset file discovered on disk but not read yet. The section index identifies which
    /// preset of a multi-preset file this entry stands for.
    /// </summary>
    /// <param name="Path">Full path of the file.</param>
    /// <param name="SectionIndex">Zero-based index of the section inside the file.</param>
    /// <param name="SectionCount">Number of sections the file is known to hold.</param>
    private sealed record PendingPreset(string Path, int SectionIndex, int SectionCount);
}
