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
    private readonly Dictionary<string, int> _slots;

    /// <summary>Creates a program over a slot layout.</summary>
    /// <param name="variables">Variable names in slot order.</param>
    /// <param name="execute">Compiled statements, or <see langword="null"/> when empty.</param>
    internal PresetProgram(IReadOnlyList<string> variables, Action<float[]>? execute)
    {
        Variables = variables;
        _execute = execute;
        _slots = new Dictionary<string, int>(variables.Count, StringComparer.Ordinal);
        for (var slot = 0; slot < variables.Count; slot++)
            _slots[variables[slot]] = slot;
    }

    /// <summary>Gets an empty program that runs no statements.</summary>
    public static PresetProgram Empty { get; } = new([], null);

    /// <summary>Gets the variable names this program reads or writes, in slot order.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Gets a value indicating whether the program has no statements.</summary>
    public bool IsEmpty => _execute is null;

    /// <summary>Returns the slot of a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index, or <c>-1</c> when the program does not use the variable.</returns>
    public int IndexOf(string name) =>
        name is not null && _slots.TryGetValue(name, out var slot) ? slot : -1;

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
        if (slots.Length < Variables.Count)
            throw new ArgumentException("The slot array is smaller than the program layout.", nameof(slots));

        _execute(slots);
    }
}
