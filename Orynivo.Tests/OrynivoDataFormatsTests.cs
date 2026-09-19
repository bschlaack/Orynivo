using Orynivo;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Guards the application drag-and-drop data formats. Avalonia validates an
/// application identifier eagerly, so an invalid one would otherwise crash the
/// main window's static initializer during startup.
/// </summary>
public sealed class OrynivoDataFormatsTests
{
    /// <summary>The queue drag format is creatable and uses a valid identifier.</summary>
    [Fact]
    public void QueueDragTokens_HasAValidApplicationIdentifier()
    {
        var format = OrynivoDataFormats.QueueDragTokens;

        Assert.Equal("orynivo.queue-paths", format.Identifier);
        Assert.Equal(Avalonia.Input.DataFormatKind.Application, format.Kind);
        // Avalonia only accepts ASCII letters, digits, the dot, and the hyphen.
        Assert.All(
            format.Identifier,
            character => Assert.True(
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-',
                $"Invalid application format character '{character}'."));
    }
}
