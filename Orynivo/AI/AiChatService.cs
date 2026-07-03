using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Orynivo.AI;

/// <summary>
/// Sends messages to an OpenAI-compatible chat-completions endpoint using streaming SSE,
/// executes tool calls in a loop, and emits <see cref="AiStreamEvent"/> values as they arrive.
/// Compatible with LM Studio, Ollama, OpenAI, Anthropic (via compatibility layer), and any
/// OpenAI-compatible provider.
/// </summary>
internal sealed class AiChatService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private static readonly string SystemPrompt =
        """
        You are an AI assistant embedded in Orynivo, a personal Windows audio player.
        You have access to tools that let you search the local music library, manage
        playlists, check play history, and control playback. Use these tools to give
        accurate, personalized answers about the user's music collection.
        Always respond in the same language the user writes in.
        When presenting lists of results, keep formatting concise.
        """;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly List<ApiMessage> _history = [];

    /// <summary>Clears the current conversation history.</summary>
    public void ClearHistory() => _history.Clear();

    /// <summary>Returns the number of messages in the current conversation.</summary>
    public int MessageCount => _history.Count;

    /// <summary>
    /// Sends a user message and streams back events including content tokens, tool-call
    /// notifications, errors, and a final <see cref="AiStreamEvent.DoneEvent"/>.
    /// Internally loops until the model emits <c>stop</c> (i.e. after all tool calls are resolved).
    /// </summary>
    /// <param name="userMessage">Message text from the user.</param>
    /// <param name="settings">Active AI chat settings (endpoint, key, model, max tokens).</param>
    /// <param name="executor">Tool executor used to dispatch model-requested tool calls.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Asynchronous stream of <see cref="AiStreamEvent"/> values.</returns>
    public async IAsyncEnumerable<AiStreamEvent> SendAsync(
        string userMessage,
        AiChatSettings settings,
        AiToolExecutor executor,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _history.Add(new ApiMessage("user", userMessage));

        var tools = AiToolDefinitions.GetAll();
        var endpoint = BuildEndpoint(settings.EndpointUrl);

        while (!ct.IsCancellationRequested)
        {
            var messages = BuildMessages();
            var request = new ApiRequest(
                Model: settings.ModelName,
                Messages: messages,
                Tools: tools.Count > 0 ? tools : null,
                Stream: true,
                MaxTokens: settings.MaxTokens > 0 ? settings.MaxTokens : 2048);

            // Fetch the response — can't yield inside catch, so capture error in a string
            HttpResponseMessage? response = null;
            string? connectError = null;
            bool cancelled = false;
            try
            {
                response = await PostStreamAsync(request, endpoint, settings.ApiKey, ct);
            }
            catch (OperationCanceledException) { cancelled = true; }
            catch (Exception ex) { connectError = $"Connection error: {ex.Message}"; }

            if (cancelled) yield break;
            if (connectError is not null) { yield return AiStreamEvent.Error(connectError); yield break; }

            if (!response!.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                yield return AiStreamEvent.Error($"API error {(int)response.StatusCode}: {Truncate(body, 300)}");
                response.Dispose();
                yield break;
            }

            // Parse the SSE stream
            string? finishReason = null;
            var contentBuilder = new StringBuilder();
            var toolAccumulators = new SortedDictionary<int, ToolCallAccumulator>();

            using (response)
            using (var stream = await response.Content.ReadAsStreamAsync(ct))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = null;
                    bool readCancelled = false;
                    try { line = await reader.ReadLineAsync(ct); }
                    catch (OperationCanceledException) { readCancelled = true; }

                    if (readCancelled) yield break;
                    if (line is null) break;
                    if (line.Length == 0 || !line.StartsWith("data: ", StringComparison.Ordinal))
                        continue;

                    var data = line.Substring("data: ".Length).Trim();
                    if (data == "[DONE]") break;

                    JsonDocument? doc = null;
                    try { doc = JsonDocument.Parse(data); }
                    catch { continue; }

                    using (doc)
                    {
                        if (!doc.RootElement.TryGetProperty("choices", out var choices) ||
                            choices.GetArrayLength() == 0)
                            continue;

                        var choice = choices[0];
                        if (choice.TryGetProperty("finish_reason", out var fr) &&
                            fr.ValueKind != JsonValueKind.Null)
                            finishReason = fr.GetString();

                        if (!choice.TryGetProperty("delta", out var delta)) continue;

                        // Content token
                        if (delta.TryGetProperty("content", out var ct2) &&
                            ct2.ValueKind == JsonValueKind.String)
                        {
                            var token = ct2.GetString()!;
                            if (token.Length > 0)
                            {
                                contentBuilder.Append(token);
                                yield return AiStreamEvent.Token(token);
                            }
                        }

                        // Tool call chunks (accumulated across multiple SSE events)
                        if (delta.TryGetProperty("tool_calls", out var tcArr) &&
                            tcArr.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var tc in tcArr.EnumerateArray())
                            {
                                var idx = tc.TryGetProperty("index", out var idxEl)
                                    ? idxEl.GetInt32() : 0;

                                if (!toolAccumulators.TryGetValue(idx, out var acc))
                                {
                                    acc = new ToolCallAccumulator();
                                    toolAccumulators[idx] = acc;
                                }

                                if (tc.TryGetProperty("id", out var idEl) &&
                                    idEl.ValueKind == JsonValueKind.String)
                                    acc.Id = idEl.GetString() ?? acc.Id;

                                if (tc.TryGetProperty("function", out var fn))
                                {
                                    if (fn.TryGetProperty("name", out var nameEl) &&
                                        nameEl.ValueKind == JsonValueKind.String)
                                        acc.Name = nameEl.GetString() ?? acc.Name;

                                    if (fn.TryGetProperty("arguments", out var argsEl) &&
                                        argsEl.ValueKind == JsonValueKind.String)
                                        acc.Args.Append(argsEl.GetString());
                                }
                            }
                        }
                    }
                }
            }

            if (finishReason == "tool_calls" && toolAccumulators.Count > 0)
            {
                // Add assistant message with tool calls to history
                var apiToolCalls = toolAccumulators.Values.Select(acc => new ApiToolCall(
                    acc.Id, "function",
                    new ApiFunctionCall(acc.Name, acc.Args.ToString()))).ToList();
                _history.Add(new ApiMessage("assistant", null, ToolCalls: apiToolCalls));

                // Execute each tool and collect results
                foreach (var acc in toolAccumulators.Values)
                {
                    yield return AiStreamEvent.ToolCall(acc.Name);
                    string result;
                    try
                    {
                        result = await executor.ExecuteAsync(acc.Name, acc.Args.ToString(), ct);
                    }
                    catch (Exception ex)
                    {
                        result = $"Tool error: {ex.Message}";
                    }
                    _history.Add(new ApiMessage("tool", result, ToolCallId: acc.Id, Name: acc.Name));
                    yield return AiStreamEvent.ToolResult(acc.Name, result);
                }

                continue; // Send next request with tool results
            }

            // Final response received
            if (contentBuilder.Length > 0)
                _history.Add(new ApiMessage("assistant", contentBuilder.ToString()));
            else if (finishReason != "tool_calls")
                _history.Add(new ApiMessage("assistant", string.Empty));

            yield return AiStreamEvent.Done();
            yield break;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    // ------------------------------------------------------------------ helpers

    private List<ApiMessage> BuildMessages()
    {
        var msgs = new List<ApiMessage>(_history.Count + 1)
        {
            new("system", SystemPrompt)
        };
        msgs.AddRange(_history);
        return msgs;
    }

    private async Task<HttpResponseMessage> PostStreamAsync(
        ApiRequest req, string endpoint, string apiKey, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(req, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var httpReq = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        const string suffix = "/chat/completions";
        return url.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? url : url + suffix;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : string.Concat(s.AsSpan(0, max), "…");

    // ------------------------------------------------------------------ accumulator

    private sealed class ToolCallAccumulator
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder Args { get; } = new();
    }
}

// ------------------------------------------------------------------ API JSON models (file-scoped)

internal sealed record ApiRequest(
    [property: JsonPropertyName("model")]      string Model,
    [property: JsonPropertyName("messages")]   List<ApiMessage> Messages,
    [property: JsonPropertyName("tools")]      List<JsonObject>? Tools,
    [property: JsonPropertyName("stream")]     bool Stream,
    [property: JsonPropertyName("max_tokens")] int MaxTokens);

internal sealed record ApiMessage(
    [property: JsonPropertyName("role")]         string Role,
    [property: JsonPropertyName("content")]      string? Content = null,
    [property: JsonPropertyName("tool_calls")]   List<ApiToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_call_id")] string? ToolCallId = null,
    [property: JsonPropertyName("name")]         string? Name = null);

internal sealed record ApiToolCall(
    [property: JsonPropertyName("id")]       string Id,
    [property: JsonPropertyName("type")]     string Type,
    [property: JsonPropertyName("function")] ApiFunctionCall Function);

internal sealed record ApiFunctionCall(
    [property: JsonPropertyName("name")]      string Name,
    [property: JsonPropertyName("arguments")] string Arguments);
