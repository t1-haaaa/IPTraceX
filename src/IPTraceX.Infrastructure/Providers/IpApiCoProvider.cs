using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Second provider: ipapi.co (free, HTTPS, no key, IPv4+IPv6).</summary>
public sealed class IpApiCoProvider : IGeoProvider
{
    public string Name => "ipapi.co";
    public int[] SupportedIpVersions => [4, 6];

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public IpApiCoProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<GeoResult> LookupAsync(string ip, CancellationToken cancellationToken = default)
    {
        var validated = IpValidation.EnsurePublic(ip);
        JsonElement? payload = await _fetcher.FetchAsync(
            $"https://ipapi.co/{validated.Text}/json/", _timeoutSeconds, null, cancellationToken)
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

        if (payload.TryGetProperty("error", out JsonElement error)
            && error.ValueKind == JsonValueKind.True)
        {
            string reason = JsonFields.Str(payload, "reason") ?? "Provider reported an error.";
            if (reason.Contains("throttle", StringComparison.OrdinalIgnoreCase)
                || reason.Contains("rate", StringComparison.OrdinalIgnoreCase))
            {
                throw new RateLimitException(
                    "API rate limit reached. Please wait and try again later.");
            }

            throw new BadResponseException($"Provider error: {reason}");
        }

        double? lat = JsonFields.Num(payload, "latitude");
        double? lon = JsonFields.Num(payload, "longitude");
        string ipValue = JsonFields.Str(payload, "ip") ?? ipText;
        string? org = JsonFields.Str(payload, "org");

        var result = new GeoResult
        {
            Ip = ipValue,
            IpVersion = ipValue.Contains(':') ? 6 : 4,
            GoogleMapsUrl = Maps.BuildMapsUrl(lat, lon),
            Source = "ipapi.co",
        };
        result.Geolocation.Country = JsonFields.Str(payload, "country_name");
        result.Geolocation.CountryCode = JsonFields.Str(payload, "country_code");
        result.Geolocation.Region = JsonFields.Str(payload, "region");
        result.Geolocation.RegionCode = JsonFields.Str(payload, "region_code");
        result.Geolocation.City = JsonFields.Str(payload, "city");
        result.Geolocation.PostalCode = JsonFields.Str(payload, "postal");
        result.Geolocation.Latitude = lat;
        result.Geolocation.Longitude = lon;
        result.Geolocation.Timezone = JsonFields.Str(payload, "timezone");
        result.Network.Isp = org;
        result.Network.Organization = org;
        result.Network.Asn = JsonFields.Str(payload, "asn");
        return result;
    }
}
