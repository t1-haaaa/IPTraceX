using Xunit;
using System.Text.Json;
using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;
using IPTraceX.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IPTraceX.Core.Tests;

/// <summary>Shared fakes/fixtures. No network anywhere in unit tests.</summary>
internal sealed class FakeFetcher : IGeoJsonFetcher
{
    private readonly Func<string, JsonElement?> _handler;
    private readonly Func<string, string> _textHandler;

    public List<string> Urls { get; } = [];

    public FakeFetcher(
        Func<string, JsonElement?> handler,
        Func<string, string>? textHandler = null)
    {
        _handler = handler;
        _textHandler = textHandler ?? (_ => "");
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

    public Task<string> FetchTextAsync(
        string url, double timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        Urls.Add(url);
        return Task.FromResult(_textHandler(url));
    }
}

internal sealed class FakeEmailEngine : IEmailProfileEngine
{
    private readonly Func<string, EmailProfile> _handler;
    private readonly Exception? _error;

    public List<string> Calls { get; } = [];

    public FakeEmailEngine(
        Func<string, EmailProfile>? handler = null, Exception? error = null)
    {
        _handler = handler ?? (_ => Sample.EmailProfile());
        _error = error;
    }

    public Task<EmailProfile> AnalyzeEmailAsync(
        string email, Action<string>? onStage = null, CancellationToken cancellationToken = default)
    {
        Calls.Add(email);
        if (_error is not null)
        {
            throw _error;
        }

        return Task.FromResult(_handler(email));
    }
}

internal sealed class FakeProfileEngine : IProfileEngine
{
    private readonly Func<string, IntelligenceProfile> _handler;
    private readonly Exception? _error;

    public FakeProfileEngine(
        Func<string, IntelligenceProfile>? handler = null, Exception? error = null)
    {
        _handler = handler ?? (_ => Sample.Profile());
        _error = error;
    }

    public Task<IntelligenceProfile> AnalyzeIpAsync(
        string ip, Action<string>? onStage = null, CancellationToken cancellationToken = default)
    {
        if (_error is not null)
        {
            throw _error;
        }

        return Task.FromResult(_handler(ip));
    }

    public Task<(DomainEvidence Resolution, List<IntelligenceProfile> Profiles)> AnalyzeDomainAsync(
        string domain, Action<string>? onStage = null, CancellationToken cancellationToken = default)
        => Task.FromResult<(DomainEvidence, List<IntelligenceProfile>)>(
            (new DomainEvidence("system-dns", true, null, domain, ["1.2.3.4"], ["2001:db8::1"]),
                [_handler("1.2.3.4")]));
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

    public static IntelligenceProfile Profile() => new("35.94.45.221", 4, false, Result(),
        new AsnIntelligence(
            "AS16509", "Amazon.com, Inc.", null, "35.92.0.0/16", "ARIN",
            "US", "AMAZON-EC2", ["geo-consensus", "ripestat"]),
        new DnsIntelligence(
            "35.94.45.221", ["ec2-35-94-45-221.example"], "MEDIUM",
            ["doh-cloudflare", "doh-google"]),
        new AnonymityIntelligence(
            new AnonymitySignal(DetectionStatus.NotDetected, "HIGH", ["tor-exits"], "absent"),
            new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [], "no source"),
            new AnonymitySignal(DetectionStatus.Unknown, "UNKNOWN", [], "no source"),
            new AnonymitySignal(DetectionStatus.Detected, "HIGH", ["cloud-ranges"], "AWS match")),
        new RiskAssessment(15, "VERY LOW",
            [new RiskEvidence("Hosting/datacenter", "LOW", "cloud-ranges", "AWS", 15, "hosting")],
            true),
        [
            new FieldConfidence("country", "United States", "high", 3, 3,
                "3 independent providers agree; no conflicting result.",
                ["ipwho.is", "ipapi.co", "ipinfo.io"], []),
            new FieldConfidence("region", "Oregon", "high", 3, 3,
                "3 independent providers agree; no conflicting result.",
                ["ipwho.is", "ipapi.co", "ipinfo.io"], []),
            new FieldConfidence("city", "Boardman", "high", 3, 3,
                "3 independent providers agree; no conflicting result.",
                ["ipwho.is", "ipapi.co", "ipinfo.io"], []),
            new FieldConfidence("asn", "AS16509", "high", 3, 3,
                "3 independent providers agree; no conflicting result.",
                ["ipwho.is", "ipapi.co", "ipinfo.io"], []),
        ],
        [
            new ProviderOutcome("ipwho.is", "success", null, "United States / Oregon", null),
            new ProviderOutcome("ripestat", "success", null, "AS16509", null),
        ],
        new InvestigationMetadata(
            "1.0.0", DateTimeOffset.UtcNow, 12,
            ["ipwho.is", "ripestat"], 2, false));

    public static EmailProfile EmailProfile() => new(
        "user@gmail.com", "user@gmail.com", "gmail.com",
        new EmailDomainIntel(
            "gmail.com",
            ["gmail-smtp-in.l.google.com"], "v=spf1 redirect=_spf.google.com",
            "v=DMARC1; p=none", "UNKNOWN", "NOT DETECTED", "HIGH", ["email-domain"],
            true, "Google (Gmail / Workspace)", "MarkMonitor Inc.",
            new DateTimeOffset(1995, 8, 13, 4, 0, 0, TimeSpan.Zero), null,
            ["email-domain", "rdap"]),
        new AvatarIntel("UNKNOWN", null, null, "UNKNOWN"),
        [],
        [],
        new EmailReputation("NOT DETECTED", 0, [], "NOT DETECTED", [], "MEDIUM",
            ["email-domain"]),
        new RiskAssessment(null, "UNKNOWN", [], false),
        [
            new FieldConfidence("domain", "gmail.com", "medium", 1, 1,
                "Directly parsed.", [], []),
        ],
        [new ProviderOutcome("email-domain", "success", null, "1 MX host(s)", null)],
        new InvestigationMetadata(
            "2.1.0", DateTimeOffset.UtcNow, 5,
            ["email-domain"], 1, false));

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
        StringWriter? errors = null,
        IProfileEngine? profiles = null,
        Exception? profileError = null,
        IEmailProfileEngine? emailProfiles = null)
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
            detectSelf,
            profiles ?? new FakeProfileEngine(error: profileError),
            null,
            emailProfiles ?? new FakeEmailEngine());
    }
}
