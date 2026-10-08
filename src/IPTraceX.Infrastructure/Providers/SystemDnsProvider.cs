using System.Net;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// DNS via the system resolver (no external API, no key, no ToS surface).
/// Per-IP: reverse lookup. Domains: A/AAAA enumeration for domain mode.
/// </summary>
public sealed class SystemDnsProvider : IIntelProvider
{
    public ProviderDescriptor Descriptor { get; } = new(
        "system-dns",
        "System DNS",
        ProviderCategory.Dns,
        [4, 6],
        false,
        "No key required; uses the machine resolver.",
        "Local resolver limits apply.");

    public async Task<IntelEvidence> InvestigateAsync(
        string ip, ProviderContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var validated = IpValidation.EnsurePublic(ip);
            IPHostEntry entry = await Dns.GetHostEntryAsync(validated.Text, cancellationToken)
                .ConfigureAwait(false);
            string host = (entry.HostName ?? "").Trim().TrimEnd('.');
            // A resolver echo of the literal IP is not a real PTR record.
            if (host.Length == 0 || string.Equals(host, validated.Text, StringComparison.OrdinalIgnoreCase))
            {
                return new DnsEvidence(Descriptor.Id, true, null, []);
            }

            return new DnsEvidence(Descriptor.Id, true, null, [host]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // NXDOMAIN / no PTR is a normal negative answer, not a failure.
            return new DnsEvidence(Descriptor.Id, true, null, []);
        }
    }

    /// <summary>Resolve a domain to deduplicated public A/AAAA literals.</summary>
    public static async Task<DomainEvidence> ResolveDomainAsync(
        string domain, CancellationToken cancellationToken = default)
    {
        string clean = (domain ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        if (clean.Length == 0 || clean.Length > 253 || clean.Contains(' ') || clean.Contains('/'))
        {
            throw new InvalidIpException("Invalid domain name.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(clean, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DomainEvidence("system-dns", false, "Domain did not resolve.", clean, [], []);
        }

        var v4 = new List<string>();
        var v6 = new List<string>();
        foreach (IPAddress address in addresses)
        {
            string text = address.ToString();
            try
            {
                var parsed = IpValidation.EnsurePublic(text);
                if (parsed.Version == 4 && !v4.Contains(parsed.Text, StringComparer.Ordinal))
                {
                    v4.Add(parsed.Text);
                }
                else if (parsed.Version == 6 && !v6.Contains(parsed.Text, StringComparer.Ordinal))
                {
                    v6.Add(parsed.Text);
                }
            }
            catch (InvalidIpException)
            {
                // Non-public resolved addresses are ignored, never analyzed.
            }
        }

        return new DomainEvidence("system-dns", true, null, clean, [.. v4], [.. v6]);
    }
}
