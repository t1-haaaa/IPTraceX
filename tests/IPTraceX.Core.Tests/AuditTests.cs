using System.Net;
using System.Net.Sockets;
using System.Text;
using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class AuditModelTests
{
    [Fact]
    public void TaxonomyKnownAndCategorized()
    {
        Assert.True(AuditEventTypes.IsKnown(AuditEventTypes.EmailSecurityAlert));
        Assert.True(AuditEventTypes.IsKnown(AuditEventTypes.ProviderTimeout));
        Assert.False(AuditEventTypes.IsKnown("HACK_ATTEMPT"));
        Assert.False(AuditEventTypes.IsKnown(null));
        Assert.Equal("email", AuditEventTypes.CategoryFor(AuditEventTypes.EmailBreachCheck));
        Assert.Equal("security", AuditEventTypes.CategoryFor(AuditEventTypes.SuspiciousInput));
        Assert.Equal("audit", AuditEventTypes.CategoryFor(AuditEventTypes.AuditQueueFull));
    }

    [Fact]
    public void SeverityLadderValid()
    {
        foreach (string s in new[] { "TRACE", "DEBUG", "INFO", "WARNING", "ERROR", "ALERT", "CRITICAL" })
        {
            Assert.True(AuditSeverity.IsValid(s));
        }

        Assert.False(AuditSeverity.IsValid("HACK"));
        Assert.False(AuditSeverity.IsValid(null));
    }

    [Fact]
    public void IdsWellFormed()
    {
        Assert.StartsWith("SES-", AuditIds.NewSession());
        Assert.StartsWith("COR-", AuditIds.NewCorrelation());
        Assert.StartsWith("EVT-", AuditIds.NewEvent());
        Assert.Matches(@"^SES-\d{8}-[A-Z0-9]{6}$", AuditIds.NewSession());
    }

    [Fact]
    public void SerializationRoundtrip()
    {
        var e = new AuditEvent(
            "EVT-1", new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
            "INFO", AuditEventTypes.ProviderQueryComplete, "provider",
            "query", "cli", "success", 182, "SES-1", "COR-1", null,
            "ip", "8.8.8.8", "ipwho.is", 200, 0, null, null, "ok",
            new Dictionary<string, string> { ["a"] = "b" });
        string line = AuditJson.ToJsonLines(e);
        AuditEvent back = AuditJson.ParseLine(line);
        Assert.Equal("EVT-1", back.EventId);
        Assert.Equal("INFO", back.Severity);
        Assert.Equal("ipwho.is", back.Provider);
        Assert.Equal(200, back.HttpStatus);
        Assert.Equal("b", back.Metadata!["a"]);
    }

    [Fact]
    public void IngestValidationRejectsBadPayloads()
    {
        Assert.Throws<UsageException>(() => AuditJson.ParseLine("{not json"));
        Assert.Throws<UsageException>(() => AuditJson.ParseLine(
            """{"severity":"INFO","event_type":"HACK_ATTEMPT"}"""));
        Assert.Throws<UsageException>(() => AuditJson.ParseLine(
            """{"severity":"BOGUS","event_type":"CLI_COMMAND"}"""));
        Assert.Throws<UsageException>(() => AuditJson.ParseLine(
            """{"severity":"INFO","event_type":"CLI_COMMAND","http_status":99}"""));
    }
}

