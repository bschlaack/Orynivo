namespace Orynivo.Visualization;

/// <summary>
/// A shader sampler name split into the texture it reads and the way it reads it. Milkdrop qualifies
/// a sampler with a three-letter prefix on the base name: <c>fc_</c> is filtered (bilinear) and
/// clamped, <c>fw_</c> is filtered and wrapped, <c>pc_</c> is point (nearest) and clamped, and
/// <c>pw_</c> is point and wrapped. The letters may also be swapped (<c>cf_</c>, <c>wf_</c>,
/// <c>cp_</c>, <c>wp_</c>). Any other three-character prefix followed by an underscore is removed and
/// leaves the filtered-and-wrapped default. The prefix changes only the sampling mode, never which
/// moment in time the texture holds, so <c>sampler_main</c>, <c>sampler_pc_main</c>, and
/// <c>sampler_fw_main</c> all read the same frame.
/// </summary>
public readonly struct ShaderSamplerName
{
    /// <summary>Creates a parsed sampler name.</summary>
    /// <param name="baseName">Texture name without the qualifier, for example <c>main</c>.</param>
    /// <param name="wrap">Whether coordinates outside the texture repeat or clamp.</param>
    /// <param name="nearest">Whether the sampler reads the nearest texel instead of filtering.</param>
    public ShaderSamplerName(string baseName, VisualizerTextureWrap wrap, bool nearest)
    {
        BaseName = baseName;
        Wrap = wrap;
        Nearest = nearest;
    }

    /// <summary>Gets the texture name without the qualifier, for example <c>main</c>.</summary>
    public string BaseName { get; }

    /// <summary>Gets whether coordinates outside the texture repeat or clamp.</summary>
    public VisualizerTextureWrap Wrap { get; }

    /// <summary>Gets whether the sampler reads the nearest texel instead of filtering.</summary>
    public bool Nearest { get; }

    /// <summary>
    /// Splits a full sampler name such as <c>sampler_pw_main</c> into its base texture and sampling
    /// mode. A leading <c>sampler_</c> is optional.
    /// </summary>
    /// <param name="name">Full sampler name.</param>
    /// <returns>The parsed name.</returns>
    public static ShaderSamplerName Parse(string name)
    {
        var rest = name;
        if (rest.StartsWith("sampler_", StringComparison.OrdinalIgnoreCase))
            rest = rest[8..];

        if (rest.Length <= 3 || rest[2] != '_')
            return new ShaderSamplerName(rest, VisualizerTextureWrap.Repeat, nearest: false);

        var prefix = rest[..3].ToLowerInvariant();
        var baseName = rest[3..];
        return prefix switch
        {
            "fc_" or "cf_" => new ShaderSamplerName(baseName, VisualizerTextureWrap.Clamp, nearest: false),
            "fw_" or "wf_" => new ShaderSamplerName(baseName, VisualizerTextureWrap.Repeat, nearest: false),
            "pc_" or "cp_" => new ShaderSamplerName(baseName, VisualizerTextureWrap.Clamp, nearest: true),
            "pw_" or "wp_" => new ShaderSamplerName(baseName, VisualizerTextureWrap.Repeat, nearest: true),
            _ => new ShaderSamplerName(baseName, VisualizerTextureWrap.Repeat, nearest: false)
        };
    }
}
