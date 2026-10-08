using Xunit;
using IPTraceX.Core;
using IPTraceX.Infrastructure;

namespace IPTraceX.Integration.Tests;

/// <summary>
/// Live provider checks. Skipped by default; run with IPTraceX_LIVE=1.
/// Never assert exact geo values (they change); assert shape and sanity.
/// </summary>
public sealed class LiveProviderTests
{
    private static bool LiveEnabled()
        => Environment.GetEnvironmentVariable("IPTRACEX_LIVE") == "1";

    private static AppConfig Config() => new()
    {
        TimeoutSeconds = 15,
        CacheTtlSeconds = 0,
    };

    [SkippableTheory]
    [InlineData("41.107.85.239", "DZ")]
    [InlineData("35.94.45.221", "US")]
    [InlineData("8.8.8.8", "US")]
    public async Task LiveConsensusSmoke(string ip, string expectedCode)
    {
        Skip.IfNot(LiveEnabled(), "Set IPTRACEX_LIVE=1 to run live provider tests.");
        var engine = new MultiProviderEngine(Config());
        GeoResult info = await engine.AnalyzeAsync(ip);
        Assert.Equal(expectedCode, info.Geolocation.CountryCode);
        Assert.True(info.ProvidersSuccessful >= 1);
        Assert.NotEmpty(info.Providers);
    }

    [SkippableFact]
    public async Task LiveIpv6Smoke()
    {
        Skip.IfNot(LiveEnabled(), "Set IPTRACEX_LIVE=1 to run live provider tests.");
        var engine = new MultiProviderEngine(Config());
        GeoResult info = await engine.AnalyzeAsync("2a03:2880:15ff:54::");
        Assert.Equal(6, info.IpVersion);
        Assert.NotNull(info.Geolocation.CountryCode);
    }

    [SkippableFact]
    public async Task LiveInvestigationRoundtrip()
    {
        Skip.IfNot(LiveEnabled(), "Set IPTRACEX_LIVE=1 to run live provider tests.");
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profiler = new IntelligenceProfiler(Config());
            IntelligenceProfile profile = await profiler.AnalyzeIpAsync("8.8.8.8");
            var investigation = new Investigation(
                InvestigationId.New(), DateTimeOffset.UtcNow, AppInfo.Version,
                profile.Geo.Ip, "ip", profile, [], Investigation.CurrentSchema);
            var store = new FileInvestigationStore(dir);
            store.Save(investigation);
            Investigation loaded = store.Get(investigation.Id);
            Assert.Equal("US", loaded.Profile!.Geo.Geolocation.CountryCode);
            Assert.NotEmpty(Evidence.BuildMatrix(loaded.Profile));
            Assert.Single(store.List());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [SkippableFact]
    public async Task LiveEmailDomainIntel()
    {
        Skip.IfNot(LiveEnabled(), "Set IPTRACEX_LIVE=1 to run live provider tests.");
        var profiler = new EmailProfiler(Config());
        EmailProfile profile = await profiler.AnalyzeEmailAsync("user@gmail.com");
        Assert.Equal("gmail.com", profile.Domain);
        Assert.NotEmpty(profile.DomainIntel.MxHosts);
        Assert.Contains(profile.DomainIntel.MxHosts, h => h.Contains("google", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(profile.DomainIntel.SpfRecord);
        Assert.StartsWith("v=spf1", profile.DomainIntel.SpfRecord, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(profile.DomainIntel.DmarcRecord);
        Assert.Equal("Google (Gmail / Workspace)", profile.DomainIntel.MailProvider);
        Assert.Equal("NOT DETECTED", profile.DomainIntel.DisposableStatus);
        Assert.True(profile.DomainIntel.IsFreeMail);
        // Avatar unknown is honest when no public avatar exists; only assert shape.
        Assert.True(profile.Avatar.Status is "FOUND" or "UNKNOWN");
        // No keys in live output either.
        Assert.DoesNotContain("Bearer", EmailJson.ToJsonString(profile));
    }
}
