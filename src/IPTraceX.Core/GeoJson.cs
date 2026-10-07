using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IPTraceX.Core;

/// <summary>
/// Exact JSON contract (snake_case keys, nulls preserved, no ANSI ever).
/// Old keys are frozen; new keys are additive only.
/// Implemented over DTO records so nulls always serialize explicitly.
/// </summary>
public static class GeoJson
{
    private sealed record GeolocationDto(
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("region")] string? Region,
        [property: JsonPropertyName("region_code")] string? RegionCode,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("postal_code")] string? PostalCode,
        [property: JsonPropertyName("latitude")] double? Latitude,
        [property: JsonPropertyName("longitude")] double? Longitude,
        [property: JsonPropertyName("timezone")] string? Timezone);

    private sealed record NetworkDto(
        [property: JsonPropertyName("isp")] string? Isp,
        [property: JsonPropertyName("organization")] string? Organization,
        [property: JsonPropertyName("asn")] string? Asn,
        [property: JsonPropertyName("as_name")] string? AsName,
        [property: JsonPropertyName("hostname")] string? Hostname);

    private sealed record QualityDto(
        [property: JsonPropertyName("providers_queried")] int Queried,
        [property: JsonPropertyName("providers_successful")] int Successful,
        [property: JsonPropertyName("providers_agreeing")] int Agreeing,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("agreement_ratio")] double Ratio,
        [property: JsonPropertyName("disputed")] bool Disputed,
        [property: JsonPropertyName("source")] string Source);

    private sealed record ProviderDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("summary")] string Summary);

    private sealed record ResultDto(
        [property: JsonPropertyName("ip")] string Ip,
        [property: JsonPropertyName("ip_version")] int IpVersion,
        [property: JsonPropertyName("geolocation")] GeolocationDto Geolocation,
        [property: JsonPropertyName("network")] NetworkDto Network,
        [property: JsonPropertyName("google_maps_url")] string? MapsUrl,
        [property: JsonPropertyName("geoip_quality")] QualityDto Quality,
        [property: JsonPropertyName("providers")] List<ProviderDto> Providers);

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions RelaxedIndented = new(Relaxed)
    {
        WriteIndented = true,
    };

    private static ResultDto ToDto(GeoResult info)
    {
        var g = info.Geolocation;
        var n = info.Network;
        return new ResultDto(
            info.Ip,
            info.IpVersion,
            new GeolocationDto(
                g.Country, g.CountryCode, g.Region, g.RegionCode, g.City,
                g.PostalCode, g.Latitude, g.Longitude, g.Timezone),
            new NetworkDto(n.Isp, n.Organization, n.Asn, n.AsName, n.Hostname),
            info.GoogleMapsUrl
                ?? Maps.BuildMapsUrl(g.Latitude, g.Longitude),
            new QualityDto(
                info.ProvidersQueried, info.ProvidersSuccessful,
                info.ProvidersAgreeing, info.Confidence, info.AgreementRatio,
                info.Disputed, info.Source),
            info.Providers
                .Select(d => new ProviderDto(d.Name, d.Status, d.Error, d.Summary))
                .ToList());
    }

    public static string ToJsonString(GeoResult info, bool indented = false)
        => JsonSerializer.Serialize(ToDto(info), indented ? RelaxedIndented : Relaxed);

    public static JsonObject FromResult(GeoResult info)
        => JsonNode.Parse(ToJsonString(info))!.AsObject();
}
