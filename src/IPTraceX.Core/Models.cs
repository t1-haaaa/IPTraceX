namespace IPTraceX.Core;

/// <summary>Normalized geolocation. Only provider adapters populate this.</summary>
public sealed class Geolocation
{
    public string? Country { get; set; }
    public string? CountryCode { get; set; }
    public string? Region { get; set; }
    public string? RegionCode { get; set; }
    public string? City { get; set; }
    public string? PostalCode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Timezone { get; set; }
}

/// <summary>Normalized network intelligence.</summary>
public sealed class NetworkInfo
{
    public string? Isp { get; set; }
    public string? Organization { get; set; }
    public string? Asn { get; set; }
    public string? AsName { get; set; }
    public string? Hostname { get; set; }
}

/// <summary>One provider's part in a multi-provider lookup (JSON-safe).</summary>
public sealed class ProviderDetail
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "skipped"; // success | failed | skipped
    public string? Error { get; set; }
    public string Summary { get; set; } = ""; // e.g. "Algeria / Tindouf"
}

/// <summary>
/// Normalized result. Data model shared by every layer; provider JSON
/// never leaks past the adapter that produced it.
/// </summary>
public sealed class GeoResult
{
    public string Ip { get; set; } = "";
    public int IpVersion { get; set; } = 4;
    public Geolocation Geolocation { get; } = new();
    public NetworkInfo Network { get; } = new();
    public string? GoogleMapsUrl { get; set; }

    // Multi-provider consensus quality (additive).
    public string Source { get; set; } = "ipwho.is";
    public string Confidence { get; set; } = "unknown"; // high|medium|low|unknown
    public int ProvidersQueried { get; set; }
    public int ProvidersSuccessful { get; set; }
    public int ProvidersAgreeing { get; set; }
    public double AgreementRatio { get; set; }
    public bool Disputed { get; set; }
    public List<ProviderDetail> Providers { get; } = [];
}
