using System.Net;
using System.Net.Sockets;

namespace NeuralDamage.Infrastructure.Services.WebImages;

/// <summary>
/// Whether an address is somewhere on the public internet, which is the only
/// place the server fetches web pictures from: never itself, the network it
/// sits in, the cloud's metadata service or any other special-purpose range.
/// IPv4 inside IPv6 (mapped, 6to4, NAT64, IPv4-compatible) is judged as the
/// IPv4 address it carries.
/// </summary>
public static class PublicAddress
{
    private static readonly (IPAddress Network, int Prefix)[] BlockedV4 =
    [
        (IPAddress.Parse("0.0.0.0"), 8),        // "this network"
        (IPAddress.Parse("10.0.0.0"), 8),       // private
        (IPAddress.Parse("100.64.0.0"), 10),    // carrier-grade NAT
        (IPAddress.Parse("127.0.0.0"), 8),      // loopback
        (IPAddress.Parse("169.254.0.0"), 16),   // link-local, cloud metadata
        (IPAddress.Parse("172.16.0.0"), 12),    // private
        (IPAddress.Parse("192.0.0.0"), 24),     // IETF protocol assignments
        (IPAddress.Parse("192.0.2.0"), 24),     // documentation
        (IPAddress.Parse("192.88.99.0"), 24),   // 6to4 relay anycast
        (IPAddress.Parse("192.168.0.0"), 16),   // private
        (IPAddress.Parse("198.18.0.0"), 15),    // benchmarking
        (IPAddress.Parse("198.51.100.0"), 24),  // documentation
        (IPAddress.Parse("203.0.113.0"), 24),   // documentation
        (IPAddress.Parse("224.0.0.0"), 4),      // multicast
        (IPAddress.Parse("240.0.0.0"), 4),      // reserved, broadcast
    ];

    private static readonly (IPAddress Network, int Prefix)[] BlockedV6 =
    [
        (IPAddress.Parse("::"), 128),           // unspecified
        (IPAddress.Parse("::1"), 128),          // loopback
        (IPAddress.Parse("100::"), 64),         // discard
        (IPAddress.Parse("2001::"), 32),        // Teredo
        (IPAddress.Parse("2001:db8::"), 32),    // documentation
        (IPAddress.Parse("fc00::"), 7),         // unique local
        (IPAddress.Parse("fe80::"), 10),        // link-local
        (IPAddress.Parse("fec0::"), 10),        // site-local
        (IPAddress.Parse("ff00::"), 8),         // multicast
    ];

    public static bool IsPublic(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (Embedded(address) is { } v4)
                return IsPublic(v4);
            return address.ScopeId == 0 && !BlockedV6.Any(b => InRange(address, b.Network, b.Prefix));
        }
        return address.AddressFamily == AddressFamily.InterNetwork && !BlockedV4.Any(b => InRange(address, b.Network, b.Prefix));
    }

    /// <summary>The IPv4 address an IPv6 one carries: mapped (::ffff:a.b.c.d), compatible (::a.b.c.d), 6to4 (2002::/16) or NAT64 (64:ff9b::/96).</summary>
    private static IPAddress? Embedded(IPAddress v6)
    {
        if (v6.IsIPv4MappedToIPv6)
            return v6.MapToIPv4();
        var bytes = v6.GetAddressBytes();
        if (bytes.Take(12).All(b => b == 0) && !(bytes[12] == 0 && bytes[13] == 0 && bytes[14] == 0 && bytes[15] <= 1))
            return new IPAddress(bytes[12..16]);
        if (bytes[0] == 0x20 && bytes[1] == 0x02)
            return new IPAddress(bytes[2..6]);
        if (InRange(v6, IPAddress.Parse("64:ff9b::"), 96) || InRange(v6, IPAddress.Parse("64:ff9b:1::"), 48))
            return new IPAddress(bytes[12..16]);
        return null;
    }

    private static bool InRange(IPAddress address, IPAddress network, int prefix)
    {
        if (address.AddressFamily != network.AddressFamily)
            return false;
        var a = address.GetAddressBytes();
        var n = network.GetAddressBytes();
        var full = prefix / 8;
        for (var i = 0; i < full; i++)
            if (a[i] != n[i])
                return false;
        var rest = prefix % 8;
        if (rest == 0)
            return true;
        var mask = (byte)(0xFF << (8 - rest));
        return (a[full] & mask) == (n[full] & mask);
    }
}
