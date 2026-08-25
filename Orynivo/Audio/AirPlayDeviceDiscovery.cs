using Zeroconf;

namespace Orynivo.Audio;

/// <summary>Discovers classic AirPlay/RAOP audio receivers through DNS-SD.</summary>
internal static class AirPlayDeviceDiscovery
{
    private const string RaopServiceType = "_raop._tcp.local.";
    private static readonly object CacheLock = new();
    private static IReadOnlyList<AirPlayDeviceInfo> _cachedDevices = [];
    private static DateTimeOffset _cacheExpiresAt;

    /// <summary>Discovers reachable RAOP receivers during a bounded multicast scan.</summary>
    /// <param name="forceRefresh">Whether to ignore the short-lived process cache.</param>
    /// <param name="cancellationToken">Cancels the discovery operation.</param>
    /// <returns>Distinct receivers ordered by display name.</returns>
    internal static async Task<IReadOnlyList<AirPlayDeviceInfo>> DiscoverAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        lock (CacheLock)
        {
            if (!forceRefresh && DateTimeOffset.UtcNow < _cacheExpiresAt)
                return _cachedDevices;
        }

        var hosts = await ZeroconfResolver.ResolveAsync(
            RaopServiceType,
            scanTime: TimeSpan.FromSeconds(3),
            retries: 1,
            retryDelayMilliseconds: 500,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var devices = new Dictionary<string, AirPlayDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in hosts)
        {
            foreach (var service in host.Services.Values.Where(static service => service.Port > 0))
            {
                var serviceName = service.ServiceName;
                var properties = FlattenProperties(service.Properties);
                if (properties.TryGetValue("pw", out var passwordRequired) &&
                    (passwordRequired.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                     passwordRequired.Equals("1", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var displayName = GetDisplayName(serviceName);
                var id = string.IsNullOrWhiteSpace(serviceName)
                    ? $"{host.IPAddress}:{service.Port}"
                    : serviceName;
                devices[id] = new AirPlayDeviceInfo(
                    id,
                    displayName,
                    host.IPAddress,
                    service.Port,
                    properties);
            }
        }

        var result = devices.Values
            .OrderBy(static device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        lock (CacheLock)
        {
            _cachedDevices = result;
            _cacheExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        }
        return result;
    }

    private static string GetDisplayName(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return "AirPlay";
        var instanceName = serviceName;
        var serviceSuffix = instanceName.IndexOf("._raop._tcp", StringComparison.OrdinalIgnoreCase);
        if (serviceSuffix > 0)
            instanceName = instanceName[..serviceSuffix];
        var separator = instanceName.IndexOf('@');
        if (separator >= 0 && separator + 1 < instanceName.Length)
            instanceName = instanceName[(separator + 1)..];
        return instanceName.Replace("\\.", ".", StringComparison.Ordinal).TrimEnd('.');
    }

    private static IReadOnlyDictionary<string, string> FlattenProperties(
        IReadOnlyList<IReadOnlyDictionary<string, string>> properties)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertySet in properties)
        foreach (var property in propertySet)
            result[property.Key] = property.Value;
        return result;
    }
}

/// <summary>Describes one AirPlay receiver discovered on the local network.</summary>
/// <param name="Id">Stable DNS-SD service identifier.</param>
/// <param name="Name">Receiver display name.</param>
/// <param name="Host">Resolved IPv4 or IPv6 address.</param>
/// <param name="Port">RAOP RTSP port.</param>
/// <param name="Properties">Advertised DNS-SD TXT properties.</param>
internal sealed record AirPlayDeviceInfo(
    string Id,
    string Name,
    string Host,
    int Port,
    IReadOnlyDictionary<string, string> Properties);
