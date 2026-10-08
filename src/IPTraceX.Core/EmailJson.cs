using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace IPTraceX.Core;

/// <summary>Email profile JSON. Additive; IP contract untouched.</summary>
public static class EmailJson
{
    private sealed record DnsDto(
        [property: JsonPropertyName("mx")] string[] Mx,
        [property: JsonPropertyName("spf")] string? Spf,
        [property: JsonPropertyName("dmarc")] string? Dmarc,
        [property: JsonPropertyName("dnssec")] string Dnssec,
        [property: JsonPropertyName("registrar")] string? Registrar,
        [property: JsonPropertyName("domain_created")] string? Created,
        [property: JsonPropertyName("sources")] string[] Sources);

    private sealed record AvatarDto(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("confidence")] string Confidence);

    private sealed record BreachDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("categories")] string[] Categories,
        [property: JsonPropertyName("source")] string Source);

    private sealed record ReputationDto(
        [property: JsonPropertyName("disposable")] string Disposable,
        [property: JsonPropertyName("breaches")] int BreachCount,
        [property: JsonPropertyName("breach_names")] string[] BreachNames,
        [property: JsonPropertyName("suspicious_domain")] string Suspicious,
        [property: JsonPropertyName("suspicious_reasons")] string[] Reasons,
        [property: JsonPropertyName("confidence")] string Confidence,
        [property: JsonPropertyName("sources")] string[] Sources);

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions RelaxedIndented = new(Relaxed)
    {
        WriteIndented = true,
    };

    public static JsonObject FromEmailProfile(EmailProfile profile, string? investigationId = null)
    {
        var doc = new JsonObject
        {
            ["schema_version"] = "2.1",
            ["target_type"] = "email",
            ["target"] = profile.Target,
            ["normalized"] = profile.Normalized,
            ["domain"] = profile.Domain,
        };
        doc["email_domain"] = JsonSerializer.SerializeToNode(new DnsDto(
            profile.DomainIntel.MxHosts,
            profile.DomainIntel.SpfRecord,
            profile.DomainIntel.DmarcRecord,
            profile.DomainIntel.DnssecStatus,
            profile.DomainIntel.Registrar,
            profile.DomainIntel.DomainCreatedUtc?.ToString("O"),
            profile.DomainIntel.Sources), Relaxed);
        doc["dns"] = JsonSerializer.SerializeToNode(new
        {
            disposable = profile.DomainIntel.DisposableStatus,
            free_mail = profile.DomainIntel.IsFreeMail,
            mail_provider = profile.DomainIntel.MailProvider,
        }, Relaxed);
        doc["avatar"] = JsonSerializer.SerializeToNode(new AvatarDto(
            profile.Avatar.Status, profile.Avatar.Url,
            profile.Avatar.Source, profile.Avatar.Confidence), Relaxed);
        var footprint = new JsonArray();
        foreach (FootprintMatch match in profile.Footprint)
        {
            footprint.Add(new JsonObject
            {
                ["platform"] = match.Platform,
                ["url"] = match.Url,
                ["title"] = match.Title,
                ["evidence_type"] = match.EvidenceType,
                ["matched_value"] = match.MatchedValue,
                ["confidence"] = match.Confidence,
                ["source"] = match.Source,
                ["observed_utc"] = match.ObservedUtc.ToString("O"),
            });
        }

        doc["public_footprint"] = footprint;
        var breaches = new JsonArray();
        foreach (BreachInfo breach in profile.Breaches)
        {
            breaches.Add(JsonSerializer.SerializeToNode(new BreachDto(
                breach.Name, breach.Domain, breach.Date,
                breach.Categories, breach.Source), Relaxed));
        }

        doc["breach_intelligence"] = breaches;
        doc["reputation"] = JsonSerializer.SerializeToNode(new ReputationDto(
            profile.Reputation.Disposable,
            profile.Reputation.BreachCount,
            profile.Reputation.BreachNames,
            profile.Reputation.SuspiciousDomain,
            profile.Reputation.SuspiciousReasons,
            profile.Reputation.Confidence,
            profile.Reputation.Sources), Relaxed);
        doc["risk"] = JsonSerializer.SerializeToNode(new
        {
            score = profile.Risk.Score,
            level = profile.Risk.Level,
            evidence = profile.Risk.Evidence.Select(e => new
            {
                indicator = e.Indicator,
                severity = e.Severity,
                source = e.Source,
                evidence = e.Evidence,
                weight = e.Weight,
                confidence = "MEDIUM",
                explanation = e.Explanation,
            }).ToList(),
            has_sufficient_evidence = profile.Risk.HasSufficientEvidence,
        }, Relaxed);
        doc["evidence"] = JsonSerializer.SerializeToNode(
            profile.FieldConfidences.Select(f => new
            {
                field = f.Field,
                value = f.Value,
                confidence = f.Confidence,
                agreeing = f.Agreeing,
                successful = f.Successful,
                reason = f.Reason,
                supporting = f.SupportingProviders,
                conflicting = f.ConflictingProviders,
            }).ToList(), Relaxed);
        doc["confidence"] = JsonSerializer.SerializeToNode(
            profile.FieldConfidences.Select(f => new
            {
                field = f.Field,
                level = f.Confidence,
                reason = f.Reason,
            }).ToList(), Relaxed);
        var providers = new JsonArray();
        foreach (ProviderOutcome outcome in profile.Providers)
        {
            providers.Add(new JsonObject
            {
                ["name"] = outcome.ProviderId,
                ["status"] = outcome.Status,
                ["error"] = outcome.Error is null
                    ? JsonNode.Parse("null")
                    : JsonValue.Create(outcome.Error),
                ["summary"] = outcome.Summary,
            });
        }

        doc["providers"] = providers;
        doc["investigation"] = JsonSerializer.SerializeToNode(new
        {
            id = investigationId,
            saved = investigationId is not null,
        }, Relaxed);
        return doc;
    }

    public static string ToJsonString(EmailProfile profile, bool indented = false, string? investigationId = null)
        => FromEmailProfile(profile, investigationId).ToJsonString(indented ? RelaxedIndented : Relaxed);
}
