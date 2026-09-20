using System.Text.Json.Nodes;
using Orynivo.AI;
using Xunit;

namespace Orynivo.Tests;

/// <summary>
/// Validates the OpenAI function-calling schema of every AI Chat tool, so a
/// malformed definition cannot reach a model.
/// </summary>
public sealed class AiToolDefinitionsTests
{
    /// <summary>Every tool definition is a well-formed function schema.</summary>
    [Fact]
    public void GetAll_ReturnsWellFormedFunctionSchemas()
    {
        var definitions = AiToolDefinitions.GetAll();

        Assert.NotEmpty(definitions);
        foreach (var definition in definitions)
        {
            var function = definition["function"]!.AsObject();
            var name = function["name"]!.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(name), "A tool has an empty name.");
            Assert.False(
                string.IsNullOrWhiteSpace(function["description"]!.GetValue<string>()),
                $"{name} has no description.");

            var parameters = function["parameters"]!.AsObject();
            Assert.Equal("object", parameters["type"]!.GetValue<string>());

            var properties = parameters["properties"]!.AsObject();
            foreach (var property in properties)
            {
                var schema = property.Value!.AsObject();
                Assert.True(schema.ContainsKey("type"), $"{name}.{property.Key} has no type.");
                Assert.False(
                    string.IsNullOrWhiteSpace(schema["description"]?.GetValue<string>()),
                    $"{name}.{property.Key} has no description.");
            }

            if (parameters["required"] is not JsonArray required)
                continue;
            foreach (var entry in required)
            {
                var requiredName = entry!.GetValue<string>();
                Assert.True(
                    properties.ContainsKey(requiredName),
                    $"{name} requires the unknown property {requiredName}.");
            }
        }
    }

    /// <summary>Tool names are unique.</summary>
    [Fact]
    public void GetAll_UsesUniqueToolNames()
    {
        var names = AiToolDefinitions.GetAll()
            .Select(definition => definition["function"]!["name"]!.GetValue<string>())
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }
}
