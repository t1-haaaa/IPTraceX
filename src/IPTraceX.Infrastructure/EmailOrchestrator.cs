using System.Collections.Concurrent;
using IPTraceX.Core;
using IPTraceX.Infrastructure.Providers;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Email intel orchestrator over the shared bounded runner: failure
/// isolation, health tracking, cancellation. Mirrors the IP path.
/// </summary>
public sealed class EmailOrchestrator
{
    private readonly IReadOnlyList<IEmailProvider> _providers;
    private readonly ConcurrentDictionary<string, ProviderHealth> _health = new(StringComparer.Ordinal);

    public EmailOrchestrator(IEnumerable<IEmailProvider> providers)
    {
        _providers = providers.ToList();
    }

    public IReadOnlyDictionary<string, ProviderHealth> Health => _health;

    public Task<IReadOnlyList<EmailEvidence>> RunAsync(
        EmailTarget target,
        EmailContext context,
        int maxConcurrency,
        TimeSpan globalTimeout,
        Action<string>? onStage = null,
        CancellationToken cancellationToken = default)
    {
        var operations = _providers
            .Select<IEmailProvider, Func<CancellationToken, Task<EmailEvidence>>>(provider => async ct =>
            {
                onStage?.Invoke($"email:{provider.Descriptor.Id}");
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(context.TimeoutSeconds));
                try
                {
                    EmailEvidence evidence = await provider
                        .InvestigateAsync(target, context, timeoutCts.Token)
                        .ConfigureAwait(false);
                    Mark(provider.Descriptor.Id,
                        evidence.Success ? ProviderHealthStatus.Ok : ProviderHealthStatus.Failed,
                        evidence.Error);
                    return evidence;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Mark(provider.Descriptor.Id, ProviderHealthStatus.Timeout, "timed out");
                    return Failure(provider.Descriptor.Id, "timed out");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Mark(provider.Descriptor.Id, ProviderHealthStatus.Failed, Short(ex));
                    return Failure(provider.Descriptor.Id, Short(ex));
                }
            })
            .ToList();

        return BoundedRunner.RunAsync(operations, maxConcurrency, globalTimeout, cancellationToken);
    }

    private void Mark(string id, ProviderHealthStatus status, string? error)
        => _health[id] = new ProviderHealth(id, status, error, DateTimeOffset.UtcNow);

    private static EmailEvidence Failure(string id, string error)
        => new EmailFailure(id, error);

    private static string Short(Exception ex)
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
}