public sealed class AuditSinkTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static AuditEvent Ev(string type = "CLI_COMMAND", string severity = "INFO")
        => new(AuditIds.NewEvent(), DateTimeOffset.UtcNow, severity, type,
            AuditEventTypes.CategoryFor(type), Component: "cli");

    [Fact]
    public void AppendsDailyJsonl()
    {
        string dir = TempDir();
        try
        {
            var sink = new FileAuditSink(dir);
            sink.Append(Ev());
            sink.Append(Ev(AuditEventTypes.ProviderTimeout, "WARNING"));
            string file = Path.Combine(dir, "logs",
                DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
                $"audit-{DateTimeOffset.UtcNow:yyyy-MM-dd}.jsonl");
            Assert.True(File.Exists(file));
            Assert.Equal(2, File.ReadAllLines(file).Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SecretsRedactedBeforeDisk()
    {
        string dir = TempDir();
        try
        {
            var sink = new FileAuditSink(dir);
            sink.Append(Ev() with
            {
                Metadata = new Dictionary<string, string>
                {
                    ["hibp-api-key"] = "SUPER-SECRET",
                    ["Authorization"] = "Bearer SECRET",
                    ["provider"] = "hibp",
                },
            });
            string content = File.ReadAllText(Directory.GetFiles(
                Path.Combine(dir, "logs"), "*.jsonl", SearchOption.AllDirectories).Single());
            Assert.DoesNotContain("SUPER-SECRET", content);
            Assert.Contains("[REDACTED]", content);
            Assert.Contains("hibp", content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void SizeCapStopsGrowth()
    {
        string dir = TempDir();
        try
        {
            var sink = new FileAuditSink(dir, maxMb: 1);
            for (int i = 0; i < 20000; i++)
            {
                sink.Append(Ev() with
                {
                    Message = new string('x', 200),
                });
            }

            string file = Directory.GetFiles(
                Path.Combine(dir, "logs"), "*.jsonl", SearchOption.AllDirectories).Single();
            Assert.True(new FileInfo(file).Length <= 1_100_000);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RetentionPurgesOldDays()
    {
        string dir = TempDir();
        try
        {
            var sink = new FileAuditSink(dir, retentionDays: 30);
            var old = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
            sink.Append(Ev() with { TimestampUtc = old });
            sink.Append(Ev());
            sink.PurgeOld(new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero));
            Assert.False(Directory.Exists(Path.Combine(dir, "logs", "2020-01-01")));
            Assert.True(Directory.Exists(Path.Combine(dir, "logs", "2026-10-08")));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CorruptLinesSkippedOnRead()
    {
        string dir = TempDir();
        try
        {
            var sink = new FileAuditSink(dir);
            sink.Append(Ev());
            string day = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
            File.AppendAllText(
                Path.Combine(dir, "logs", day, $"audit-{day}.jsonl"), "{broken\n");
            Assert.Single(FileAuditSink.Read(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public sealed class AlertRuleTests
{
    private static AuditEvent Ev(string type, DateTimeOffset ts)
        => new(AuditIds.NewEvent(), ts, "INFO", type,
            AuditEventTypes.CategoryFor(type), Component: "api");

    [Fact]
    public void AuthBurstAlerts()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 10)
            .Select(i => Ev(AuditEventTypes.AuthFailure, now.AddSeconds(-i * 5)))
            .ToList<AuditEvent>();
        var alerts = AlertRules.Evaluate(events, now);
        Assert.Single(alerts);
        Assert.Equal(AlertRules.AuthFailures, alerts[0].Rule);
        Assert.Equal("ALERT", alerts[0].Severity);
    }

    [Fact]
    public void OrdinaryFailuresDoNotAlert()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 5)
            .Select(i => Ev(AuditEventTypes.ProviderTimeout, now.AddSeconds(-i * 5)))
            .ToList<AuditEvent>();
        Assert.Empty(AlertRules.Evaluate(events, now));
    }

    [Fact]
    public void TraversalPatternAlerts()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 3)
            .Select(i => Ev(AuditEventTypes.SuspiciousInput, now.AddSeconds(-i * 5)) with
            {
                ErrorCode = "PATH_TRAVERSAL",
            })
            .ToList<AuditEvent>();
        Assert.Contains(AlertRules.Evaluate(events, now), a => a.Rule == AlertRules.PathTraversal);
    }

    [Fact]
    public void InvalidBurstAlerts()
    {
        var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 100)
            .Select(i => Ev(AuditEventTypes.InvalidPayload, now.AddMilliseconds(-i * 500)))
            .ToList<AuditEvent>();
        Assert.Contains(AlertRules.Evaluate(events, now), a => a.Rule == AlertRules.InvalidRequests);
    }
}

public sealed class AuditPipelineTests
{
    private static AuditEvent Ev()
        => new(AuditIds.NewEvent(), DateTimeOffset.UtcNow, "INFO",
            AuditEventTypes.CliCommand, "cli", Component: "cli");

    [Fact]
    public void LocalLoggingWorksWithoutRemote()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var logger = new AuditLogger(dir);
            logger.Emit(Ev());
            logger.Flush(TimeSpan.FromSeconds(5));
            Assert.Single(FileAuditSink.Read(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RemoteFailureIsFailOpen()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Nothing listens here: connection refused must not throw.
            using var logger = new AuditLogger(dir, "http://127.0.0.1:9",
                remoteTimeout: TimeSpan.FromSeconds(2));
            logger.Emit(Ev());
            logger.Flush(TimeSpan.FromSeconds(8));
            bool seen = false;
            for (int i = 0; i < 50 && !seen; i++)
            {
                seen = FileAuditSink.Read(dir, maxLines: 100)
                    .Any(e => e.EventType == AuditEventTypes.AuditSendError);
                if (!seen)
                {
                    Thread.Sleep(200);
                }
            }

            Assert.True(seen, "expected AUDIT_SEND_ERROR after refused connection");
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task RemoteBatchDeliveredToFakeServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new TaskCompletionSource<string>();
        _ = Task.Run(async () =>
        {
            using TcpClient client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buffer = new byte[65536];
            int read = await stream.ReadAsync(buffer);
            string request = Encoding.UTF8.GetString(buffer, 0, read);
            string body = "{\"received\":true}";
            string response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n"
                + $"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n{body}";
            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes);
            received.TrySetResult(request);
        });

        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var logger = new AuditLogger(dir, $"http://127.0.0.1:{port}",
                remoteTimeout: TimeSpan.FromSeconds(10));
            logger.Emit(Ev());
            logger.Emit(Ev());
            logger.Flush(TimeSpan.FromSeconds(10));
            string request = await received.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Contains("POST /api/v1/events", request);
            Assert.Contains(AuditEventTypes.CliCommand, request);
        }
        finally
        {
            listener.Stop();
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void QueueOverflowKeepsLocalDropsRemote()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var logger = new AuditLogger(dir, "http://127.0.0.1:9",
                queueCapacity: 2, remoteTimeout: TimeSpan.FromSeconds(1));
            for (int i = 0; i < 20; i++)
            {
                logger.Emit(Ev());
            }

            logger.Flush(TimeSpan.FromSeconds(6));
            Assert.Contains(FileAuditSink.Read(dir, maxLines: 1000),
                e => e.EventType == AuditEventTypes.AuditQueueFull);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public sealed class AuditCliTests
{
    private static (CliApp App, StringWriter Out, string Dir) Make()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var config = Sample.TestConfig();
        config.ProjectRoot = dir;
        config.AuditEnabled = true;
        var output = new StringWriter();
        var app = Sample.App(new FakeEngine(), "", new Palette(false), config,
            null, output, new StringWriter(),
            audit: new AuditLogger(dir));
        return (app, output, dir);
    }

    [Fact]
    public async Task LookupEmitsAuditTrail()
    {
        var (app, _, dir) = Make();
        try
        {
            Assert.Equal(0, await app.RunAsync(["8.8.8.8"]));
            IReadOnlyList<AuditEvent> events = FileAuditSink.Read(dir, maxLines: 100);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.IpLookupStart);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.IpLookupComplete);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.ProviderQueryComplete);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.RiskCalculation);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task EmailLookupEmitsSecurityEvents()
    {
        var (app, _, dir) = Make();
        try
        {
            Assert.Equal(0, await app.RunAsync(["--email", "user@gmail.com"]));
            IReadOnlyList<AuditEvent> events = FileAuditSink.Read(dir, maxLines: 100);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.EmailLookupStart);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.EmailValidation);
            Assert.Contains(events, e => e.EventType == AuditEventTypes.EmailBreachCheck);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task LogsViewerFilters()
    {
        var (app, output, dir) = Make();
        try
        {
            Assert.Equal(0, await app.RunAsync(["8.8.8.8"]));
            var viewer = Sample.App(new FakeEngine(), "", new Palette(false),
                appConfig(dir), null, output, new StringWriter(),
                audit: NullAuditLogger.Instance);
            Assert.Equal(0, await viewer.RunAsync(["--logs", "--last", "5"]));
            Assert.Contains("AUDIT LOG", output.ToString());
            output.GetStringBuilder().Clear();
            Assert.Equal(0, await viewer.RunAsync(["--logs", "--json", "--last", "200"]));
            Assert.Contains(AuditEventTypes.ApplicationStart, output.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task SecurityStatusSummary()
    {
        var (app, output, dir) = Make();
        try
        {
            Assert.Equal(0, await app.RunAsync(["8.8.8.8"]));
            output.GetStringBuilder().Clear();
            Assert.Equal(0, await app.RunAsync(["--security-status"]));
            string text = output.ToString();
            Assert.Contains("IPTraceX SECURITY STATUS", text);
            Assert.Contains("Events Today:", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task TraversalIdRaisesSuspiciousEvent()
    {
        var (app, _, dir) = Make();
        try
        {
            await app.RunAsync(["--investigation", "../evil"]);
            Assert.Contains(FileAuditSink.Read(dir, maxLines: 100),
                e => e.EventType == AuditEventTypes.SuspiciousInput
                    && e.Severity == AuditSeverity.Alert);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static AppConfig appConfig(string dir)
    {
        var config = Sample.TestConfig();
        config.ProjectRoot = dir;
        return config;
    }
}
