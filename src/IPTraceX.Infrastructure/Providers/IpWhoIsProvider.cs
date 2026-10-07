using System.Text.Json;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Primary provider: ipwho.is (free, HTTPS, no key, IPv4+IPv6).</summary>
public sealed class IpWhoIsProvider : IGeoProvider
{
    public string Name => "ipwho.is";
    public int[] SupportedIpVersions => [4, 6];

    private readonly double _timeoutSeconds;
    private readonly IGeoJsonFetcher _fetcher;

    public IpWhoIsProvider(double timeoutSeconds = 10.0, IGeoJsonFetcher? fetcher = null)
    {
        _timeoutSeconds = timeoutSeconds;
        _fetcher = fetcher ?? new HttpJsonClient();
    }

    public async Task<GeoResult> LookupAsync(string ip, CancellationToken cancellationToken = default)
    {
        var validated = IpValidation.EnsurePublic(ip);
        JsonElement? payload = await _fetcher.FetchAsync(
            $"https://ipwho.is/{validated.Text}", _timeoutSeconds, null, cancellationToken)
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

        if (payload.TryGetProperty("success", out JsonElement success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new NotFoundException(
                JsonFields.Str(payload, "message") ?? "Provider has no data for this IP.");
        }

        string? timezone = null;
        if (payload.TryGetProperty("timezone", out JsonElement timezoneEl))
        {
            timezone = timezoneEl.ValueKind == JsonValueKind.Object
                ? JsonFields.Str(timezoneEl, "id")
                : timezoneEl.ValueKind == JsonValueKind.String
                    ? JsonFields.BlankToNull(timezoneEl.GetString())
                    : null;
        }

        string? asn = null;
        string? isp = null;
        string? org = null;
        string? hostname = null;
        if (payload.TryGetProperty("connection", out JsonElement connection)
            && connection.ValueKind == JsonValueKind.Object)
        {
            isp = JsonFields.Str(connection, "isp");
            org = JsonFields.Str(connection, "org");
            hostname = JsonFields.Str(connection, "domain");
            if (connection.TryGetProperty("asn", out JsonElement asnEl))
            {
                asn = asnEl.ValueKind switch
                {
                    JsonValueKind.Number => "AS" + asnEl.GetRawText(),
                    JsonValueKind.String => JsonFields.BlankToNull(asnEl.GetString()),
                    _ => null,
                };
            }
        }

        double? lat = JsonFields.Num(payload, "latitude");
        double? lon = JsonFields.Num(payload, "longitude");

        string ipValue = JsonFields.Str(payload, "ip") ?? ipText;
        string typeValue = JsonFields.Str(payload, "type") ?? "";
        int version = typeValue == "IPv4" ? 4 : typeValue == "IPv6" ? 6 : ipValue.Contains(':') ? 6 : 4;

        var result = new GeoResult
        {
            Ip = ipValue,
            IpVersion = version,
            GoogleMapsUrl = Maps.BuildMapsUrl(lat, lon),
            Source = "ipwho.is",
        };
        result.Geolocation.Country = JsonFields.Str(payload, "country");
        result.Geolocation.CountryCode = JsonFields.Str(payload, "country_code");
        result.Geolocation.Region = JsonFields.Str(payload, "region");
        result.Geolocation.RegionCode = JsonFields.Str(payload, "region_code");
        result.Geolocation.City = JsonFields.Str(payload, "city");
        result.Geolocation.PostalCode = JsonFields.Str(payload, "postal");
        result.Geolocation.Latitude = lat;
        result.Geolocation.Longitude = lon;
        result.Geolocation.Timezone = timezone;
        result.Network.Isp = isp;
        result.Network.Organization = org;
        result.Network.Asn = asn;
        result.Network.Hostname = hostname;
        return result;
    }
}
