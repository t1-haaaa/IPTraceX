using IPTraceX.Core;
using IPTraceX.Infrastructure;
using IPTraceX.Infrastructure.Providers;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class IntelProviderTests
{
    private static ProviderContext Ctx() => new(5, "", null);

    private static System.Text.Json.JsonElement Doc(string raw)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task RipeStatNormalizes()
    {
        var fetcher = new FakeFetcher(url => url.Contains("network-info")
            ? FakeFetcher.Json("""{"data":{"prefix":"8.8.8.0/24","asns":["15169"]}}""")
            : FakeFetcher.Json("""{"data":{"records":[[{"key":"NetName","value":"GOGL"},{"key":"Organization","value":"Google LLC"},{"key":"Country","value":"US"}]]}}"""));
        var provider = new RipeStatProvider(5, fetcher);
        IntelEvidence evidence = await provider.InvestigateAsync("8.8.8.8", Ctx());
        var asn = Assert.IsType<AsnEvidence>(evidence);
        Assert.True(asn.Success);
        Assert.Equal("8.8.8.0/24", asn.Prefix);
        Assert.Equal(["AS15169"], asn.Asns);
        Assert.Equal("GOGL", asn.NetworkName);
        Assert.Equal("Google LLC", asn.Organization);
    }

    [Fact]
    public async Task DohReverseNameAndParsing()
    {
        Assert.Equal("8.8.8.8.in-addr.arpa",
            DohPtrProviderBase.ReverseName(System.Net.IPAddress.Parse("8.8.8.8")));
        string v6 = DohPtrProviderBase.ReverseName(System.Net.IPAddress.Parse("2a03:2880:15ff:54::"));
        Assert.EndsWith(".ip6.arpa", v6);
        Assert.DoesNotContain("::", v6);

        var fetcher = new FakeFetcher(_ => FakeFetcher.Json(
            """{"Status":0,"Answer":[{"name":"x","type":12,"data":"dns.google."}]}"""));
        var provider = new CloudflareDohProvider(5, fetcher);
        IntelEvidence evidence = await provider.InvestigateAsync("8.8.8.8", Ctx());
        var dns = Assert.IsType<DnsEvidence>(evidence);
        Assert.True(dns.Success);
        Assert.Equal(["dns.google"], dns.PtrHostnames);
        Assert.Contains("cloudflare-dns.com", fetcher.Urls[0]);
    }

    [Fact]
    public async Task TorListCachesAndMatches()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", dir);
            var fetcher = new FakeFetcher(
                _ => null,
                _ => "1.2.3.4\n# comment\n\nnot-an-ip\n5.6.7.8\n1.2.3.4\n");
            var provider = new TorExitsProvider(5, fetcher);
            IntelEvidence hit = await provider.InvestigateAsync("1.2.3.4", Ctx());
            var torHit = Assert.IsType<TorEvidence>(hit);
            Assert.True(torHit.Success);
            Assert.True(torHit.IsExitNode);
            Assert.Equal(2, torHit.ExitCount);
            IntelEvidence miss = await provider.InvestigateAsync("9.9.9.9", Ctx());
            Assert.False(Assert.IsType<TorEvidence>(miss).IsExitNode);
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
    public void CloudPrefixMatching()
    {
        Assert.True(CloudRangesProvider.PrefixContains(
            "13.32.0.0/15", System.Net.IPAddress.Parse("13.33.0.1")));
        Assert.False(CloudRangesProvider.PrefixContains(
            "13.32.0.0/15", System.Net.IPAddress.Parse("13.31.255.255")));
        Assert.False(CloudRangesProvider.PrefixContains(
            "13.32.0.0/15", System.Net.IPAddress.Parse("2a03:2880::1")));
        Assert.False(CloudRangesProvider.PrefixContains("bogus", System.Net.IPAddress.Parse("1.1.1.1")));
        Assert.Equal(24, CloudRangesProvider.PrefixLength("1.2.3.0/24"));
    }

    [Fact]
    public async Task CloudRangesUsesOfficialFeeds()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        try
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", dir);
            var fetcher = new FakeFetcher(
                _ => null,
                url => url.Contains("amazonaws")
                    ? """{"prefixes":[{"ip_prefix":"9.9.9.0/24","service":"CLOUDFRONT"}]}"""
                    : url.Contains("gstatic") ? """{"prefixes":[]}"""
                    : "9.9.9.0/24\n");
            var provider = new CloudRangesProvider(5, fetcher);
            IntelEvidence evidence = await provider.InvestigateAsync("9.9.9.9", Ctx());
            var cloud = Assert.IsType<CloudEvidence>(evidence);
            Assert.True(cloud.Success);
            Assert.Contains(cloud.Matches, m => m.Prefix == "9.9.9.0/24" && m.Provider == "AWS");
            Assert.True(fetcher.Urls.Exists(u => u.Contains("amazonaws.com")));
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
    public async Task AbuseIpDbSkippedWithoutKey()
    {
        var provider = new AbuseIpDbProvider(5, new FakeFetcher(_ => null), "");
        IntelEvidence evidence = await provider.InvestigateAsync(
            "8.8.8.8", new ProviderContext(5, "", null));
        var rep = Assert.IsType<ReputationEvidence>(evidence);
        Assert.False(rep.Success);
        Assert.Contains("no API key", rep.Error);
    }

    [Fact]
    public async Task AbuseIpDbParsesWithKey()
    {
        var fetcher = new FakeFetcher(_ => FakeFetcher.Json(
            """{"data":{"abuseConfidenceScore":85,"usageType":"VPN / Proxy","isp":"Example","countryCode":"US"}}"""));
        var provider = new AbuseIpDbProvider(5, fetcher, "TESTKEY");
        IntelEvidence evidence = await provider.InvestigateAsync("1.2.3.4", Ctx());
        var rep = Assert.IsType<ReputationEvidence>(evidence);
        Assert.True(rep.Success);
        Assert.Equal(85, rep.AbuseConfidenceScore);
        Assert.Equal(["VPN / Proxy"], rep.UsageTypes);
    }

    [Fact]
    public async Task OrchestratorBoundsConcurrencyAndIsolatesFailures()
    {
        var good = new LambdaIntel("good", new DnsEvidence("good", true, null, ["h.example"]));
        var bad = new LambdaIntel("bad", null, new ProviderException("boom"));
        var orchestrator = new IntelOrchestrator([good, bad]);
        IReadOnlyList<IntelEvidence> results = await orchestrator.RunAsync(
            "8.8.8.8", Ctx(), maxConcurrency: 1,
            globalTimeout: TimeSpan.FromSeconds(10));
        Assert.Equal(2, results.Count);
        Assert.True(results[0].Success);
        Assert.False(results[1].Success);
        Assert.Equal(ProviderHealthStatus.Ok, orchestrator.Health["good"].Status);
        Assert.Equal(ProviderHealthStatus.Failed, orchestrator.Health["bad"].Status);
    }

    private sealed class LambdaIntel(string id, IntelEvidence? ok, Exception? error = null) : IIntelProvider
    {
        public ProviderDescriptor Descriptor { get; } = new(
            id, id, ProviderCategory.Network, [4, 6], false, "", "");

        public Task<IntelEvidence> InvestigateAsync(
            string ip, ProviderContext context, CancellationToken cancellationToken)
        {
            if (error is not null)
            {
                throw error;
            }

            return Task.FromResult(ok!);
        }
    }
}
