using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>
/// Intel provider seam. Geo providers keep using <see cref="IGeoProvider"/>
/// (untouched); every other capability implements this contract.
/// </summary>
public interface IIntelProvider
{
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Investigate a validated public IP. Must never throw for bad data —
    /// return failure info in the evidence instead. May throw only
    /// OperationCanceledException for cancellation.
    /// </summary>
    Task<IntelEvidence> InvestigateAsync(
        string ip, ProviderContext context, CancellationToken cancellationToken = default);
}

/// <summary>Shared per-run context (timeouts, tokens — never secrets in output).</summary>
public sealed record ProviderContext(
    double TimeoutSeconds,
    string IpInfoToken,
    string? AbuseIpDbKey);

/// <summary>Normalized evidence from one intel provider.</summary>
public abstract record IntelEvidence(string ProviderId, bool Success, string? Error);

/// <summary>ASN/prefix/registry evidence (RIPEstat).</summary>
public sealed record AsnEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    string? Prefix,
    string[] Asns,
    string? Holder,
    string? Registry,
    string? Organization,
    string? NetworkName,
    string? Country) : IntelEvidence(ProviderId, Success, Error);

/// <summary>Reverse-DNS evidence (DoH / system resolver).</summary>
public sealed record DnsEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    string[] PtrHostnames) : IntelEvidence(ProviderId, Success, Error);

/// <summary>Tor exit-list evidence.</summary>
public sealed record TorEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    bool IsExitNode,
    int ExitCount) : IntelEvidence(ProviderId, Success, Error);

/// <summary>Cloud-range match evidence (official ranges only).</summary>
public sealed record CloudEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    CloudMatch[] Matches) : IntelEvidence(ProviderId, Success, Error);

/// <summary>One official cloud prefix containing the target.</summary>
public sealed record CloudMatch(string Provider, string Service, string Prefix);

/// <summary>AbuseIPDB reputation evidence (optional key).</summary>
public sealed record ReputationEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    int? AbuseConfidenceScore,
    string[] UsageTypes,
    string? Isp,
    string? CountryCode) : IntelEvidence(ProviderId, Success, Error);

/// <summary>Domain resolution evidence (system resolver).</summary>
public sealed record DomainEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    string Domain,
    string[] A,
    string[] Aaaa) : IntelEvidence(ProviderId, Success, Error);

/// <summary>Generic failure placeholder (timeout/cancel before any data).</summary>
public sealed record FailureEvidence(
    string ProviderId,
    string? Error) : IntelEvidence(ProviderId, false, Error);
