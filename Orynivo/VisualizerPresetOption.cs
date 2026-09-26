using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Orynivo;

/// <summary>
/// One selectable row in <see cref="VisualizerPresetSelectionDialog"/>. The row binds its stable
/// preset key to a checkbox so the user can activate or deactivate the preset.
/// </summary>
public sealed class VisualizerPresetOption : INotifyPropertyChanged
{
    private bool _isEnabled = true;

    /// <summary>Initializes a preset option.</summary>
    /// <param name="key">Stable preset key persisted in the settings.</param>
    /// <param name="name">Display name shown to the user.</param>
    /// <param name="isBuiltIn">Whether the preset is one of the shipped built-ins.</param>
    public VisualizerPresetOption(string key, string name, bool isBuiltIn)
    {
        Key = key;
        Name = name;
        IsBuiltIn = isBuiltIn;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the stable preset key persisted in the settings.</summary>
    public string Key { get; }

    /// <summary>Gets the display name shown to the user.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the preset is one of the shipped built-ins.</summary>
    public bool IsBuiltIn { get; }

    /// <summary>Gets or sets a value indicating whether the preset is active.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            RaisePropertyChanged();
        }
    }

    /// <summary>Raises the change notification for a property.</summary>
    /// <param name="propertyName">Name of the changed property.</param>
    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
