using IPTraceX.Core;
using IPTraceX.Infrastructure;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.CLI;

/// <summary>Production analysis engine adapter (DI-friendly seam).</summary>
public sealed class EngineAdapter : IAnalysisEngine
{
    private readonly MultiProviderEngine _engine;

    public EngineAdapter(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        _engine = new MultiProviderEngine(config, fetcher);
    }

    public Task<GeoResult> AnalyzeAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default)
        => _engine.AnalyzeAsync(ip, onStage, cancellationToken);
}

/// <summary>Production full-profile adapter (DI-friendly seam).</summary>
public sealed class ProfileEngineAdapter : IProfileEngine
{
    private readonly IntelligenceProfiler _profiler;

    public ProfileEngineAdapter(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        _profiler = new IntelligenceProfiler(config, fetcher);
    }

    public Task<IntelligenceProfile> AnalyzeIpAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default)
        => _profiler.AnalyzeIpAsync(ip, onStage, cancellationToken);

    public Task<(DomainEvidence Resolution, List<IntelligenceProfile> Profiles)> AnalyzeDomainAsync(
        string domain, Action<string>? onStage = null, CancellationToken cancellationToken = default)
        => _profiler.AnalyzeDomainAsync(domain, onStage, cancellationToken);
}

/// <summary>Full email-profile seam (production profiler or test fake).</summary>
public interface IEmailProfileEngine
{
    Task<EmailProfile> AnalyzeEmailAsync(
        string email, Action<string>? onStage = null, CancellationToken cancellationToken = default);
}

/// <summary>Production email profiler adapter (DI-friendly seam).</summary>
public sealed class EmailProfileEngineAdapter : IEmailProfileEngine
{
    private readonly Infrastructure.EmailProfiler _profiler;

    public EmailProfileEngineAdapter(AppConfig config, IGeoJsonFetcher? fetcher = null)
    {
        _profiler = new Infrastructure.EmailProfiler(config, fetcher);
    }

    public Task<EmailProfile> AnalyzeEmailAsync(
        string email, Action<string>? onStage = null, CancellationToken cancellationToken = default)
        => _profiler.AnalyzeEmailAsync(email, onStage, cancellationToken);
}
