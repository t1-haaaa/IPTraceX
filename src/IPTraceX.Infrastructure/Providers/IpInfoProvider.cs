using System.Text.Json;
using System.Text.RegularExpressions;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Third provider: ipinfo.io (free anonymous tier, HTTPS, IPv4+IPv6).
/// Anonymous responses carry the country as a code only and the ASN
/// embedded in "org" ("AS15169 Google LLC"). Both are parsed here; the
/// consensus layer fills the full name from corroborating providers.
/// An optional token raises the quota (never logged, never cached).
/// </summary>
public sealed class IpInfoProvider : IGeoProvider
{
    public string Name => "ipinfo.io";
    public int[] SupportedIpVersions => [4, 6];

    private static readonly Regex AsnRe =
        new(@"\bAS\s?(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;
    private readonly string _token;

    public IpInfoProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null, string token = "")
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
        _token = token.Trim();
    }

    public async Task<GeoResult> LookupAsync(string ip, CancellationToken cancellationToken = default)
    {
        var validated = IpValidation.EnsurePublic(ip);
        Dictionary<string, string>? headers = _token.Length == 0
            ? null
            : new Dictionary<string, string> { ["Authorization"] = "Bearer " + _token };
        JsonElement? payload = await _fetcher.FetchAsync(
            $"https://ipinfo.io/{validated.Text}/json", _timeoutSeconds, headers, cancellationToken)
            .ConfigureAwait(false);
        if (payload is null)
        {
            throw new BadResponseException("Provider returned an empty response.");
        }

        return Normalize(validated.Text, payload.Value);
    }

    public static GeoResult Normalize(string ipText, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new BadResponseException("Provider returned an unexpected response shape.");
        }

        var (lat, lon) = SplitLoc(JsonFields.Str(payload, "loc"));
        var (asn, asName) = SplitOrg(JsonFields.Str(payload, "org"));
        string ipValue = JsonFields.Str(payload, "ip") ?? ipText;

        var result = new GeoResult
        {
            Ip = ipValue,
            IpVersion = ipValue.Contains(':') ? 6 : 4,
            GoogleMapsUrl = Maps.BuildMapsUrl(lat, lon),
            Source = "ipinfo.io",
        };
        result.Geolocation.Country = null; // code only on the anonymous tier
        result.Geolocation.CountryCode = JsonFields.Str(payload, "country");
        result.Geolocation.Region = JsonFields.Str(payload, "region");
        result.Geolocation.City = JsonFields.Str(payload, "city");
        result.Geolocation.PostalCode = JsonFields.Str(payload, "postal");
        result.Geolocation.Latitude = lat;
        result.Geolocation.Longitude = lon;
        result.Geolocation.Timezone = JsonFields.Str(payload, "timezone");
        result.Network.Isp = asName;
        result.Network.Organization = asName;
        result.Network.Asn = asn;
        result.Network.AsName = asName;
        result.Network.Hostname = JsonFields.Str(payload, "hostname");
        return result;
    }

    public static (string? Asn, string? Name) SplitOrg(string? org)
    {
        if (string.IsNullOrWhiteSpace(org))
        {
            return (null, null);
        }

        Match match = AsnRe.Match(org);
        string? asn = match.Success ? "AS" + match.Groups[1].Value : null;
        string name = AsnRe.Replace(org, "").Trim(' ', '\t', '-', ',', ';');
        if (name.Length == 0)
        {
            name = "";
        }

        string? result = name.Length == 0 ? null : name;
        if (result is not null && asn is not null
            && result.Equals(asn, StringComparison.OrdinalIgnoreCase))
        {
            result = null;
        }

        return (asn, result);
    }

    public static (double? Lat, double? Lon) SplitLoc(string? loc)
    {
        if (string.IsNullOrEmpty(loc) || !loc.Contains(','))
        {
            return (null, null);
        }

        string[] parts = loc.Split(',', 2);
        return (Maps.ToDouble(parts[0].Trim()), Maps.ToDouble(parts[1].Trim()));
    }
}
