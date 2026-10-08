using IPTraceX.Core;

namespace IPTraceX.Infrastructure.Providers;

/// <summary>Email provider seam (mirrors IIntelProvider for IPs).</summary>
public interface IEmailProvider
{
    ProviderDescriptor Descriptor { get; }

    Task<EmailEvidence> InvestigateAsync(
        EmailTarget target, EmailContext context, CancellationToken cancellationToken = default);
}

/// <summary>Shared per-run context (timeouts, tokens — never in output).</summary>
public sealed record EmailContext(
    double TimeoutSeconds,
    string? HibpApiKey,
    string? GitHubToken);

/// <summary>Normalized evidence from one email provider.</summary>
public abstract record EmailEvidence(string ProviderId, bool Success, string? Error);

/// <summary>Domain/DNS intelligence evidence.</summary>
public sealed record EmailDomainEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    string[] MxHosts,
    string? SpfRecord,
    string? DmarcRecord,
    string DnssecStatus,
    string? Registrar,
    DateTimeOffset? DomainCreatedUtc,
    string? RegistrantCountry,
    bool? Disposable,
    bool IsFreeMail,
    string? MailProvider) : EmailEvidence(ProviderId, Success, Error);

/// <summary>Avatar discovery evidence (public only).</summary>
public sealed record AvatarEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    bool Found,
    string? ProfileUrl,
    string? DisplayName) : EmailEvidence(ProviderId, Success, Error);

/// <summary>Public-footprint evidence (weak links stay weak).</summary>
public sealed record FootprintEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    FootprintMatch[] Matches) : EmailEvidence(ProviderId, Success, Error);

/// <summary>Breach metadata evidence (safe fields only).</summary>
public sealed record BreachEvidence(
    string ProviderId,
    bool Success,
    string? Error,
    BreachInfo[] Breaches) : EmailEvidence(ProviderId, Success, Error);

/// <summary>Generic failure placeholder.</summary>
public sealed record EmailFailure(string ProviderId, string? Error)
    : EmailEvidence(ProviderId, false, Error);
