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

    // ---- Full intelligence profile (legacy keys preserved verbatim) ----

    private sealed record AnonSignalDto(string Status, string Confidence, string[] Sources, string Evidence);

    private sealed record AnonymityDto(
        [property: JsonPropertyName("tor")] AnonSignalDto Tor,
        [property: JsonPropertyName("vpn")] AnonSignalDto Vpn,
        [property: JsonPropertyName("proxy")] AnonSignalDto Proxy,
        [property: JsonPropertyName("hosting")] AnonSignalDto Hosting);

    private sealed record AsnDto(
        [property: JsonPropertyName("asn")] string? Asn,
        [property: JsonPropertyName("organization")] string? Organization,
        [property: JsonPropertyName("network_name")] string? NetworkName,
        [property: JsonPropertyName("prefix")] string? Prefix,
        [property: JsonPropertyName("registry")] string? Registry,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("holder")] string? Holder,
        [property: JsonPropertyName("sources")] string[] Sources);

    private sealed record DnsDto(
        [property: JsonPropertyName("ip")] string Ip,
        [property: JsonPropertyName("ptr_hostnames")] string[] PtrHostnames,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("sources")] string[] Sources);

    private sealed record RiskEvidenceDto(
        [property: JsonPropertyName("indicator")] string Indicator,
        [property: JsonPropertyName("severity")] string Severity,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("evidence")] string Evidence,
        [property: JsonPropertyName("weight")] int Weight,
        [property: JsonPropertyName("explanation")] string Explanation);

    private sealed record RiskDto(
        [property: JsonPropertyName("score")] int? Score,
        [property: JsonPropertyName("level")] string Level,
        [property: JsonPropertyName("evidence")] List<RiskEvidenceDto> Evidence,
        [property: JsonPropertyName("has_sufficient_evidence")] bool HasSufficientEvidence);

    private sealed record FieldConfidenceDto(
        [property: JsonPropertyName("field")] string Field,
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("agreeing")] int Agreeing,
        [property: JsonPropertyName("successful")] int Successful,
        [property: JsonPropertyName("reason")] string Reason,
        [property: JsonPropertyName("supporting")] string[] Supporting,
        [property: JsonPropertyName("conflicting")] string[] Conflicting);

    private sealed record MetadataDto(
        [property: JsonPropertyName("tool_version")] string ToolVersion,
        [property: JsonPropertyName("timestamp_utc")] string TimestampUtc,
        [property: JsonPropertyName("duration_ms")] long DurationMs,
        [property: JsonPropertyName("providers_queried")] string[] ProvidersQueried,
        [property: JsonPropertyName("providers_successful")] int ProvidersSuccessful,
        [property: JsonPropertyName("has_cached_results")] bool HasCachedResults);

    private static AnonymityDto Anon(IntelligenceProfile profile)
    {
        static AnonSignalDto Sig(AnonymitySignal s)
            => new(s.Status.ToString().ToUpperInvariant(), s.Confidence, s.Sources, s.Evidence);
        return new AnonymityDto(
            Sig(profile.Anonymity.Tor), Sig(profile.Anonymity.Vpn),
            Sig(profile.Anonymity.Proxy), Sig(profile.Anonymity.Hosting));
    }

    public static JsonObject FromProfile(IntelligenceProfile profile)
    {
        JsonObject legacy = FromResult(profile.Geo);
        legacy["schema_version"] = "2.1";
        legacy["target"] = profile.Target;
        legacy["dns"] = JsonSerializer.SerializeToNode(new DnsDto(
            profile.Dns.Ip, profile.Dns.PtrHostnames,
            profile.Dns.Confidence, profile.Dns.Sources), Relaxed);
        legacy["security"] = JsonSerializer.SerializeToNode(Anon(profile), Relaxed);
        legacy["risk"] = JsonSerializer.SerializeToNode(new RiskDto(
            profile.Risk.Score, profile.Risk.Level,
            profile.Risk.Evidence.Select(e => new RiskEvidenceDto(
                e.Indicator, e.Severity, e.Source, e.Evidence, e.Weight, e.Explanation)).ToList(),
            profile.Risk.HasSufficientEvidence), Relaxed);
        legacy["asn"] = JsonSerializer.SerializeToNode(new AsnDto(
            profile.Asn.Asn, profile.Asn.Organization, profile.Asn.NetworkName,
            profile.Asn.Prefix, profile.Asn.Registry, profile.Asn.Country,
            profile.Asn.Holder, profile.Asn.Sources), Relaxed);
        legacy["consensus"] = JsonSerializer.SerializeToNode(
            profile.FieldConfidences.Select(f => new FieldConfidenceDto(
                f.Field, f.Value, f.Confidence, f.Agreeing, f.Successful,
                f.Reason, f.SupportingProviders, f.ConflictingProviders)).ToList(), Relaxed);
        legacy["metadata"] = JsonSerializer.SerializeToNode(new MetadataDto(
            profile.Metadata.ToolVersion,
            profile.Metadata.TimestampUtc.ToString("o"),
            profile.Metadata.DurationMs,
            profile.Metadata.ProvidersQueried,
            profile.Metadata.ProvidersSuccessful,
            profile.Metadata.HasCachedResults), Relaxed);
        return legacy;
    }

    public static string ProfileToJsonString(IntelligenceProfile profile, bool indented = false)
        => FromProfile(profile).ToJsonString(indented ? RelaxedIndented : Relaxed);
}
