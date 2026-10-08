using Xunit;
using IPTraceX.CLI;

namespace IPTraceX.Core.Tests;

public sealed class UiTests
{
    private static readonly Palette Plain = new(false);

    [Fact]
    public void BannerIsAsciiAndNarrow()
    {
        string text = Formatting.AsciiBanner(Plain);
        Assert.True(text.All(c => c < 128));
        Assert.Contains("MULTI-PROVIDER IP INTELLIGENCE", text);
        Assert.True(text.Split('\n').Max(line => line.Length) <= 80);
    }

    [Fact]
    public void NoGiantBoxesAnywhere()
    {
        string combined = string.Join("\n", new[]
        {
            Formatting.Startup(Plain, "1.0.0"),
            Formatting.FormatReport(Sample.Result(), Plain),
            Formatting.InteractiveMenu(Plain, true),
            Formatting.BatchHeader(Plain, 3),
            Formatting.BatchSummary(Plain, 3, 0),
        });
        foreach (char ch in new[] { '╔', '═', '║', '╚', '╝', '┌', '─', '│', '✓' })
        {
            Assert.DoesNotContain(ch.ToString(), combined);
        }
    }

    [Fact]
    public void NoAnsiWhenDisabled()
    {
        string combined = string.Join("\n", new[]
        {
            Formatting.Startup(Plain, "1.0.0"),
            Formatting.FormatReport(Sample.Result(), Plain),
            Formatting.InteractiveMenu(Plain, true),
        });
        Assert.Equal(-1, combined.IndexOf("\u001b", StringComparison.Ordinal));
    }

    [Fact]
    public void StartupShowsDeveloper()
    {
        string text = Formatting.Startup(Plain, "1.0.0");
        Assert.Contains("Developer: t1_haaa", text);
        Assert.Contains("Version 1.0.0", text);
    }

    [Fact]
    public void LauncherHeaderSuppressesDuplicateStartup()
    {
        Assert.False(Formatting.StartupShownByLauncher());
        string? previous = Environment.GetEnvironmentVariable("IPTraceX_LAUNCHER_UI");
        try
        {
            Environment.SetEnvironmentVariable("IPTraceX_LAUNCHER_UI", "1");
            Assert.True(Formatting.StartupShownByLauncher());
        }
        finally
        {
            Environment.SetEnvironmentVariable("IPTraceX_LAUNCHER_UI", previous);
        }
    }

    [Fact]
    public void MainMenuNumbering()
    {
        string menu = Formatting.MainMenu(Plain);
        foreach (string token in new[] { "[01]", "[02]", "[03]", "[04]", "[05]", "[06]", "[07]", "[08]", "[09]", "[10]", "[00]" })
        {
            Assert.Contains(token, menu);
        }

        Assert.Contains("Analyze IP", menu);
        Assert.Contains("Analyze domain", menu);
        Assert.Contains("Analyze email", menu);
        Assert.Contains("Investigations", menu);
    }

    [Fact]
    public void InvestigationsMenuNumbering()
    {
        string menu = Formatting.InvestigationsMenu(Plain);
        foreach (string token in new[] { "[01]", "[02]", "[03]", "[04]", "[05]", "[06]", "[00]" })
        {
            Assert.Contains(token, menu);
        }
    }

    [Fact]
    public void MenuNumberingAndSections()
    {
        string menu = Formatting.InteractiveMenu(Plain, true);
        foreach (string token in new[] { "[01]", "[02]", "[03]", "[04]", "[00]", "[?]", "[::]" })
        {
            Assert.Contains(token, menu);
        }

        string report = Formatting.FormatReport(Sample.Result(), Plain);
        foreach (string section in new[]
                 {
                     "[+] IP INFORMATION", "[+] GEOLOCATION", "[+] NETWORK",
                     "[+] GOOGLE MAPS", "[+] GEOIP QUALITY",
                 })
        {
            Assert.Contains(section, report);
        }

        Assert.Contains("Public", report);
        Assert.Contains("https://www.google.com/maps?q=45.8398578,-119.7005791", report);
        Assert.Contains("HIGH", report);
    }

    [Fact]
    public void BatchStyleAndSummaryContract()
    {
        Assert.Contains("[::]", Formatting.BatchHeader(Plain, 5));
        Assert.Equal("[01]", Formatting.BatchItem(Plain, 1));
        string summary = Formatting.BatchSummary(Plain, 3, 0);
        Assert.Contains("Completed: 3", summary);
        Assert.Contains("Failed: 0", summary);
    }

    [Fact]
    public void ProgressStagesKnown()
    {
        Assert.Contains("Validating", Formatting.StageLine(Plain, "validating"));
        Assert.Contains("Querying", Formatting.StageLine(Plain, "querying"));
        Assert.Contains("completed", Formatting.StageLine(Plain, "done")!.ToLowerInvariant());
        Assert.Null(Formatting.StageLine(Plain, "bogus-stage"));
    }

    [Fact]
    public void ColorsPresentWhenEnabled()
    {
        var vivid = new Palette(true);
        Assert.NotEqual(-1, Formatting.AsciiBanner(vivid).IndexOf("\u001b", StringComparison.Ordinal));
        Assert.NotEqual(-1, Formatting.FormatReport(Sample.Result(), vivid).IndexOf("\u001b", StringComparison.Ordinal));
    }
}
