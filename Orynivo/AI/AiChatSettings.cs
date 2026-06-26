namespace Orynivo.AI;

/// <summary>
/// Settings for the embedded AI chat, persisted as part of <see cref="AppSettings"/>.
/// </summary>
public sealed class AiChatSettings
{
    /// <summary>Gets or sets a value indicating whether the AI chat feature is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the base URL of the OpenAI-compatible API endpoint (e.g. <c>http://localhost:1234/v1</c>).</summary>
    public string EndpointUrl { get; set; } = "http://localhost:1234/v1";

    /// <summary>Gets or sets the API key. Leave empty when using local models that do not require authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the model identifier sent with each chat request.</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Gets or sets the maximum number of tokens the model may generate per response.</summary>
    public int MaxTokens { get; set; } = 2048;
}
