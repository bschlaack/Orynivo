using Orynivo.Localization;

// Exercise the real compiled localization manager without launching the desktop.
foreach (var language in Enum.GetValues<Language>().Concat(Enum.GetValues<Language>().Reverse()))
{
    LocalizationManager.Apply(language);
    var strings = LocalizationManager.Current;
    foreach (var property in typeof(LocalizedStrings).GetProperties())
    {
        var value = (string?)property.GetValue(strings);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{language}: empty {property.Name}");
        // Numeric arguments also exercise N0/F1 and other numeric format specifiers.
        _ = string.Format(value, Enumerable.Repeat<object>(12, 20).ToArray());
        if (Avalonia.Application.Current.Resources.TryGetValue("L_" + property.Name, out var resource)
            && !Equals(resource, value))
            throw new InvalidOperationException($"{language}: stale resource {property.Name}");
    }
    Console.WriteLine($"PASS: {language}, format strings and runtime resource switch.");
}

namespace Avalonia
{
    /// <summary>Minimal resource host for testing the linked production manager.</summary>
    internal sealed class Application
    {
        /// <summary>Gets the test application instance.</summary>
        public static Application Current { get; } = new();

        /// <summary>Gets the resource dictionary populated by the production manager.</summary>
        public Dictionary<string, object> Resources { get; } = new();
    }
}
