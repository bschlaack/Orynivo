using System.Net;
using Orynivo.Web;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>
/// Verifies the SSRF address classification used by the web-browsing guard:
/// loopback, private, link-local, CGNAT, multicast, and reserved ranges are
/// refused while public addresses are permitted.
/// </summary>
public sealed class PrivateNetworkPolicyTests
{
    /// <summary>IPv4 ranges are classified correctly.</summary>
    /// <param name="ip">Address literal.</param>
    /// <param name="expected">Expected classification.</param>
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.255.255.255", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("0.1.2.3", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("10.255.255.255", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("100.63.255.255", false)]
    [InlineData("100.128.0.0", false)]
    [InlineData("169.254.169.254", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("172.15.255.255", false)]
    [InlineData("172.32.0.0", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("239.255.255.255", true)]
    [InlineData("223.255.255.255", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void IsPrivateOrReserved_ClassifiesIPv4(string ip, bool expected)
        => Assert.Equal(expected, PrivateNetworkPolicy.IsPrivateOrReserved(IPAddress.Parse(ip)));

    /// <summary>IPv6 ranges are classified correctly.</summary>
    /// <param name="ip">Address literal.</param>
    /// <param name="expected">Expected classification.</param>
    [Theory]
    [InlineData("::1", true)]
    [InlineData("::", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fec0::1", true)]
    [InlineData("ff02::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fd12:3456::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void IsPrivateOrReserved_ClassifiesIPv6(string ip, bool expected)
        => Assert.Equal(expected, PrivateNetworkPolicy.IsPrivateOrReserved(IPAddress.Parse(ip)));

    /// <summary>IPv4-mapped IPv6 addresses are classified by their IPv4 target.</summary>
    /// <param name="ip">Address literal.</param>
    /// <param name="expected">Expected classification.</param>
    [Theory]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("::ffff:10.0.0.1", true)]
    [InlineData("::ffff:8.8.8.8", false)]
    public void IsPrivateOrReserved_MapsIPv4MappedIPv6(string ip, bool expected)
        => Assert.Equal(expected, PrivateNetworkPolicy.IsPrivateOrReserved(IPAddress.Parse(ip)));
}
