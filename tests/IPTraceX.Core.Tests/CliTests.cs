using Xunit;
using IPTraceX.CLI;
using IPTraceX.Core;

namespace IPTraceX.Core.Tests;

public sealed class CliTests
{
    private static (CliApp App, StringWriter Out, StringWriter Err) Make(
        FakeEngine engine, string stdin = "", AppConfig? config = null,
        Exception? profileError = null)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var app = Sample.App(engine, stdin, new Palette(false), config, null, output, errors,
            profiles: null, profileError: profileError);
        return (app, output, errors);
    }

    [Fact]
    public async Task HelpExitsZero()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--help"]));
        Assert.Contains("IPTraceX", output.ToString());
    }

    [Fact]
    public async Task VersionOutput()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--version"]));
        Assert.Contains("IPTraceX", output.ToString());
        Assert.Contains(AppInfo.Version, output.ToString());
    }

    [Fact]
    public async Task JsonIsPureAndValid()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--json", "35.94.45.221"]));
        string text = output.ToString();
        Assert.Equal(-1, text.IndexOf("\u001b", StringComparison.Ordinal));
        using var doc = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal("35.94.45.221", doc.RootElement.GetProperty("ip").GetString());
        Assert.True(doc.RootElement.TryGetProperty("geoip_quality", out _));
    }

    [Fact]
    public async Task MapPrintsUrl()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--map", "35.94.45.221"]));
        Assert.Contains("https://www.google.com/maps?q=45.8398578,-119.7005791", output.ToString());
    }

    [Fact]
    public async Task InvalidIpCleanMessageAndExitCode()
    {
        var (app, _, errors) = Make(
            new FakeEngine(), profileError: new InvalidIpException("Invalid IP address."));
        Assert.Equal(3, await app.RunAsync(["not-an-ip"]));
        string err = errors.ToString();
        Assert.Contains("[ERROR] Invalid IP address.", err);
        Assert.DoesNotContain("x96", err);
    }

    [Fact]
    public async Task BatchDeduplicatesAndSummarizes()
    {
        var engine = new FakeEngine(ip =>
            ip == "bad" ? throw new InvalidIpException("Invalid IP address.") : Sample.Result());
        var (app, output, _) = Make(engine);
        int code = await app.RunAsync(["--file", WriteTempFile("a.txt", "35.94.45.221\n35.94.45.221\nbad\n")]);
        Assert.Equal(1, code);
        Assert.Equal(new[] { "35.94.45.221", "bad" }, engine.Calls);
        Assert.Contains("Completed:", output.ToString());
    }

    [Fact]
    public async Task StdinBatch()
    {
        var (app, output, _) = Make(new FakeEngine(), "35.94.45.221\n");
        Assert.Equal(0, await app.RunAsync(["--stdin"]));
        Assert.Contains("Completed: 1", output.ToString());
    }

    [Fact]
    public async Task UnknownOptionIsUsageError()
    {
        var (app, _, errors) = Make(new FakeEngine());
        Assert.Equal(2, await app.RunAsync(["--bogus"]));
        Assert.Contains("Unknown option", errors.ToString());
    }

    [Fact]
    public async Task InvalidTimeoutIsUsageError()
    {
        var (app, _, _) = Make(new FakeEngine());
        Assert.Equal(2, await app.RunAsync(["--timeout", "abc", "8.8.8.8"]));
    }

    [Fact]
    public async Task SelfDetectionNote()
    {
        var output = new StringWriter();
        var app = Sample.App(
            new FakeEngine(), "", new Palette(false), Sample.TestConfig(),
            _ => Task.FromResult("41.107.85.239"), output, new StringWriter());
        Assert.Equal(0, await app.RunAsync(["--self"]));
        Assert.Contains("public exit IP", output.ToString());
    }

    [Fact]
    public void SafeFilenamesAndTraversal()
    {
        Assert.Equal("8.8.8.8.json", CliApp.SafeFilename("8.8.8.8", ".json"));
        Assert.Equal("2001_4860_4860__8888.json", CliApp.SafeFilename("2001:4860:4860::8888", ".json"));
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string resolved = CliApp.SafeResolve(dir, CliApp.SafeFilename("8.8.8.8", ".json"));
            Assert.StartsWith(Path.GetFullPath(dir), resolved);
            // Even hostile input cannot escape: separators are stripped to a plain name.
            string hostile = CliApp.SafeResolve(dir, ".." + Path.DirectorySeparatorChar + "evil");
            Assert.StartsWith(Path.GetFullPath(dir), hostile);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task DomainFlowAnalyzesResolvedIps()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--domain", "example.com"]));
        string text = output.ToString();
        Assert.Contains("example.com", text);
        Assert.Contains("1.2.3.4", text);
    }

    [Fact]
    public async Task RdnsFlowShowsPtr()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--rdns", "35.94.45.221"]));
        string text = output.ToString();
        Assert.Contains("REVERSE DNS", text);
        Assert.Contains("ec2-35-94-45-221.example", text);
    }

    [Fact]
    public async Task ReportFlagSavesFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Sample.TestConfig();
            config.ProjectRoot = dir;
            var (app, output, _) = Make(new FakeEngine(), config: config);
            Assert.Equal(0, await app.RunAsync(["--report", "json", "35.94.45.221"]));
            Assert.Contains("Report saved to", output.ToString());
            Assert.True(Directory.GetFiles(Path.Combine(dir, "reports"), "*.json", SearchOption.AllDirectories).Length == 1);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task ReportFlagRejectsBadFormat()
    {
        var (app, _, errors) = Make(new FakeEngine());
        int code = await app.RunAsync(["--report", "xml", "35.94.45.221"]);
        Assert.NotEqual(0, code);
        Assert.Contains("Unsupported report format", errors.ToString());
    }

    [Fact]
    public async Task ProvidersFlagListsCatalog()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--providers"]));
        string text = output.ToString();
        Assert.Contains("PROVIDER HEALTH", text);
        Assert.Contains("ripestat", text);
        Assert.Contains("tor-exits", text);
        Assert.Contains("abuseipdb", text);
    }

    [Fact]
    public async Task MainMenuOffersAllFlows()
    {
        var (app, output, _) = Make(new FakeEngine(), "00\n");
        Assert.Equal(0, await app.RunInteractiveAsync(default));
        string text = output.ToString();
        foreach (string token in new[] { "[01]", "[02]", "[03]", "[04]", "[05]", "[06]", "[07]", "[08]", "[00]" })
        {
            Assert.Contains(token, text);
        }
    }

    [Fact]
    public async Task ProfileJsonKeepsLegacyKeys()
    {
        var (app, output, _) = Make(new FakeEngine());
        Assert.Equal(0, await app.RunAsync(["--json", "35.94.45.221"]));
        using var doc = System.Text.Json.JsonDocument.Parse(output.ToString());
        var root = doc.RootElement;
        foreach (string key in new[] { "ip", "ip_version", "geolocation", "network", "google_maps_url", "geoip_quality", "providers" })
        {
            Assert.True(root.TryGetProperty(key, out _), $"missing legacy key {key}");
        }

        foreach (string key in new[] { "target", "dns", "security", "risk", "asn", "consensus", "metadata" })
        {
            Assert.True(root.TryGetProperty(key, out _), $"missing new key {key}");
        }
    }

    [Fact]
    public void FriendlyErrors()
    {
        Assert.Contains("not a public routable IP",
            CliApp.FriendlyError(new NonPublicIpException("1.2.3.4 is not a public routable IP (private address).")));
        Assert.Contains("rate limit",
            CliApp.FriendlyError(new RateLimitException("API rate limit reached.")).ToLowerInvariant());
        Assert.Equal(3, CliApp.ExitFor(new InvalidIpException("x")));
        Assert.Equal(4, CliApp.ExitFor(new ProviderException("x")));
    }

    private static string WriteTempFile(string name, string content)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_" + name);
        File.WriteAllText(path, content);
        return path;
    }
}
