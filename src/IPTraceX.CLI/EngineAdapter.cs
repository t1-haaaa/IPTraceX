using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Microsoft.Extensions.Logging;

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
