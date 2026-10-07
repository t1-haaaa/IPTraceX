using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace IPTraceX.Core;

/// <summary>
/// IP validation on top of <see cref="IPAddress"/> (parity with the former
/// Python implementation, which used the stdlib <c>ipaddress</c> module).
/// Public-only policy: private/loopback/link-local/multicast/reserved/
/// unspecified addresses are rejected with a reason and never sent anywhere.
/// </summary>
public static class IpValidation
{
    private static readonly UnicodeCategory[] EdgeStripCategories =
    [
        UnicodeCategory.Control,
        UnicodeCategory.Format,
        UnicodeCategory.Surrogate,
        UnicodeCategory.LineSeparator,
        UnicodeCategory.ParagraphSeparator,
    ];

    private const char ReplacementChar = '\ufffd'; // U+FFFD

    /// <summary>
    /// Remove surrounding whitespace/invisibles. Edges only -- interior
    /// content is returned verbatim so invalid input can never silently
    /// become valid.
    /// </summary>
    public static string CleanIpText(object? raw)
    {
        if (raw is null)
        {
            return "";
        }

        if (raw is not string text)
        {
            throw new InvalidIpException("Invalid IP address.");
        }

        text = text.Trim().Trim('[', ']', ',').Trim();
        int start = 0, end = text.Length;
        while (start < end && IsEdgeNoise(text[start]))
        {
            start++;
        }

        while (end > start && IsEdgeNoise(text[end - 1]))
        {
            end--;
        }

        return text[start..end].Trim();
    }

    private static bool IsEdgeNoise(char c)
    {
        if (c == ReplacementChar)
        {
            return true;
        }

        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return Array.IndexOf(EdgeStripCategories, cat) >= 0;
    }

    public static (IPAddress Address, int Version, string Text) ParseIp(object? raw)
    {
        if (raw is not null && raw is not string)
        {
            throw new InvalidIpException("Invalid IP address.");
        }

        string text = CleanIpText(raw);
        if (text.Length == 0)
        {
            throw new InvalidIpException("Empty IP address.");
        }

        // Reject CIDR, URLs, hostnames early.
        if (text.Contains('/') || text.Contains(' '))
        {
            throw new InvalidIpException("Invalid IP address.");
        }

        if (!IPAddress.TryParse(text, out IPAddress? address) || address is null)
        {
            throw new InvalidIpException("Invalid IP address.");
        }

        // IPAddress.TryParse accepts zone ids ("fe80::1%eth0"); reject those.
        if (text.Contains('%'))
        {
            throw new InvalidIpException("Invalid IP address.");
        }

        int version = address.AddressFamily == AddressFamily.InterNetworkV6 ? 6 : 4;
        return (address, version, address.ToString());
    }

    /// <summary>Human reason when <paramref name="address"/> is NOT public, else null.</summary>
    public static string? PublicRoutabilityReason(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return "loopback address";
        }

        IPAddress effective = address;
        if (address.IsIPv4MappedToIPv6)
        {
            effective = address.MapToIPv4();
        }

        if (IsPrivate(effective))
        {
            return "private address";
        }

        if (IsLinkLocal(effective))
        {
            return "link-local address";
        }

        if (IsMulticast(effective))
        {
            return "multicast address";
        }

        if (IsReserved(effective))
        {
            return "reserved address";
        }

        if (IsUnspecified(effective))
        {
            return "unspecified address";
        }

        return null;
    }

    public static (IPAddress Address, int Version, string Text) EnsurePublic(object? raw)
    {
        var parsed = ParseIp(raw);
        string? reason = PublicRoutabilityReason(parsed.Address);
        if (reason is not null)
        {
            throw new NonPublicIpException($"{parsed.Text} is not a public routable IP ({reason}).");
        }

        return parsed;
    }

    private static byte[] Bytes(IPAddress address) => address.GetAddressBytes();

    private static bool InCidr(IPAddress address, string network, int prefix)
    {
        byte[] addr = Bytes(address);
        byte[] net = IPAddress.Parse(network).GetAddressBytes();
        if (addr.Length != net.Length)
        {
            return false;
        }

        int fullBytes = prefix / 8;
        int restBits = prefix % 8;
        for (int i = 0; i < fullBytes; i++)
        {
            if (addr[i] != net[i])
            {
                return false;
            }
        }

        if (restBits > 0)
        {
            byte mask = (byte)(0xFF << (8 - restBits));
            if ((addr[fullBytes] & mask) != (net[fullBytes] & mask))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsV4(IPAddress a) => a.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsPrivate(IPAddress a)
    {
        if (IsV4(a))
        {
            return InCidr(a, "10.0.0.0", 8)
                || InCidr(a, "172.16.0.0", 12)
                || InCidr(a, "192.168.0.0", 16)
                || InCidr(a, "100.64.0.0", 10)
                || InCidr(a, "198.18.0.0", 15);
        }

        return InCidr(a, "fc00::", 7);
    }

    private static bool IsLinkLocal(IPAddress a)
    {
        if (IsV4(a))
        {
            return InCidr(a, "169.254.0.0", 16);
        }

        return InCidr(a, "fe80::", 10);
    }

    private static bool IsMulticast(IPAddress a)
    {
        if (IsV4(a))
        {
            return InCidr(a, "224.0.0.0", 4);
        }

        return InCidr(a, "ff00::", 8);
    }

    private static bool IsReserved(IPAddress a)
    {
        if (IsV4(a))
        {
            return InCidr(a, "240.0.0.0", 4);
        }

        return false;
    }

    private static bool IsUnspecified(IPAddress a)
    {
        byte[] b = Bytes(a);
        foreach (byte x in b)
        {
            if (x != 0)
            {
                return false;
            }
        }

        return true;
    }
}
