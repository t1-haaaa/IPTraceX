using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>Audit logger seam (real pipeline or no-op for tests).</summary>
public interface IAuditLogger : IDisposable
{
    string SessionId { get; }
    void Emit(AuditEvent e);
    void Flush(TimeSpan? timeout = null);
}

/// <summary>No-op logger for unit tests that opt out of file IO.</summary>
public sealed class NullAuditLogger : IAuditLogger
{
    public string SessionId { get; } = "SES-TEST";

    public static NullAuditLogger Instance { get; } = new();

    public void Emit(AuditEvent e)
    {
    }

    public void Flush(TimeSpan? timeout = null)
    {
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Central audit logger: always writes local JSONL, forwards to the
/// remote Vercel ingest API through a bounded async queue. Every remote
/// failure is fail-open — core operations never block or crash.
/// </summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly FileAuditSink _sink;
    private readonly RemoteAuditSender? _sender;
    private readonly BlockingCollection<AuditEvent> _queue;
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts = new();
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private bool _disposed;

    public string SessionId { get; } = AuditIds.NewSession();

    public AuditLogger(
        string projectRoot,
        string? endpoint = null,
        string? logDir = null,
        long logMaxMb = 100,
        int logRetentionDays = 30,
        int queueCapacity = 1000,
        int batchSize = 50,
        TimeSpan? flushInterval = null,
        TimeSpan? remoteTimeout = null,
        string? auditSecret = null)
    {
        _sink = new FileAuditSink(projectRoot, logDir, logMaxMb, logRetentionDays);
        _batchSize = Math.Max(1, batchSize);
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        _queue = new BlockingCollection<AuditEvent>(Math.Max(1, queueCapacity));
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            _sender = new RemoteAuditSender(
                endpoint, remoteTimeout ?? TimeSpan.FromSeconds(10), auditSecret);
        }

        _worker = new Thread(Work) { IsBackground = true, Name = "audit-shipper" };
        _worker.Start();
    }

    public void Emit(AuditEvent e)
    {
        AuditEvent full = e with
        {
            EventId = string.IsNullOrEmpty(e.EventId) ? AuditIds.NewEvent() : e.EventId,
            TimestampUtc = e.TimestampUtc == default ? DateTimeOffset.UtcNow : e.TimestampUtc,
            SessionId = e.SessionId ?? SessionId,
        };
        _sink.Append(full);
        if (_sender is null)
        {
            return;
        }

        if (!_queue.TryAdd(full))
        {
            // Backpressure: drop the event, record the fact locally.
            _sink.Append(full with
            {
                EventId = AuditIds.NewEvent(),
                TimestampUtc = DateTimeOffset.UtcNow,
                Severity = AuditSeverity.Warning,
                EventType = AuditEventTypes.AuditQueueFull,
                Category = AuditEventTypes.CategoryFor(AuditEventTypes.AuditQueueFull),
                Message = "Audit queue full; event kept locally, remote copy dropped.",
            });
        }
    }

    public void Flush(TimeSpan? timeout = null)
    {
        if (_sender is null)
        {
            return;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (_queue.Count != 0 && DateTimeOffset.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Flush(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
        }

        _cts.Cancel();
        _queue.CompleteAdding();
        try
        {
            if (!_worker.Join(TimeSpan.FromSeconds(5)))
            {
                // Background shipper; never hang shutdown.
            }
        }
        catch (Exception)
        {
        }

        _cts.Dispose();
        _queue.Dispose();
        GC.SuppressFinalize(this);
    }

    private void Work()
    {
        var batch = new List<AuditEvent>(_batchSize);
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                batch.Clear();
                int waitMs = Math.Max(1, (int)_flushInterval.TotalMilliseconds);
                if (_queue.TryTake(out AuditEvent? first, waitMs, _cts.Token))
                {
                    if (first is not null)
                    {
                        batch.Add(first);
                    }

                    while (batch.Count < _batchSize && _queue.TryTake(out AuditEvent? next))
                    {
                        if (next is not null)
                        {
                            batch.Add(next);
                        }
                    }

                    SendBatch(batch);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Shipper never throws; failures are recorded locally.
            }
        }

        // Drain on shutdown (best effort, bounded).
        try
        {
            var remaining = new List<AuditEvent>();
            while (_queue.TryTake(out AuditEvent? e))
            {
                remaining.Add(e);
                if (remaining.Count >= _batchSize * 4)
                {
                    break;
                }
            }

            for (int i = 0; i < remaining.Count; i += _batchSize)
            {
                SendBatch(remaining.Skip(i).Take(_batchSize).ToList());
            }
        }
        catch (Exception)
        {
        }
    }

    private void SendBatch(List<AuditEvent> batch)
    {
        if (_sender is null || batch.Count == 0)
        {
            return;
        }

        _sink.Append(new AuditEvent(
            AuditIds.NewEvent(), DateTimeOffset.UtcNow,
            AuditSeverity.Trace, AuditEventTypes.AuditSendStart,
            AuditEventTypes.CategoryFor(AuditEventTypes.AuditSendStart),
            SessionId: SessionId));
        try
        {
            int sent = _sender.SendAsync(batch, _cts.Token).GetAwaiter().GetResult();
            _sink.Append(new AuditEvent(
                AuditIds.NewEvent(), DateTimeOffset.UtcNow,
                AuditSeverity.Trace, AuditEventTypes.AuditSendComplete,
                AuditEventTypes.CategoryFor(AuditEventTypes.AuditSendComplete),
                SessionId: SessionId,
                Metadata: new Dictionary<string, string> { ["sent"] = sent.ToString() }));
        }
        catch (Exception ex)
        {
            _sink.Append(new AuditEvent(
                AuditIds.NewEvent(), DateTimeOffset.UtcNow,
                AuditSeverity.Warning, AuditEventTypes.AuditSendError,
                AuditEventTypes.CategoryFor(AuditEventTypes.AuditSendError),
                SessionId: SessionId,
                ErrorType: ex.GetType().Name,
                Message: TrimMessage(ex.Message)));
        }
    }

    private static string TrimMessage(string message)
    {
        string clean = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length > 200 ? clean[..200] : clean;
    }
}

/// <summary>Batch POST to the Vercel ingest API. Fail-open by contract.</summary>
public sealed class RemoteAuditSender
{
    private readonly string _endpoint;
    private readonly TimeSpan _timeout;
    private readonly string _secret;
    private static readonly HttpClient Shared = new();

    public RemoteAuditSender(string endpoint, TimeSpan timeout, string? secret = null)
    {
        _endpoint = endpoint.Trim().TrimEnd('/');
        _timeout = timeout;
        _secret = (secret ?? "").Trim();
    }

    public async Task<int> SendAsync(IReadOnlyList<AuditEvent> batch, CancellationToken ct = default)
    {
        if (batch.Count == 0)
        {
            return 0;
        }

        var payload = new JsonArray();
        foreach (AuditEvent e in batch)
        {
            payload.Add(AuditJson.ToNode(e));
        }

        var body = new JsonObject { ["events"] = payload };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint + "/api/v1/events")
        {
            Content = new StringContent(
                body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (_secret.Length != 0)
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _secret);
        }
        using HttpResponseMessage response = await Shared
            .SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
        if ((int)response.StatusCode == 429)
        {
            throw new RateLimitException("Audit ingest rate limited.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new NetworkException($"Audit ingest failed (HTTP {(int)response.StatusCode}).");
        }

        return batch.Count;
    }
}
