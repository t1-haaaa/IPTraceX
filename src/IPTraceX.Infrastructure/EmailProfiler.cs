using System.Diagnostics;
using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Assembles full email intelligence profiles from provider evidence.
/// Mirrors IntelligenceProfiler; email and IP flows stay independent.
/// </summary>
public sealed class EmailProfiler
{
    private readonly AppConfig _config;
    private readonly IGeoJsonFetcher? _fetcher;

    public EmailProfiler(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        _config = config;
        _fetcher = fetcher;
    }

    public List<IEmailProvider> BuildProviders() => [
        new EmailDomainProvider(_config.TimeoutSeconds, _fetcher),
        new GravatarProvider(_config.TimeoutSeconds, _fetcher),
        new GitHubFootprintProvider(_config.TimeoutSeconds, _fetcher),
        new HibpBreachProvider(_config.TimeoutSeconds, _fetcher, _config.HibpApiKey),
    ];

    public async Task<EmailProfile> AnalyzeEmailAsync(
        string raw,
        Action<string>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        EmailTarget target = EmailValidation.Parse(raw);
        var context = new EmailContext(_config.TimeoutSeconds, _config.HibpApiKey, _config.GitHubToken);
        var orchestrator = new EmailOrchestrator(BuildProviders());
        IReadOnlyList<EmailEvidence> evidence = await orchestrator.RunAsync(
            target, context, _config.MaxConcurrency,
            TimeSpan.FromSeconds(_config.GlobalTimeoutSeconds),
            onStage, cancellationToken).ConfigureAwait(false);
        clock.Stop();
        return Assemble(target, evidence, clock.ElapsedMilliseconds);
    }

    internal EmailProfile Assemble(
        EmailTarget target, IReadOnlyList<EmailEvidence> evidence, long durationMs)
    {
        EmailDomainEvidence? domain = evidence.OfType<EmailDomainEvidence>()
            .FirstOrDefault(e => e.Success);
        AvatarEvidence? avatar = evidence.OfType<AvatarEvidence>()
            .FirstOrDefault(e => e.Success);
        FootprintEvidence? footprint = evidence.OfType<FootprintEvidence>()
            .FirstOrDefault(e => e.Success);
        BreachEvidence? breach = evidence.OfType<BreachEvidence>()
            .FirstOrDefault(e => e.Success);

        var domainIntel = new EmailDomainIntel(
            target.Domain,
            domain?.MxHosts ?? [],
            domain?.SpfRecord,
            domain?.DmarcRecord,
            domain?.DnssecStatus ?? "UNKNOWN",
            DisposableStatus(domain?.Disposable),
            DisposableConfidence(domain),
            domain is null ? [] : ["email-domain"],
            domain?.IsFreeMail ?? false,
            domain?.MailProvider,
            domain?.Registrar,
            domain?.DomainCreatedUtc,
            domain?.RegistrantCountry,
            domain is null ? [] : ["email-domain", "rdap"]);

        var avatarIntel = new AvatarIntel(
            avatar is null ? "UNKNOWN" : avatar.Found ? "FOUND" : "UNKNOWN",
            avatar?.ProfileUrl,
            avatar is null ? null : "gravatar",
            avatar is null ? "UNKNOWN" : avatar.Found ? "MEDIUM" : "UNKNOWN");

        var footprintMatches = footprint?.Matches ?? [];
        var breaches = breach?.Breaches ?? [];

        bool isNew = domain?.DomainCreatedUtc is DateTimeOffset created
            && (DateTimeOffset.UtcNow - created).TotalDays < 30;
        bool hasMx = (domain?.MxHosts.Length ?? 0) != 0;
        EmailReputation reputation = EmailIntel.BuildReputation(
            domain?.Disposable, breaches, isNew, hasMx);

        RiskAssessment risk = EmailIntel.EvaluateRisk(
            domain?.Disposable, breaches.Length,
            reputation.SuspiciousDomain == "DETECTED",
            EmailIntel.ParseEmailWeights(_config.EmailRiskWeights));

        var confidences = new List<FieldConfidence>
        {
            Field("domain", target.Domain, 1, 1, "Directly parsed from the validated target."),
            Field("mail-provider", domainIntel.MailProvider,
                domainIntel.MailProvider is null ? 0 : 1, 1,
                domainIntel.MailProvider is null
                    ? "No provider matched the observed MX hosts."
                    : "Classified from observed MX hosts."),
            Field("disposable",
                domain?.Disposable is null ? null : domain.Disposable.Value ? "YES" : "NO",
                domain?.Disposable is null ? 0 : 1, 1,
                domain?.Disposable is null
                    ? "Disposable service unreachable."
                    : "Answered by the disposable-classification service."),
            Field("avatar",
                avatarIntel.Status == "FOUND" ? avatarIntel.Url : null,
                avatarIntel.Status == "FOUND" ? 1 : 0, 1,
                avatarIntel.Status == "FOUND"
                    ? "One public provider returned an avatar."
                    : "No public avatar found; absence proves nothing."),
            Field("breach",
                breaches.Length == 0 ? null : $"{breaches.Length} breach(es)",
                breach is null ? 0 : 1, 1,
                breach is null
                    ? "No breach-intelligence source configured."
                    : breaches.Length == 0
                        ? "Breach corpus queried; address absent."
                        : "Breach metadata returned by the corpus."),
        };

        var outcomes = evidence.Select(e => new ProviderOutcome(
            e.ProviderId, e.Success ? "success" : "failed", e.Error,
            Summarize(e), null)).ToList();

        var metadata = new InvestigationMetadata(
            AppInfo.Version, DateTimeOffset.UtcNow, durationMs,
            [.. evidence.Select(e => e.ProviderId).Distinct(StringComparer.Ordinal)],
            evidence.Count(e => e.Success),
            false);
        return new EmailProfile(
            target.Raw.Trim(), target.Normalized, target.Domain,
            domainIntel, avatarIntel, footprintMatches, breaches,
            reputation, risk, confidences, outcomes, metadata);
    }

    private static FieldConfidence Field(
        string name, string? value, int agreeing, int total, string reason)
    {
        string level = value is null || total == 0 || agreeing == 0
            ? "unknown"
            : total == 1
                ? "medium"
                : (double)agreeing / total >= 1.0 ? "high"
                : (double)agreeing / total >= 0.5 ? "medium" : "low";
        return new FieldConfidence(
            name, value, level, agreeing, total, reason, [], []);
    }

    private static string DisposableStatus(bool? disposable)
        => disposable is true ? "DETECTED"
            : disposable is false ? "NOT DETECTED" : "UNKNOWN";

    private static string DisposableConfidence(EmailDomainEvidence? domain)
        => domain?.Disposable is null ? "UNKNOWN" : "HIGH";

    private static string Summarize(EmailEvidence evidence)
        => evidence switch
        {
            EmailDomainEvidence d when d.Success =>
                $"{d.MxHosts.Length} MX host(s)" + (d.MailProvider is null ? "" : $", {d.MailProvider}"),
            AvatarEvidence a when a.Success => a.Found ? "avatar found" : "no public avatar",
            FootprintEvidence f when f.Success => $"{f.Matches.Length} public match(es)",
            BreachEvidence b when b.Success => $"{b.Breaches.Length} breach(es)",
            _ => evidence.Error ?? "failed",
        };
}
