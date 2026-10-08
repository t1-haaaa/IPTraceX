using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EmailEvidenceTests
{
    private static EmailProfile Profile(string risk = "UNKNOWN", int? score = null, int breaches = 0)
    {
        var baseProfile = Sample.EmailProfile();
        var riskAssessment = new RiskAssessment(
            score, risk,
            score.HasValue
                ? [new RiskEvidence("Disposable email", "MEDIUM", "email-domain", "test", 20, "test")]
                : [],
            score.HasValue);
        var breachList = Enumerable.Range(0, breaches)
            .Select(i => new BreachInfo($"Breach{i}", "example.com", "2024-05", ["Email addresses"], "HIBP"))
            .ToList();
        return baseProfile with
        {
            Risk = riskAssessment,
            Breaches = breachList,
        };
    }

    [Fact]
    public void CompareEmailInvestigationsDetectsChange()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileInvestigationStore(dir);
            var first = new Investigation(
                InvestigationId.New("EMX"), DateTimeOffset.UtcNow, AppInfo.Version,
                "a@gmail.com", "email", null, [], Investigation.CurrentSchema,
                EmailJson.FromEmailProfile(Profile()));
            var second = new Investigation(
                InvestigationId.New("EMX"), DateTimeOffset.UtcNow, AppInfo.Version,
                "b@gmail.com", "email", null, [], Investigation.CurrentSchema,
                EmailJson.FromEmailProfile(Profile("LOW", 20, 1)));
            store.Save(first);
            store.Save(second);

            var output = new StringWriter();
            var app = Sample.App(new FakeEngine(), "", new Palette(false), Sample.TestConfig(),
                null, output, new StringWriter(), emailProfiles: new FakeEmailEngine());
            var field = typeof(CliApp).GetField("_store",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field!.SetValue(app, store);
            Assert.Equal(0, app.CompareInvestigations(first.Id, second.Id));
            string text = output.ToString();
            Assert.Contains("CHANGE DETECTED", text);
            Assert.Contains("Risk", text);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EmailTimelineDetectsBreachChange()
    {
        var history = new List<(DateTimeOffset Timestamp, EmailProfile Profile)>
        {
            (new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), Profile()),
            (new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), Profile("LOW", 20, 1)),
        };
        List<string> changes = Evidence.EmailTimelineChanges(history);
        Assert.NotEmpty(changes);
        Assert.Contains(changes, c => c.Contains("Breaches") || c.Contains("Risk"));
    }

    [Fact]
    public void EmailSecretsNeverInJsonOrFiles()
    {
        var profile = Sample.EmailProfile();
        string json = EmailJson.ToJsonString(profile, indented: true);
        Assert.DoesNotContain("hibp-api-key", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Authorization", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Bearer", json, StringComparison.OrdinalIgnoreCase);

        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new FileInvestigationStore(dir);
            var investigation = new Investigation(
                InvestigationId.New("EMX"), DateTimeOffset.UtcNow, AppInfo.Version,
                profile.Target, "email", null, [], Investigation.CurrentSchema,
                EmailJson.FromEmailProfile(profile));
            store.Save(investigation);
            string file = Directory.GetFiles(
                Path.Combine(dir, "investigations"), "*.json", SearchOption.AllDirectories).Single();
            string content = File.ReadAllText(file);
            Assert.DoesNotContain("SECRET-KEY-123", content, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hibp-api-key", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EmailReportNeutralizesNewlines()
    {
        var profile = Sample.EmailProfile() with
        {
            Breaches = [new BreachInfo("Evil\nInjected", "example.com", "2024-05", ["Email addresses"], "HIBP")],
        };
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = ReportService.SaveEmailProfile(profile, "txt", dir);
            string content = File.ReadAllText(path);
            Assert.DoesNotContain("Evil\nInjected", content);
            Assert.Contains("Evil Injected", content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EmailReportPathTraversalContained()
    {
        var profile = Sample.EmailProfile();
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = ReportService.SaveEmailProfile(profile with { Target = "../../evil@gmail.com" }, "txt", dir);
            string fullReports = Path.GetFullPath(Path.Combine(dir, "reports"));
            Assert.StartsWith(fullReports, Path.GetFullPath(path), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EmailMaliciousIdsRejected()
    {
        Assert.False(InvestigationId.IsValid("../EMX-20261008-ABCDEF"));
        Assert.False(InvestigationId.IsValid("EMX-20261008-ABCDEF\nINJECTED"));
        Assert.ThrowsAny<TraceXException>(() => InvestigationId.RequireValid("EMX-evil"));
    }
}
