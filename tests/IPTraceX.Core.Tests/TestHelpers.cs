using Xunit;
using System.Text.Json;
using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPTraceX.Core.Tests;

/// <summary>Shared fakes/fixtures. No network anywhere in unit tests.</summary>
internal sealed class FakeFetcher : IGeoJsonFetcher
{
    private readonly Func<string, JsonElement?> _handler;

    public List<string> Urls { get; } = [];

    public FakeFetcher(Func<string, JsonElement?> handler)
    {
        _handler = handler;
    }

    public static JsonElement? Json(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    public Task<JsonElement?> FetchAsync(
        string url, double timeoutSeconds,
        IDictionary<string, string>? extraHeaders = null,
        CancellationToken cancellationToken = default)
    {
        Urls.Add(url);
        return Task.FromResult(_handler(url));
    }
}

internal sealed class FakeEngine : IAnalysisEngine
{
    private readonly Func<string, GeoResult> _handler;
    private readonly Exception? _error;

    public List<string> Calls { get; } = [];
    public List<string> Stages { get; } = [];

    public FakeEngine(Func<string, GeoResult>? handler = null, Exception? error = null)
    {
        _handler = handler ?? Sample.Result;
        _error = error;
    }

    public Task<GeoResult> AnalyzeAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(ip);
        if (onStage is not null)
        {
            foreach (string stage in new[] { "validating", "querying:fake", "done" })
            {
                Stages.Add(stage);
                onStage(stage);
            }
        }

        if (_error is not null)
        {
            throw _error;
        }

        return Task.FromResult(_handler(ip));
    }
}

internal static class Sample
{
    public static GeoResult Result(string source = "Multi-provider consensus") => new()
    {
        Ip = "35.94.45.221",
        IpVersion = 4,
        Geolocation =
        {
            Country = "United States",
            CountryCode = "US",
            Region = "Oregon",
            RegionCode = "OR",
            City = "Boardman",
            PostalCode = "97818",
            Latitude = 45.8398578,
            Longitude = -119.7005791,
            Timezone = "America/Los_Angeles",
        },
        Network =
        {
            Isp = "Amazon.com, Inc.",
            Organization = "Amazon.com, Inc.",
            Asn = "AS16509",
            Hostname = "amazon.com",
        },
        GoogleMapsUrl = "https://www.google.com/maps?q=45.8398578,-119.7005791",
        Source = source,
        Confidence = "high",
        ProvidersQueried = 3,
        ProvidersSuccessful = 3,
        ProvidersAgreeing = 3,
        AgreementRatio = 1.0,
    };

    public static AppConfig TestConfig() => new()
    {
        TimeoutSeconds = 5,
        CacheTtlSeconds = 0,
        ProjectRoot = Path.GetTempPath(),
    };

    public static CliApp App(
        FakeEngine engine,
        string stdin = "",
        Palette? palette = null,
        AppConfig? config = null,
        Func<CancellationToken, Task<string>>? detectSelf = null,
        StringWriter? output = null,
        StringWriter? errors = null)
    {
        output ??= new StringWriter();
        errors ??= new StringWriter();
        return new CliApp(
            config ?? TestConfig(),
            palette ?? new Palette(false),
            new StringReader(stdin),
            output,
            errors,
            NullLogger.Instance,
            engine,
            detectSelf);
    }
}
