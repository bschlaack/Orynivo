namespace Orynivo.Visualization;

/// <summary>
/// One compiled preset expression block. Statements run against a plain slot array, so the
/// render pipeline can seed the built-ins it provides (<c>bass</c>, <c>time</c>, <c>x</c>,
/// <c>y</c>, and so on) and read back the user variables the preset wrote, without any
/// dictionary lookup inside the per-pixel loop.
/// </summary>
public sealed class PresetProgram
{
    private readonly Action<float[]>? _execute;
    private readonly HashSet<string> _referenced;
    private readonly HashSet<string> _written;

    /// <summary>Creates a program over a slot layout.</summary>
    /// <param name="layout">Shared layout of the owning preset.</param>
    /// <param name="execute">Compiled statements, or <see langword="null"/> when empty.</param>
    /// <param name="referenced">Variable names the statements mention, or <see langword="null"/>.</param>
    /// <param name="written">Variable names the statements assign to, or <see langword="null"/>.</param>
    internal PresetProgram(
        PresetVariableLayout layout,
        Action<float[]>? execute,
        IReadOnlyCollection<string>? referenced = null,
        IReadOnlyCollection<string>? written = null)
    {
        Layout = layout;
        _execute = execute;
        _referenced = referenced is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(referenced, StringComparer.Ordinal);
        _written = written is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(written, StringComparer.Ordinal);
    }

    /// <summary>Gets an empty program that runs no statements.</summary>
    public static PresetProgram Empty { get; } = new(new PresetVariableLayout(), null);

    /// <summary>Gets the slot layout every program of the owning preset shares.</summary>
    public PresetVariableLayout Layout { get; }

    /// <summary>Gets the variable names this program reads or writes, in slot order.</summary>
    public IReadOnlyList<string> Variables => Layout.Names;

    /// <summary>Gets a value indicating whether the program has no statements.</summary>
    public bool IsEmpty => _execute is null;

    /// <summary>
    /// Gets the variable names this program references, whether it reads or writes them. The
    /// render pipeline uses it to skip values a preset never looks at, so the set must stay
    /// conservative: reporting a variable the program does not actually use only costs a little
    /// work, while missing one would change the picture.
    /// </summary>
    public IReadOnlyCollection<string> ReferencedVariables => _referenced;

    /// <summary>Reports whether this program references a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns><see langword="true"/> when the compiled statements mention the variable.</returns>
    public bool Uses(string name) => _referenced.Contains(name);

    /// <summary>
    /// Gets the variable names this program assigns to. A per-pixel pass may only run in parallel
    /// when the values it writes are re-seeded for every pixel, because a value written by one
    /// pixel and read by another would make the picture depend on the split.
    /// </summary>
    public IReadOnlyCollection<string> WrittenVariables => _written;

    /// <summary>Reports whether this program assigns to a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns><see langword="true"/> when the compiled statements write the variable.</returns>
    public bool Writes(string name) => _written.Contains(name);

    /// <summary>Returns the slot of a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index, or <c>-1</c> when the program does not use the variable.</returns>
    public int IndexOf(string name) => Layout.IndexOf(name);

    /// <summary>
    /// Runs the compiled statements. The slot array must be at least as long as
    /// <see cref="Variables"/>.
    /// </summary>
    /// <param name="slots">Mutable slot storage.</param>
    /// <exception cref="ArgumentException">The slot array is too short.</exception>
    public void Execute(float[] slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (_execute is null)
            return;
        if (slots.Length < Layout.Count)
            throw new ArgumentException("The slot array is smaller than the program layout.", nameof(slots));

        _execute(slots);
    }
}
