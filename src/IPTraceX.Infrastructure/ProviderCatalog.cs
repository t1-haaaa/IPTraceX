using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Central provider catalog: every integrated source with its capability
/// metadata. The CLI, health views and docs all read from here — providers
/// are never hardcoded into the interface layer.
/// Scales to dozens of providers without structural changes.
/// </summary>
public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderDescriptor> All =
    [
        new("ipwho.is", "ipwho.is", ProviderCategory.Geo, [4, 6],
            false, "No key required.", "Free tier; 429 respected."),
        new("ipapi.co", "ipapi.co", ProviderCategory.Geo, [4, 6],
            false, "No key required.", "Free tier ~1000/day; 429 respected."),
        new("ipinfo.io", "IPinfo", ProviderCategory.Geo, [4, 6],
            false, "Optional IPINFO_TOKEN (higher quota).",
            "Anonymous tier throttled; 429 respected."),
        new("ripestat", "RIPEstat", ProviderCategory.Asn, [4, 6],
            false, "No key required.", "Fair use; tiny responses."),
        new("doh-cloudflare", "Cloudflare DoH", ProviderCategory.Dns, [4, 6],
            false, "No key required.", "Public resolver; fair use."),
        new("doh-google", "Google DoH", ProviderCategory.Dns, [4, 6],
            false, "No key required.", "Public resolver; fair use."),
        new("system-dns", "System DNS", ProviderCategory.Dns, [4, 6],
            false, "No key required; uses the machine resolver.", "Local resolver limits apply."),
        new("tor-exits", "Tor Exits", ProviderCategory.Security, [4, 6],
            false, "No key required.", "One small list, cached 6 hours."),
        new("cloud-ranges", "Cloud Ranges", ProviderCategory.Cloud, [4, 6],
            false, "No key required.", "Official feeds, cached 24 hours."),
        new("abuseipdb", "AbuseIPDB", ProviderCategory.Reputation, [4],
            true, "Requires IPTraceX_ABUSEIPDB_KEY (free tier available).",
            "Free tier: 1000 lookups/day."),
    ];

    public static ProviderDescriptor? Find(string id)
        => All.FirstOrDefault(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static readonly Dictionary<string, string> ReliabilityTiers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["tor-exits"] = ReliabilityTier.High,
            ["cloud-ranges"] = ReliabilityTier.High,
            ["ipwho.is"] = ReliabilityTier.Medium,
            ["ipapi.co"] = ReliabilityTier.Medium,
            ["ipinfo.io"] = ReliabilityTier.Medium,
            ["ripestat"] = ReliabilityTier.Medium,
            ["doh-cloudflare"] = ReliabilityTier.Medium,
            ["doh-google"] = ReliabilityTier.Medium,
            ["system-dns"] = ReliabilityTier.Medium,
            ["abuseipdb"] = ReliabilityTier.Medium,
        };

    /// <summary>
    /// Documented reliability tier. Displayed next to evidence; used only
    /// to break exact vote ties (stable sort), never to erase disagreement.
    /// </summary>
    public static string ReliabilityOf(string id)
        => ReliabilityTiers.TryGetValue(id, out string? tier) ? tier : ReliabilityTier.Unknown;

    public static int ReliabilityRank(string id)
        => ReliabilityOf(id) switch
        {
            ReliabilityTier.High => 0,
            ReliabilityTier.Medium => 1,
            ReliabilityTier.Low => 2,
            _ => 3,
        };
}
