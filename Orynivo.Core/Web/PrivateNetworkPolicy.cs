using System.Net;
using System.Net.Sockets;

namespace Orynivo.Web;

/// <summary>
/// Classifies IP addresses that the web-browsing SSRF guard must refuse. The
/// policy is applied at connect time so DNS rebinding cannot bypass it.
/// </summary>
internal static class PrivateNetworkPolicy
{
    /// <summary>
    /// Determines whether an IP address is loopback, private, link-local, or
    /// otherwise reserved and therefore must not be reached by fetches.
    /// </summary>
    /// <param name="address">The address to classify.</param>
    /// <returns><see langword="true"/> when the address must not be reached.</returns>
    internal static bool IsPrivateOrReserved(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 0                                        // 0.0.0.0/8
                || b[0] == 10                                       // 10.0.0.0/8 private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)       // 100.64.0.0/10 CGNAT
                || b[0] == 127                                      // 127.0.0.0/8 loopback
                || (b[0] == 169 && b[1] == 254)                     // 169.254.0.0/16 link-local (incl. cloud metadata)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)        // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 168)                     // 192.168.0.0/16 private
                || b[0] >= 224;                                     // 224.0.0.0/4 multicast + reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
                return true;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC)                              // fc00::/7 unique local
                return true;
            if (address.Equals(IPAddress.IPv6Any))                 // ::
                return true;
        }

        return false;
    }
}
