namespace Orynivo.Server.Endpoints;

/// <summary>
/// Describes an optional lossy transcode requested for a stream so bandwidth-limited
/// clients (for example the mobile web remote) can avoid downloading the original.
/// </summary>
/// <param name="Format">Normalized target format (<c>opus</c> or <c>aac</c>).</param>
/// <param name="BitrateKbps">Target bitrate in kilobits per second.</param>
public sealed record StreamTranscodeOptions(string Format, int BitrateKbps)
{
    /// <summary>The lowest accepted bitrate in kilobits per second.</summary>
    public const int MinimumBitrateKbps = 64;

    /// <summary>The highest accepted bitrate in kilobits per second.</summary>
    public const int MaximumBitrateKbps = 320;

    /// <summary>Gets the response content type for the selected format.</summary>
    public string ContentType => Format switch
    {
        "opus" => "audio/ogg",
        "aac" => "audio/aac",
        _ => "application/octet-stream"
    };

    /// <summary>Gets the FFmpeg output codec and container arguments.</summary>
    public string FfmpegOutputArguments => Format switch
    {
        "opus" => $"-c:a libopus -b:a {BitrateKbps}k -f ogg",
        "aac" => $"-c:a aac -b:a {BitrateKbps}k -f adts",
        _ => "-c:a flac -f flac"
    };

    /// <summary>
    /// Validates a requested format and bitrate. An absent bitrate falls back to a
    /// per-format default.
    /// </summary>
    /// <param name="format">Requested format name, or <see langword="null"/>.</param>
    /// <param name="bitrateKbps">Requested bitrate in kilobits per second, or <see langword="null"/>.</param>
    /// <param name="options">Receives the validated options.</param>
    /// <returns><see langword="true"/> when the request is supported.</returns>
    public static bool TryParse(string? format, int? bitrateKbps, out StreamTranscodeOptions? options)
    {
        options = null;
        if (string.IsNullOrWhiteSpace(format))
            return false;

        var normalized = format.Trim().ToLowerInvariant();
        if (normalized is not ("opus" or "aac"))
            return false;

        var bitrate = bitrateKbps ?? (normalized == "opus" ? 128 : 192);
        if (bitrate < MinimumBitrateKbps || bitrate > MaximumBitrateKbps)
            return false;

        options = new StreamTranscodeOptions(normalized, bitrate);
        return true;
    }
}
