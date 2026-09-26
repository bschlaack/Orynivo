using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Orynivo.Audio;
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
    private readonly List<string> _discoveredFiles = [];
    private readonly HashSet<string> _disabledKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<int, VisualizerPreset> _loaded = [];
    private readonly Dictionary<int, string> _sourceHashes = [];
    private readonly HashSet<int> _failed = [];
    private readonly List<string> _rejected = [];
    private readonly List<string> _rejectedReasons = [];
    private readonly object _gate = new();
    private string? _discoveredFolder;

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
            _discoveredFiles.Clear();
            _discoveredFiles.AddRange(discovered.Select(static preset => preset.Path));
            _discoveredFolder = folder;
            _loaded.Clear();
            _sourceHashes.Clear();
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
            {
                SeekDiagnostics.Log("visualizer-preset", $"index={wrapped} cache-hit name={cached.Name} sha256={_sourceHashes.GetValueOrDefault(wrapped, "unknown")}");
                return cached;
            }

            pending = _pending[wrapped - _builtIn.Count];
        }

        var fallbackName = DisplayName(pending);
        lock (_gate)
        {
            if (_failed.Contains(wrapped))
                return Fallback();
        }

        try
        {
            var sections = VisualizerPreset.ParseSections(File.ReadAllText(pending.Path));
            var text = pending.SectionIndex < sections.Count ? sections[pending.SectionIndex] : string.Empty;
            var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            SeekDiagnostics.Log(
                "visualizer-preset",
                $"compiler-input index={wrapped} file={Path.GetFileName(pending.Path)} section={pending.SectionIndex} " +
                $"sha256={sha256} length={text.Length}\n--- BEGIN PRESET SOURCE ---\n{text}\n--- END PRESET SOURCE ---");
            var preset = VisualizerPreset.Parse(text, fallbackName);
            lock (_gate)
            {
                _loaded[wrapped] = preset;
                _sourceHashes[wrapped] = sha256;
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

    /// <summary>
    /// Returns the display name of the preset at an index, wrapping around, without parsing it.
    /// The visualizer's label uses this so the name follows the selection immediately: reading the
    /// rendered preset instead reported the previously shown preset until the render thread had
    /// applied the switch. That made the name disagree with the picture for exactly the switch the
    /// user had just made, which is most visible at the end of a short user list, where the next
    /// index wraps into the built-ins and the label still named the last user preset.
    /// </summary>
    /// <param name="index">Preset index.</param>
    /// <returns>The display name, or an empty string when no preset is available.</returns>
    public string NameAt(int index)
    {
        lock (_gate)
        {
            var count = _builtIn.Count + _pending.Count;
            if (count == 0)
                return string.Empty;

            var wrapped = ((index % count) + count) % count;
            if (wrapped < _builtIn.Count)
                return _builtIn[wrapped].Name;
            if (_loaded.TryGetValue(wrapped, out var cached))
                return cached.Name;

            return DisplayName(_pending[wrapped - _builtIn.Count]);
        }
    }

    /// <summary>
    /// Returns the stable key of the preset at an index, wrapping around. A built-in uses
    /// <c>builtin:&lt;name&gt;</c>; a user preset file uses <c>file:&lt;path relative to the preset
    /// folder&gt;</c>, so every section of one file shares the file's key.
    /// </summary>
    /// <param name="index">Preset index.</param>
    /// <returns>The stable key, or an empty string when no preset is available.</returns>
    public string KeyAt(int index)
    {
        lock (_gate)
        {
            var count = _builtIn.Count + _pending.Count;
            if (count == 0)
                return string.Empty;

            return KeyAtLocked(((index % count) + count) % count);
        }
    }

    /// <summary>
    /// Lists every preset the library offers: the built-ins followed by every discovered file.
    /// The list is built without reading the files, so opening the selection dialog over a large
    /// collection stays fast; a multi-section file appears as one entry under its file name.
    /// </summary>
    /// <returns>One descriptor per preset, in display order.</returns>
    public IReadOnlyList<VisualizerPresetDescriptor> Describe()
    {
        lock (_gate)
        {
            var result = new List<VisualizerPresetDescriptor>(_builtIn.Count + _discoveredFiles.Count);
            foreach (var preset in _builtIn)
                result.Add(new VisualizerPresetDescriptor(BuiltInKey(preset.Name), preset.Name, true));
            foreach (var path in _discoveredFiles)
            {
                result.Add(new VisualizerPresetDescriptor(
                    FileKey(RelativePathOf(path)),
                    Path.GetFileNameWithoutExtension(path),
                    false));
            }

            return result;
        }
    }

    /// <summary>Replaces the set of preset keys the visualizer must skip.</summary>
    /// <param name="keys">Stable keys to deactivate, or <see langword="null"/> for none.</param>
    public void SetDisabledKeys(IEnumerable<string>? keys)
    {
        lock (_gate)
        {
            _disabledKeys.Clear();
            if (keys is null)
                return;

            foreach (var key in keys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    _disabledKeys.Add(key);
            }
        }
    }

    /// <summary>Gets a value indicating whether the preset at an index is deactivated.</summary>
    /// <param name="index">Preset index.</param>
    /// <returns><see langword="true"/> when the preset's key is in the disabled set.</returns>
    public bool IsDisabled(int index)
    {
        lock (_gate)
        {
            var count = _builtIn.Count + _pending.Count;
            if (count == 0 || _disabledKeys.Count == 0)
                return false;

            return _disabledKeys.Contains(KeyAtLocked(((index % count) + count) % count));
        }
    }

    /// <summary>
    /// Resolves an index to the first enabled preset at or after it in the requested direction,
    /// wrapping around. When every preset is deactivated, the original wrapped index is returned
    /// so the visualizer never stops rendering.
    /// </summary>
    /// <param name="index">Starting preset index.</param>
    /// <param name="direction">Step direction; a negative value scans backwards.</param>
    /// <returns>The resolved index.</returns>
    public int ResolveEnabledIndex(int index, int direction)
    {
        if (direction == 0)
            direction = 1;

        lock (_gate)
        {
            var count = _builtIn.Count + _pending.Count;
            if (count == 0)
                return 0;

            var wrapped = ((index % count) + count) % count;
            if (_disabledKeys.Count == 0)
                return wrapped;

            for (var step = 0; step < count; step++)
            {
                if (!_disabledKeys.Contains(KeyAtLocked(wrapped)))
                    return wrapped;
                wrapped = ((wrapped + direction) % count + count) % count;
            }

            return ((index % count) + count) % count;
        }
    }

    /// <summary>Builds the stable key of a built-in preset.</summary>
    /// <param name="name">Built-in preset name.</param>
    /// <returns>The namespaced key.</returns>
    public static string BuiltInKey(string name) => "builtin:" + name;

    /// <summary>Builds the stable key of a user preset file.</summary>
    /// <param name="relativePath">Path relative to the configured preset folder.</param>
    /// <returns>The namespaced key with forward slashes.</returns>
    public static string FileKey(string relativePath) => "file:" + relativePath.Replace('\\', '/');

    /// <summary>Returns the stable key for an already wrapped index; the caller holds the gate.</summary>
    /// <param name="wrapped">Index inside the combined preset list.</param>
    /// <returns>The stable key.</returns>
    private string KeyAtLocked(int wrapped) =>
        wrapped < _builtIn.Count
            ? BuiltInKey(_builtIn[wrapped].Name)
            : FileKey(RelativePathOf(_pending[wrapped - _builtIn.Count].Path));

    /// <summary>Derives a preset file's path relative to the discovered folder.</summary>
    /// <param name="path">Full preset file path.</param>
    /// <returns>The relative path, or the file name when no folder is known.</returns>
    private string RelativePathOf(string path)
    {
        if (!string.IsNullOrEmpty(_discoveredFolder))
        {
            try
            {
                return Path.GetRelativePath(_discoveredFolder, path);
            }
            catch (ArgumentException)
            {
            }
        }

        return Path.GetFileName(path);
    }

    /// <summary>Returns the preset used when a user preset cannot be parsed.</summary>
    /// <returns>The first built-in, or an empty preset when none is available.</returns>
    private VisualizerPreset Fallback() =>
        _builtIn.Count > 0 ? _builtIn[0] : VisualizerPreset.Create("Empty", null, null);

    /// <summary>
    /// Derives the name of an unparsed preset file from its path. A multi-preset file numbers its
    /// sections so each one stays distinguishable.
    /// </summary>
    /// <param name="pending">Discovered preset that has not been read yet.</param>
    /// <returns>The display name.</returns>
    private static string DisplayName(PendingPreset pending) =>
        pending.SectionCount > 1
            ? $"{Path.GetFileNameWithoutExtension(pending.Path)} ({pending.SectionIndex + 1})"
            : Path.GetFileNameWithoutExtension(pending.Path);


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

/// <summary>
/// One preset offered by <see cref="VisualizerPresetLibrary"/>, identified by its stable key. The
/// selection dialog lists these without reading the preset files.
/// </summary>
/// <param name="Key">Stable key used to persist the enabled state.</param>
/// <param name="DisplayName">Name shown in the selection dialog.</param>
/// <param name="IsBuiltIn">Whether the preset is one of the shipped built-ins.</param>
internal sealed record VisualizerPresetDescriptor(string Key, string DisplayName, bool IsBuiltIn);
