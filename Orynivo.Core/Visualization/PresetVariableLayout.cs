namespace Orynivo.Visualization;

/// <summary>
/// The shared slot layout of one preset. Milkdrop presets pass user variables such as
/// <c>q1</c> from the per-frame stage into the per-pixel stage, so every expression block
/// of a preset compiles against the same layout and runs over the same slot array.
/// </summary>
public sealed class PresetVariableLayout
{
    private readonly List<string> _names = [];
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);

    /// <summary>Gets the variable names in slot order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>Gets the number of allocated slots.</summary>
    public int Count => _names.Count;

    /// <summary>Returns the slot of a variable.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index, or <c>-1</c> when the layout does not contain it.</returns>
    public int IndexOf(string name) =>
        name is not null && _slots.TryGetValue(name, out var slot) ? slot : -1;

    /// <summary>Returns the slot of a variable, allocating one on first use.</summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The slot index.</returns>
    public int GetOrAdd(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_slots.TryGetValue(name, out var existing))
            return existing;

        var slot = _names.Count;
        _names.Add(name);
        _slots[name] = slot;
        return slot;
    }
}
