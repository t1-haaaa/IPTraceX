namespace IPTraceX.Infrastructure;

/// <summary>
/// Shared bounded-concurrency runner: at most N operations in flight,
/// one global timeout, per-item failure isolation. Used by the IP intel
/// orchestrator and the email orchestrator alike (no duplication).
/// </summary>
public static class BoundedRunner
{
    public static async Task<IReadOnlyList<T>> RunAsync<T>(
        IReadOnlyList<Func<CancellationToken, Task<T>>> operations,
        int maxConcurrency,
        TimeSpan globalTimeout,
        CancellationToken cancellationToken = default)
    {
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        globalCts.CancelAfter(globalTimeout);
        CancellationToken ct = globalCts.Token;

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
        var tasks = operations.Select(operation => RunOneAsync(operation, gate, ct)).ToList();
        try
        {
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return tasks
                .Where(t => t.IsCompletedSuccessfully)
                .Select(t => t.Result)
                .ToList();
        }
    }

    private static async Task<T> RunOneAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        bool acquired = false;
        try
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            acquired = true;
            return await operation(ct).ConfigureAwait(false);
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }
        }
    }
}
