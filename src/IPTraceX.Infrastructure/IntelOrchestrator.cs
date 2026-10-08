using System.Collections.Concurrent;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>Live health of one provider in this process.</summary>
public sealed record ProviderHealth(
    string ProviderId,
    ProviderHealthStatus Status,
    string? LastError,
    DateTimeOffset LastCheckedUtc);

/// <summary>
/// Runs intel providers with bounded concurrency, per-provider timeouts,
/// one global timeout, and failure isolation. Geo providers keep their own
/// sequential path; this orchestrator serves the wider intel layer.
/// </summary>
public sealed class IntelOrchestrator
{
    private readonly IReadOnlyList<Providers.IIntelProvider> _providers;
    private readonly ConcurrentDictionary<string, ProviderHealth> _health = new(StringComparer.Ordinal);

    public IntelOrchestrator(IEnumerable<Providers.IIntelProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyDictionary<string, ProviderHealth> Health => _health;

    public async Task<IReadOnlyList<Providers.IntelEvidence>> RunAsync(
        string ip,
        Providers.ProviderContext context,
        int maxConcurrency,
        TimeSpan globalTimeout,
        Action<string>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        globalCts.CancelAfter(globalTimeout);
        CancellationToken ct = globalCts.Token;

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = _providers.Select(
            provider => RunOneAsync(provider, ip, context, gate, onStage, ct)).ToList();

        try
        {
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Global timeout/cancel: keep whatever finished individually.
            var finished = new List<Providers.IntelEvidence>();
            foreach (var task in tasks)
            {
                if (task.IsCompletedSuccessfully)
                {
                    finished.Add(task.Result);
                }
            }

            return finished;
        }
    }

    private async Task<Providers.IntelEvidence> RunOneAsync(
        Providers.IIntelProvider provider,
        string ip,
        Providers.ProviderContext context,
        SemaphoreSlim gate,
        Action<string>? onStage,
        CancellationToken ct)
    {
        bool acquired = false;
        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            onStage?.Invoke($"intel:{provider.Descriptor.Id}");
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(context.TimeoutSeconds));
            try
            {
                Providers.IntelEvidence evidence = await provider
                    .InvestigateAsync(ip, context, timeoutCts.Token)
                    .ConfigureAwait(false);
                Mark(provider.Descriptor.Id,
                    evidence.Success ? ProviderHealthStatus.Ok : ProviderHealthStatus.Failed,
                    evidence.Error);
                return evidence;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // caller cancelled: propagate, do not record as failure
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Mark(provider.Descriptor.Id, ProviderHealthStatus.Timeout, "timed out");
                return Fail(provider.Descriptor.Id, "timed out");
            }
            catch (Exception ex)
            {
                Mark(provider.Descriptor.Id, ProviderHealthStatus.Failed, ShortError(ex));
                return Fail(provider.Descriptor.Id, ShortError(ex));
            }
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }
        }
    }

    private static string ShortError(Exception ex)
    {
        string message = ex.Message;
        int newline = message.IndexOf('\n');
        if (newline >= 0)
        {
            message = message[..newline];
        }

        message = message.Trim();
        return message.Length > 160 ? message[..160] : message;
    }

    private void Mark(string id, ProviderHealthStatus status, string? error)
        => _health[id] = new ProviderHealth(id, status, error, DateTimeOffset.UtcNow);

    private static Providers.IntelEvidence Fail(string id, string error)
        => new Providers.FailureEvidence(id, error);
}
