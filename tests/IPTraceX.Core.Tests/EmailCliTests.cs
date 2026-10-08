using IPTraceX.CLI;
using IPTraceX.Core;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EmailCliTests
{
    private static (CliApp App, StringWriter Out, StringWriter Err) Make(
        FakeEmailEngine? email = null, string stdin = "", AppConfig? config = null)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var app = Sample.App(
            new FakeEngine(), stdin, new Palette(false),
            config ?? Sample.TestConfig(), null, output, errors,
            emailProfiles: email ?? new FakeEmailEngine());
        return (app, output, errors);
    }

    [Fact]
    public async Task EmailDirectShowsProfile()
    {
        var (app, output, _) = Make();
        Assert.Equal(0, await app.RunAsync(["--email", "user@gmail.com"]));
        string text = output.ToString();
        Assert.Contains("EMAIL INTELLIGENCE", text);
        Assert.Contains("user@gmail.com", text);
        Assert.Contains("DOMAIN INTELLIGENCE", text);
    }

    [Fact]
    public async Task EmailInvalidRejected()
    {
        var (app, _, errors) = Make();
        Assert.Equal(3, await app.RunAsync(["--email", "not-an-email"]));
        Assert.Contains("Invalid email address", errors.ToString());
    }

    [Fact]
    public async Task EmailMissingValueIsUsage()
    {
        var (app, _, errors) = Make();
        Assert.Equal(2, await app.RunAsync(["--email"]));
        Assert.Contains("--email requires", errors.ToString());
    }

    [Fact]
    public async Task EmailJsonContract()
    {
        var (app, output, _) = Make();
        Assert.Equal(0, await app.RunAsync(["--json", "--email", "user@gmail.com"]));
        string text = output.ToString();
        Assert.Equal(-1, text.IndexOf((char)27));
        using var doc = System.Text.Json.JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.Equal("email", root.GetProperty("target_type").GetString());
        Assert.Equal("2.1", root.GetProperty("schema_version").GetString());
        foreach (string key in new[]
                 {
                     "email_domain", "dns", "avatar", "public_footprint",
                     "breach_intelligence", "reputation", "evidence",
                     "confidence", "risk", "providers", "investigation",
                 })
        {
            Assert.True(root.TryGetProperty(key, out _), $"missing {key}");
        }
    }

    [Fact]
    public async Task EmailBatchCounts()
    {
        var (app, output, _) = Make();
        int code = await app.RunEmailBatchAsync(
            ["user@gmail.com", "USER@GMAIL.COM", "bad", "", "# x"], false, default);
        Assert.Equal(1, code);
        string text = output.ToString();
        Assert.Contains("Processed: 2", text);
        Assert.Contains("Valid: 1", text);
        Assert.Contains("Invalid: 1", text);
    }

    [Fact]
    public async Task EmailBatchInvestigateSaves()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Sample.TestConfig();
            config.ProjectRoot = dir;
            var (app, output, _) = Make(config: config);
            Assert.Equal(0, await app.RunEmailBatchAsync(["user@gmail.com"], true, default));
            Assert.Contains("Investigation saved: EMX-", output.ToString());
            Assert.Single(Directory.GetFiles(
                Path.Combine(dir, "investigations"), "*.json", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task EmailInvestigateRoundtrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Sample.TestConfig();
            config.ProjectRoot = dir;
            var (app, output, _) = Make(config: config);
            Assert.Equal(0, await app.RunAsync(["--email", "user@gmail.com", "--investigate"]));
            var (listApp, listOut, _) = Make(config: config);
            Assert.Equal(0, await listApp.RunAsync(["--list-investigations"]));
            Assert.Contains("user@gmail.com", listOut.ToString());
            string id = listOut.ToString().Split()
                .First(t => t.StartsWith("EMX-", StringComparison.Ordinal));
            var (openApp, openOut, _) = Make(config: config);
            Assert.Equal(0, await openApp.RunAsync(["--investigation", id]));
            Assert.Contains("EMAIL INVESTIGATION", openOut.ToString());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task EmailReportFormats()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var config = Sample.TestConfig();
            config.ProjectRoot = dir;
            foreach (string format in new[] { "txt", "json", "html" })
            {
                var (app, output, _) = Make(config: config);
                Assert.Equal(0, await app.RunAsync(["--email", "user@gmail.com", "--report", format]));
                Assert.Contains("Report saved to", output.ToString());
            }

            Assert.Equal(3, Directory.GetFiles(
                Path.Combine(dir, "reports"), "*", SearchOption.AllDirectories).Length);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task EmailMenuFlow()
    {
        var (app, output, _) = Make(new FakeEmailEngine(), "03\nuser@gmail.com\n00\n00\n");
        Assert.Equal(0, await app.RunInteractiveAsync(default));
        Assert.Contains("EMAIL INTELLIGENCE", output.ToString());
    }

    [Fact]
    public async Task EmailNoColorPure()
    {
        var output = new StringWriter();
        var app = Sample.App(
            new FakeEngine(), "", new Palette(false), Sample.TestConfig(),
            null, output, new StringWriter(),
            emailProfiles: new FakeEmailEngine());
        Assert.Equal(0, await app.RunAsync(["--email", "user@gmail.com"]));
        Assert.Equal(-1, output.ToString().IndexOf((char)27));
    }
}
