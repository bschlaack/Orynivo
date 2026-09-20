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

    /// <summary>Creates a program over a slot layout.</summary>
    /// <param name="layout">Shared layout of the owning preset.</param>
    /// <param name="execute">Compiled statements, or <see langword="null"/> when empty.</param>
    internal PresetProgram(PresetVariableLayout layout, Action<float[]>? execute)
    {
        Layout = layout;
        _execute = execute;
    }

    /// <summary>Gets an empty program that runs no statements.</summary>
    public static PresetProgram Empty { get; } = new(new PresetVariableLayout(), null);

    /// <summary>Gets the slot layout every program of the owning preset shares.</summary>
    public PresetVariableLayout Layout { get; }

    /// <summary>Gets the variable names this program reads or writes, in slot order.</summary>
    public IReadOnlyList<string> Variables => Layout.Names;

    /// <summary>Gets a value indicating whether the program has no statements.</summary>
    public bool IsEmpty => _execute is null;

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
