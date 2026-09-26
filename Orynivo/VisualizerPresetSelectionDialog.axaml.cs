using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Orynivo.Localization;
using Orynivo.Visualization;

namespace Orynivo;

/// <summary>
/// Lists every available visualizer preset so the user can activate or deactivate each one. The
/// chosen state is returned through <see cref="DisabledKeys"/>; the visualizer skips deactivated
/// presets while browsing and during automatic advance. The list is built without reading the
/// preset files, so it opens quickly even for a large collection.
/// </summary>
public partial class VisualizerPresetSelectionDialog : Window
{
    private readonly ObservableCollection<VisualizerPresetOption> _options = [];
    private readonly string? _presetDirectory;
    private readonly HashSet<string> _disabledKeys;
    private bool _loading;

    /// <summary>Initializes an empty designer instance.</summary>
    public VisualizerPresetSelectionDialog()
        : this(null, [])
    {
    }

    /// <summary>Initializes the dialog for a preset folder and the currently deactivated keys.</summary>
    /// <param name="presetDirectory">Preset folder, or <see langword="null"/> for the default one.</param>
    /// <param name="disabledKeys">Stable keys of the presets that are currently deactivated.</param>
    public VisualizerPresetSelectionDialog(string? presetDirectory, IReadOnlyCollection<string> disabledKeys)
    {
        _presetDirectory = presetDirectory;
        _disabledKeys = new HashSet<string>(disabledKeys ?? [], StringComparer.Ordinal);
        InitializeComponent();
        PresetListBox.ItemsSource = _options;
        Opened += async (_, _) =>
        {
            WindowChrome.ApplyTheme(this);
            await LoadPresetsAsync();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close(false);
        };
        UpdateActiveCount();
    }

    /// <summary>Gets a value indicating whether the user confirmed the selection.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>Gets the stable keys of the presets the user deactivated.</summary>
    public IReadOnlyList<string> DisabledKeys { get; private set; } = [];

    /// <summary>
    /// Enumerates the built-ins and the preset files below the configured folder away from the UI
    /// thread, then binds one option per preset.
    /// </summary>
    private async Task LoadPresetsAsync()
    {
        _loading = true;
        StatusTextBlock.Text = LocalizationManager.Current.VisualizerPresetSelectionLoading;
        StatusTextBlock.IsVisible = true;
        PresetListBox.IsVisible = false;

        var descriptors = await Task.Run(() =>
        {
            var library = new VisualizerPresetLibrary();
            library.LoadBuiltIns();
            library.Discover(_presetDirectory);
            return library.Describe();
        });

        _loading = false;
        foreach (var descriptor in descriptors)
        {
            var option = new VisualizerPresetOption(descriptor.Key, descriptor.DisplayName, descriptor.IsBuiltIn)
            {
                IsEnabled = !_disabledKeys.Contains(descriptor.Key)
            };
            option.PropertyChanged += OnOptionChanged;
            _options.Add(option);
        }

        if (_options.Count == 0)
        {
            StatusTextBlock.Text = LocalizationManager.Current.VisualizerPresetSelectionEmpty;
            StatusTextBlock.IsVisible = true;
            PresetListBox.IsVisible = false;
        }
        else
        {
            StatusTextBlock.IsVisible = false;
            PresetListBox.IsVisible = true;
        }

        UpdateActiveCount();
    }

    /// <summary>Refreshes the enabled count whenever one option changes.</summary>
    /// <param name="sender">Changed option.</param>
    /// <param name="e">Change details.</param>
    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisualizerPresetOption.IsEnabled))
            UpdateActiveCount();
    }

    /// <summary>Updates the localized summary of active versus total presets.</summary>
    private void UpdateActiveCount()
    {
        var enabled = _options.Count(option => option.IsEnabled);
        ActiveCountTextBlock.Text = string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Current.VisualizerPresetSelectionActiveCount,
            enabled,
            _options.Count);
    }

    /// <summary>Enables every preset.</summary>
    /// <param name="sender">Event source.</param>
    /// <param name="e">Event data.</param>
    private void SelectAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var option in _options)
            option.IsEnabled = true;
    }

    /// <summary>Deactivates every preset.</summary>
    /// <param name="sender">Event source.</param>
    /// <param name="e">Event data.</param>
    private void SelectNoneButton_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var option in _options)
            option.IsEnabled = false;
    }

    /// <summary>Discards the changes and closes the dialog.</summary>
    /// <param name="sender">Event source.</param>
    /// <param name="e">Event data.</param>
    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>Returns the deactivated keys and closes the dialog.</summary>
    /// <param name="sender">Event source.</param>
    /// <param name="e">Event data.</param>
    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        DisabledKeys = _options
            .Where(option => !option.IsEnabled)
            .Select(option => option.Key)
            .ToList();
        Confirmed = true;
        Close(true);
    }
}
