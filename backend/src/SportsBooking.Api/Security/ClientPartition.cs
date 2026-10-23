using System.Net;
using System.Net.Sockets;

namespace SportsBooking.Api.Security;

/// <summary>The rate-limit partition of a client address (after trusted forwarded headers were applied).</summary>
internal static class ClientPartition
{
    private const int IPv6PrefixLength = 64;

    /// <summary>
    /// IPv4 by address. IPv6 by its /64 network, the usual allocation to a single customer, so one client cannot
    /// get a fresh limit by rotating addresses inside it. No address (not a network connection) is one shared partition.
    /// </summary>
    public static string For(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, IPv6PrefixLength / 8, bytes.Length - (IPv6PrefixLength / 8));
        return $"{new IPAddress(bytes)}/{IPv6PrefixLength}";
    }
}
