namespace IPTraceX.Core;

/// <summary>Email target after normalization (domain lowercased).</summary>
public sealed record EmailTarget(string Raw, string Local, string Domain)
{
    public string Normalized => $"{Local}@{Domain}";
}

/// <summary>Email domain intelligence (MX/SPF/DMARC/RDAP/classification).</summary>
public sealed record EmailDomainIntel(
    string Domain,
    string[] MxHosts,
    string? SpfRecord,
    string? DmarcRecord,
    string DnssecStatus,
    string DisposableStatus,
    string DisposableConfidence,
    string[] DisposableSources,
    bool IsFreeMail,
    string? MailProvider,
    string? Registrar,
    DateTimeOffset? DomainCreatedUtc,
    string? RegistrantCountry,
    string[] Sources);

/// <summary>Public avatar result. UNKNOWN unless publicly verifiable.</summary>
public sealed record AvatarIntel(
    string Status,
    string? Url,
    string? Source,
    string Confidence);

/// <summary>One public-footprint observation. Weak links stay weak.</summary>
public sealed record FootprintMatch(
    string Platform,
    string Url,
    string Title,
    string EvidenceType,
    string MatchedValue,
    string Confidence,
    string Source,
    DateTimeOffset ObservedUtc);

/// <summary>Safe breach metadata only — never secrets or dumps.</summary>
public sealed record BreachInfo(
    string Name,
    string? Domain,
    string? Date,
    string[] Categories,
    string Source);

/// <summary>Email reputation summary (computed, not a provider).</summary>
public sealed record EmailReputation(
    string Disposable,
    int BreachCount,
    string[] BreachNames,
    string SuspiciousDomain,
    string[] SuspiciousReasons,
    string Confidence,
    string[] Sources);

/// <summary>Full email intelligence profile.</summary>
public sealed record EmailProfile(
    string Target,
    string Normalized,
    string Domain,
    EmailDomainIntel DomainIntel,
    AvatarIntel Avatar,
    IReadOnlyList<FootprintMatch> Footprint,
    IReadOnlyList<BreachInfo> Breaches,
    EmailReputation Reputation,
    RiskAssessment Risk,
    IReadOnlyList<FieldConfidence> FieldConfidences,
    IReadOnlyList<ProviderOutcome> Providers,
    InvestigationMetadata Metadata,
    EmailSecurityExposure? Security = null);
