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
}
