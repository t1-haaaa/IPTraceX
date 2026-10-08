namespace IPTraceX.Core;

/// <summary>Provider categories. New categories extend this enum.</summary>
public enum ProviderCategory
{
    Geo,
    Asn,
    Dns,
    Security,
    Cloud,
    Reputation,
    Network,
    Avatar,
    Footprint,
    Breach,
}

/// <summary>How a signal was observed.</summary>
public enum DetectionStatus
{
    Unknown,
    NotDetected,
    Detected,
}

/// <summary>Provider health in this process (resets per run).</summary>
public enum ProviderHealthStatus
{
    Unknown,
    Ok,
    Timeout,
    RateLimited,
    Failed,
}

/// <summary>Static capability metadata for one provider.</summary>
public sealed record ProviderDescriptor(
    string Id,
    string DisplayName,
    ProviderCategory Category,
    int[] SupportedIpVersions,
    bool RequiresKey,
    string AuthNote,
    string RateLimitNote);

/// <summary>One provider's verdict, with its own normalized payload.</summary>
public sealed record ProviderOutcome(
    string ProviderId,
    string Status,
    string? Error,
    string Summary,
    GeoResult? Result = null);

/// <summary>Single anonymity signal with sources and confidence.</summary>
public sealed record AnonymitySignal(
    DetectionStatus Status,
    string Confidence,
    string[] Sources,
    string Evidence);

/// <summary>VPN / proxy / Tor / hosting verdicts. Unknown means "no capable
/// source answered", never a guess.</summary>
public sealed record AnonymityIntelligence(
    AnonymitySignal Tor,
    AnonymitySignal Vpn,
    AnonymitySignal Proxy,
    AnonymitySignal Hosting);

/// <summary>ASN / prefix / registry intelligence.</summary>
public sealed record AsnIntelligence(
    string? Asn,
    string? Organization,
    string? NetworkName,
    string? Prefix,
    string? Registry,
    string? Country,
    string? Holder,
    string[] Sources);

/// <summary>Reverse-DNS intelligence for one IP.</summary>
public sealed record DnsIntelligence(
    string Ip,
    string[] PtrHostnames,
    string Confidence,
    string[] Sources);

/// <summary>One risk contribution. Every point is explained.</summary>
public sealed record RiskEvidence(
    string Indicator,
    string Severity,
    string Source,
    string Evidence,
    int Weight,
    string Explanation);

/// <summary>Transparent, evidence-driven risk score.</summary>
public sealed record RiskAssessment(
    int? Score,
    string Level,
    IReadOnlyList<RiskEvidence> Evidence,
    bool HasSufficientEvidence);

/// <summary>Per-field consensus confidence.</summary>
public sealed record FieldConfidence(
    string Field,
    string? Value,
    string Confidence,
    int Agreeing,
    int Successful,
    string Reason,
    string[] SupportingProviders,
    string[] ConflictingProviders);

/// <summary>Run metadata (no secrets, ever).</summary>
public sealed record InvestigationMetadata(
    string ToolVersion,
    DateTimeOffset TimestampUtc,
    long DurationMs,
    string[] ProvidersQueried,
    int ProvidersSuccessful,
    bool HasCachedResults);

/// <summary>
/// Complete intelligence profile for one target. Additive to the legacy
/// GeoResult contract: the classic fields stay byte-compatible.
/// </summary>
public sealed record IntelligenceProfile(
    string Target,
    int IpVersion,
    bool IsDomainTarget,
    GeoResult Geo,
    AsnIntelligence Asn,
    DnsIntelligence Dns,
    AnonymityIntelligence Anonymity,
    RiskAssessment Risk,
    IReadOnlyList<FieldConfidence> FieldConfidences,
    IReadOnlyList<ProviderOutcome> Providers,
    InvestigationMetadata Metadata);
