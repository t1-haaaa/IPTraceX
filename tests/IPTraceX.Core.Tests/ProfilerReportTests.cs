using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class ProfilerReportTests
{
    private static AppConfig Config() => Sample.TestConfig();

    private static IntelligenceProfile Profile() => Sample.Profile();

    [Fact]
    public void ReportFormatsAndTraversal()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string format in new[] { "txt", "json", "html" })
            {
                string path = ReportService.SaveProfile(Profile(), format, dir);
                Assert.True(File.Exists(path));
                Assert.StartsWith(Path.GetFullPath(Path.Combine(dir, "reports")), Path.GetFullPath(path));
            }

            string txt = File.ReadAllText(Directory.GetFiles(
                Path.Combine(dir, "reports"), "*.txt", SearchOption.AllDirectories)[0]);
            Assert.Contains("Investigation Report", txt);
            Assert.Contains("RISK", txt);
            string html = File.ReadAllText(Directory.GetFiles(
                Path.Combine(dir, "reports"), "*.html", SearchOption.AllDirectories)[0]);
            Assert.Contains("<html", html.ToLowerInvariant());
            Assert.DoesNotContain("<script", html.ToLowerInvariant());
            Assert.Throws<UsageException>(() => ReportService.SaveProfile(Profile(), "xml", dir));
            Assert.Throws<UsageException>(() => ReportService.SaveProfile(Profile(), "md", dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ReportTextNeutralizesNewlines()
    {
        var profile = Sample.Profile();
        profile.Geo.Geolocation.Country = "Algeria\nINJECTED: yes";
        string text = ReportService.ToText(profile);
        Assert.DoesNotContain("\nINJECTED", text);
        Assert.Contains("Algeria INJECTED: yes", text);
    }

    [Fact]
    public void SafeNames()
    {
        Assert.Equal("8.8.8.8", ReportService.SafeName("8.8.8.8"));
        Assert.Equal("report", ReportService.SafeName(".."));
        Assert.True(ReportService.SafeName(new string('x', 200)).Length <= 64);
        Assert.DoesNotContain("/", ReportService.SafeName("a/b"));
    }

    [Fact]
    public void IntelProviderSelection()
    {
        var profiler = new IntelligenceProfiler(Config());
        var providers = profiler.BuildIntelProviders();
        Assert.Equal(6, providers.Count);
        Assert.DoesNotContain(providers, p => p.Descriptor.Id == "abuseipdb");

        var keyed = Sample.TestConfig();
        keyed.AbuseIpDbKey = "TESTKEY";
        var profilerKeyed = new IntelligenceProfiler(keyed);
        Assert.Contains(profilerKeyed.BuildIntelProviders(), p => p.Descriptor.Id == "abuseipdb");
    }

    [Fact]
    public void ProviderCatalogCoversAll()
    {
        Assert.True(ProviderCatalog.All.Count >= 9);
        Assert.All(ProviderCatalog.All, d =>
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Id));
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.NotEmpty(d.SupportedIpVersions);
        });
        Assert.NotNull(ProviderCatalog.Find("ripestat"));
        Assert.Null(ProviderCatalog.Find("nope"));
    }

    [Fact]
    public void RiskWeightsAndConcurrencyConfig()
    {
        var weights = RiskEngine.ParseWeights("tor=10");
        Assert.Equal(10, weights["tor"]);
        var cfg = Sample.TestConfig();
        Assert.Equal(4, cfg.MaxConcurrency);
        Assert.Equal(90.0, cfg.GlobalTimeoutSeconds);
    }
}
