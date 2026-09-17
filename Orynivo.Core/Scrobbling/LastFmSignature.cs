using System.Security.Cryptography;
using System.Text;

namespace Orynivo.Scrobbling;

/// <summary>
/// Computes Last.fm API request signatures.
/// </summary>
/// <remarks>
/// The signature is the MD5 hash of the alphabetically ordered
/// <c>name + value</c> concatenation of every request parameter, followed by the
/// API secret, rendered as lowercase hexadecimal. The <c>format</c> and
/// <c>callback</c> parameters must not be included.
/// </remarks>
public static class LastFmSignature
{
    /// <summary>
    /// Computes the API signature for the supplied parameters.
    /// </summary>
    /// <param name="parameters">Request parameters, excluding <c>format</c> and <c>callback</c>.</param>
    /// <param name="apiSecret">The account's Last.fm API secret.</param>
    /// <returns>The lowercase hexadecimal MD5 signature.</returns>
    public static string Compute(IReadOnlyDictionary<string, string> parameters, string apiSecret)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(apiSecret);

        var builder = new StringBuilder();
        foreach (var pair in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append(pair.Value);
        }

        builder.Append(apiSecret);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
