using System.Net.Http.Headers;
using System.Text.Json;

namespace Orynivo.AI;

/// <summary>Queries and validates OpenAI-compatible API endpoints without exposing credentials.</summary>
internal sealed class AiEndpointService : IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Loads the model identifiers advertised by an OpenAI-compatible endpoint.</summary>
    /// <param name="endpointUrl">Configured API base URL or chat-completions URL.</param>
    /// <param name="apiKey">Optional bearer token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The distinct advertised model identifiers in display order.</returns>
    public async Task<IReadOnlyList<string>> GetModelsAsync(
        string endpointUrl,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        var endpoint = BuildModelsEndpoint(endpointUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadModelIds(document.RootElement);
    }

    /// <summary>Builds the models endpoint from a configured OpenAI-compatible URL.</summary>
    /// <param name="endpointUrl">API base URL or chat-completions URL.</param>
    /// <returns>An absolute HTTP or HTTPS models endpoint.</returns>
    /// <exception cref="ArgumentException">Thrown when the URL is invalid or contains embedded credentials.</exception>
    internal static Uri BuildModelsEndpoint(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl?.Trim(), UriKind.Absolute, out var configured) ||
            (configured.Scheme != Uri.UriSchemeHttp && configured.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(configured.UserInfo))
        {
            throw new ArgumentException("A credential-free absolute HTTP or HTTPS endpoint is required.", nameof(endpointUrl));
        }

        var builder = new UriBuilder(configured);
        var path = builder.Path.TrimEnd('/');
        const string chatSuffix = "/chat/completions";
        if (path.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
            path = path[..^chatSuffix.Length];
        if (!path.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            path += "/models";
        builder.Path = path;
        builder.Query = string.Empty;
        builder.Fragment = string.Empty;
        return builder.Uri;
    }

    /// <summary>Reads model identifiers from OpenAI-compatible and Ollama-compatible response shapes.</summary>
    /// <param name="root">Response root element.</param>
    /// <returns>Distinct non-empty model identifiers.</returns>
    internal static IReadOnlyList<string> ReadModelIds(JsonElement root)
    {
        var models = new List<string>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in data.EnumerateArray())
                AddStringProperty(entry, "id", models);
        }
        else if (root.TryGetProperty("models", out var nativeModels) && nativeModels.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in nativeModels.EnumerateArray())
            {
                if (!AddStringProperty(entry, "name", models))
                    AddStringProperty(entry, "model", models);
            }
        }

        return models
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static model => model, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool AddStringProperty(JsonElement element, string propertyName, ICollection<string> target)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        var value = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return false;
        target.Add(value);
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _httpClient.Dispose();
}
