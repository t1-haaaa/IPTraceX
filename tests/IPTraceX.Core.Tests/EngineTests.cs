using Xunit;
using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;

namespace IPTraceX.Core.Tests;

public sealed class EngineTests
{
    private static AppConfig Config() => Sample.TestConfig();

    private sealed class FakeProvider(string name, object outcome) : Infrastructure.Providers.IGeoProvider
    {
        public string Name => name;
        public int[] SupportedIpVersions => [4, 6];
        public int Calls;

        public Task<GeoResult> LookupAsync(string ip, CancellationToken ct = default)
        {
            Calls++;
            if (outcome is Exception ex)
            {
                throw ex;
            }

            return Task.FromResult(Sample.Result());
        }
    }

    private static MultiProviderEngine EngineWith(params (string Name, object Outcome)[] specs)
    {
        var config = Config();
        var providers = specs
            .Select(s => (Infrastructure.Providers.IGeoProvider)new FakeProvider(s.Name, s.Outcome))
            .ToList();
        return new MultiProviderEngine(config, providers);
    }

    [Fact]
    public async Task ContinuesWhenOneFails()
    {
        var engine = EngineWith(
            ("a", Sample.Result()), ("b", new ProviderException("boom")), ("c", Sample.Result()));
        GeoResult info = await engine.AnalyzeAsync("41.107.85.239");
        Assert.Equal("United States", info.Geolocation.Country);
        Assert.Equal(2, info.ProvidersSuccessful);
        Assert.Equal(3, info.ProvidersQueried);
        Assert.Equal(new[] { "success", "failed", "success" }, info.Providers.Select(d => d.Status));
    }

    [Fact]
    public async Task AllFailRaises()
    {
        var engine = EngineWith(("a", new ProviderException("x")), ("b", new ProviderException("y")));
        await Assert.ThrowsAsync<ProviderException>(() => engine.AnalyzeAsync("41.107.85.239"));
    }

    [Fact]
    public async Task TimeoutAnd429AreRecordedNotFatal()
    {
        var engine = EngineWith(
            ("a", new ProviderTimeoutException("timed out")),
            ("b", new RateLimitException("limited")),
            ("c", Sample.Result()));
        GeoResult info = await engine.AnalyzeAsync("8.8.8.8");
        Assert.Equal("United States", info.Geolocation.Country);
        Assert.Equal(1, info.ProvidersSuccessful);
    }

    [Fact]
    public async Task ValidationRunsBeforeAnyProvider()
    {
        var engine = EngineWith(("a", Sample.Result()));
        await Assert.ThrowsAnyAsync<InvalidIpException>(() => engine.AnalyzeAsync("192.168.1.1"));
        await Assert.ThrowsAnyAsync<InvalidIpException>(() => engine.AnalyzeAsync("not-an-ip"));
    }

    [Fact]
    public void ProviderNamesResolved()
    {
        Assert.Equal(
            new[] { "ipwho.is", "ipapi.co", "ipinfo.io" },
            ProviderRegistry.ResolveNames(""));
        Assert.Equal(
            new[] { "ipinfo.io", "ipapi.co" },
            ProviderRegistry.ResolveNames("ipinfo,ipapi"));
        Assert.Throws<UsageException>(() => ProviderRegistry.ResolveNames("nope"));
    }

    [Fact]
    public void CacheIsProviderIsolated()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", dir);
            var data = System.Text.Json.Nodes.JsonObject.Parse("""{"ip":"8.8.8.8"}""")!.AsObject();
            FileCache.Put("8.8.8.8", data, 3600, "v2:a");
            Assert.NotNull(FileCache.Get("8.8.8.8", 3600, "v2:a"));
            Assert.Null(FileCache.Get("8.8.8.8", 3600, "v2:b"));
            Assert.Null(FileCache.Get("8.8.8.8", 3600));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", previous);
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task FetcherTimeoutSurfacesAsFailure()
    {
        var fetcher = new FakeFetcher(_ => throw new ProviderTimeoutException("timed out"));
        var config = Config();
        var engine = new MultiProviderEngine(config,
        [
            new Infrastructure.Providers.IpWhoIsProvider(5, fetcher),
        ]);
        await Assert.ThrowsAsync<ProviderException>(() => engine.AnalyzeAsync("8.8.8.8"));
        Assert.NotEmpty(fetcher.Urls);
        Assert.StartsWith("https://", fetcher.Urls[0]);
    }

    [Fact]
    public async Task Fetcher429SurfacesAsFailure()
    {
        var fetcher = new FakeFetcher(_ => throw new RateLimitException("limited"));
        var config = Config();
        var engine = new MultiProviderEngine(config,
        [
            new Infrastructure.Providers.IpApiCoProvider(5, fetcher),
        ]);
        await Assert.ThrowsAsync<ProviderException>(() => engine.AnalyzeAsync("8.8.8.8"));
    }

    [Fact]
    public async Task MalformedPayloadRejectedByAdapter()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("\"just-a-string\"");
        Assert.Throws<BadResponseException>(() =>
            Infrastructure.Providers.IpApiCoProvider.Normalize("8.8.8.8", doc.RootElement.Clone()));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Ipv4AndIpv6ThroughEngine()
    {
        var engine = EngineWith(("a", Sample.Result()));
        Assert.Equal(4, (await engine.AnalyzeAsync("8.8.8.8")).IpVersion);
    }

    [Fact]
    public async Task SelfIpDetection()
    {
        var fetcher = new FakeFetcher(_ => FakeFetcher.Json("""{"ip":"41.107.85.239"}"""));
        string ip = await SelfIp.DetectAsync(5, fetcher);
        Assert.Equal("41.107.85.239", ip);
    }

    [Fact]
    public async Task SelfIpBadPayload()
    {
        var fetcher = new FakeFetcher(_ => FakeFetcher.Json("""{"nope":true}"""));
        await Assert.ThrowsAsync<BadResponseException>(() => SelfIp.DetectAsync(5, fetcher));
    }
}
