using IPTraceX.CLI;
using IPTraceX.Core;
using IPTraceX.Infrastructure;
using IPTraceX.Infrastructure.Providers;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EmailSecurityTests
{
    private static BreachInfo Breach(string name, params string[] classes)
        => new(name, "example.com", "2024-05-01", classes, "HIBP");

    private static EmailProfile ProfileWith(params BreachInfo[] breaches)
    {
        var baseProfile = Sample.EmailProfile();
        EmailSecurityExposure security = BreachSecurity.Analyze(breaches, true);
        RiskAssessment risk = EmailIntel.EvaluateRisk(
            false, breaches.Length, false, security, EmailIntel.DefaultEmailWeights());
        return baseProfile with { Breaches = breaches, Risk = risk, Security = security };
    }

    [Fact]
    public void PasswordExposureDetected()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("BigBreach", "Email addresses", "Usernames", "Passwords")], true);
        Assert.Equal(ExposureStatus.Reported, exposure.BreachExposure);
        Assert.Equal(ExposureStatus.Reported, exposure.PasswordExposure);
        Assert.Equal(1, exposure.PasswordBreachCount);
        Assert.Equal(BreachSeverity.High, exposure.Severity);
        Assert.True(exposure.ActionRequired);
    }

    [Fact]
    public void PasswordExposureNotReported()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("MailList", "Email addresses", "Usernames")], true);
        Assert.Equal(ExposureStatus.Reported, exposure.BreachExposure);
        Assert.Equal(ExposureStatus.NotReported, exposure.PasswordExposure);
        Assert.Equal(BreachSeverity.Medium, exposure.Severity);
    }

    [Fact]
    public void PasswordExposureUnknownWithoutSource()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze([], false);
        Assert.Equal(ExposureStatus.Unknown, exposure.BreachExposure);
        Assert.Equal(ExposureStatus.Unknown, exposure.PasswordExposure);
        Assert.Equal(BreachSeverity.None, exposure.Severity);
        Assert.False(exposure.ActionRequired);
    }

    [Fact]
    public void PasswordHintsDetected()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("HintLeak", "Email addresses", "Password hints")], true);
        Assert.Equal(ExposureStatus.Reported, exposure.PasswordHints);
        Assert.Equal(ExposureStatus.NotReported, exposure.PasswordExposure);
        Assert.Equal(BreachSeverity.High, exposure.Severity);
        Assert.Contains(exposure.Recommendations,
            r => r.Contains("hint", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AuthTokenDetectedIsCritical()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("TokenLeak", "Email addresses", "Auth tokens")], true);
        Assert.Equal(ExposureStatus.Reported, exposure.AuthData);
        Assert.Equal(BreachSeverity.Critical, exposure.Severity);
        Assert.True(exposure.ActionRequired);
        Assert.Contains(exposure.Recommendations,
            r => r.Contains("Revoke", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MultipleBreachesSummarized()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
        [
            Breach("A", "Email addresses", "Passwords"),
            Breach("B", "Email addresses", "Password hints"),
            Breach("C", "Email addresses"),
            Breach("D", "Email addresses", "Usernames"),
            Breach("E", "Email addresses", "Auth tokens"),
        ], true);
        Assert.Equal(5, exposure.BreachCount);
        Assert.Equal(1, exposure.PasswordBreachCount);
        Assert.Equal(1, exposure.HintBreachCount);
        Assert.Equal(1, exposure.TokenBreachCount);
        Assert.Equal(2, exposure.OtherBreachCount);
        Assert.Equal(BreachSeverity.Critical, exposure.Severity);
    }

    [Fact]
    public void DuplicateBreachesDeduplicatedForCounts()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
        [
            Breach("Same", "Email addresses", "Passwords"),
            Breach("Same", "Passwords", "Email addresses"),
        ], true);
        Assert.Equal(1, exposure.BreachCount);
    }

    [Fact]
    public void PasswordRiskScored()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("BigBreach", "Email addresses", "Passwords")], true);
        RiskAssessment risk = EmailIntel.EvaluateRisk(
            false, 1, false, exposure, EmailIntel.DefaultEmailWeights());
        Assert.Equal(30, risk.Score);
        Assert.Contains(risk.Evidence, e => e.Indicator == "Password exposure");
    }

    [Fact]
    public void NoDoubleCountingSingleBreach()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("BigBreach", "Email addresses", "Usernames", "Passwords")], true);
        RiskAssessment risk = EmailIntel.EvaluateRisk(
            false, 1, false, exposure, EmailIntel.DefaultEmailWeights());
        Assert.Single(risk.Evidence);
        Assert.Equal(30, risk.Score);
    }

    [Fact]
    public void TokenRiskOutranksGenericBreach()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("T", "Email addresses", "Auth tokens"),
             Breach("U", "Email addresses")], true);
        RiskAssessment risk = EmailIntel.EvaluateRisk(
            false, 2, false, exposure, EmailIntel.DefaultEmailWeights());
        Assert.DoesNotContain(risk.Evidence, e => e.Indicator == "Breach exposure");
        Assert.Contains(risk.Evidence, e => e.Indicator == "Authentication data exposure");
    }

    [Fact]
    public void RecommendationsEvidenceDriven()
    {
        EmailSecurityExposure exposure = BreachSecurity.Analyze(
            [Breach("BigBreach", "Email addresses", "Passwords")], true);
        Assert.Contains(exposure.Recommendations,
            r => r.Contains("Change the affected password immediately.", StringComparison.Ordinal));
        Assert.Contains(exposure.Recommendations,
            r => r.Contains("multi-factor", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(exposure.Recommendations,
            r => r.Contains("hacked", StringComparison.OrdinalIgnoreCase)
                || r.Contains("compromised", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void JsonSecurityExposureContract()
    {
        string json = EmailJson.ToJsonString(
            ProfileWith(Breach("BigBreach", "Email addresses", "Passwords")), indented: true);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("security_exposure", out var sec));
        Assert.Equal("REPORTED", sec.GetProperty("breach_exposure").GetString());
        Assert.Equal("REPORTED", sec.GetProperty("password_exposure").GetString());
        Assert.Equal("NOT_REPORTED", sec.GetProperty("password_hints").GetString());
        Assert.Equal(1, sec.GetProperty("breach_count").GetInt32());
        Assert.Equal("HIGH", sec.GetProperty("severity").GetString());
        Assert.True(sec.GetProperty("action_required").GetBoolean());
        var breach = root.GetProperty("breach_intelligence")[0];
        Assert.True(breach.TryGetProperty("data_classes", out _));
        Assert.True(breach.GetProperty("password_exposure").GetBoolean());
    }

    [Fact]
    public void ReportContainsSecurityExposure()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = ReportService.SaveEmailProfile(
                ProfileWith(Breach("BigBreach", "Email addresses", "Passwords")), "txt", dir);
            string content = File.ReadAllText(path);
            Assert.Contains("SECURITY EXPOSURE", content);
            Assert.Contains("Password Exposure: REPORTED", content);
            Assert.Contains("Change the affected password immediately.", content);
            Assert.Contains("Have I Been Pwned", content);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void BreachValuesNeverSerialized()
    {
        // Even hostile category strings carry no secret values: only names.
        var breach = Breach("X", "Email addresses", "Passwords");
        string json = EmailJson.ToJsonString(ProfileWith(breach), indented: true);
        Assert.DoesNotContain("hunter123", json);
        Assert.DoesNotContain("hash:", json, StringComparison.OrdinalIgnoreCase);
        foreach (var prop in new[] { "password\"", "token\"", "cookie\"", "session\"" })
        {
            Assert.DoesNotContain($"\"{prop}", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void CliShowsPasswordWarning()
    {
        string text = Formatting.FormatEmailProfile(
            ProfileWith(Breach("BigBreach", "Email addresses", "Passwords")), new Palette(false));
        Assert.Contains("PASSWORD DATA EXPOSED", text);
        Assert.Contains("Password     : NOT SHOWN", text);
        Assert.Contains("does NOT retrieve or display", text);
        Assert.Contains("EMAIL SECURITY STATUS", text);
        Assert.DoesNotContain("hunter123", text);
    }

    [Fact]
    public void CliTokenWarningIsCritical()
    {
        string text = Formatting.FormatEmailProfile(
            ProfileWith(Breach("T", "Auth tokens")), new Palette(false));
        Assert.Contains("AUTHENTICATION DATA EXPOSED", text);
        Assert.Contains("REVOKE SESSIONS AND TOKENS", text);
    }

    [Fact]
    public void InvestigationPersistsSecuritySection()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var profile = ProfileWith(Breach("BigBreach", "Email addresses", "Passwords"));
            var store = new FileInvestigationStore(dir);
            var investigation = new Investigation(
                InvestigationId.New("EMX"), DateTimeOffset.UtcNow, AppInfo.Version,
                profile.Target, "email", null, [], Investigation.CurrentSchema,
                EmailJson.FromEmailProfile(profile));
            store.Save(investigation);
            Investigation loaded = store.Get(investigation.Id);
            Assert.NotNull(loaded.Email);
            Assert.Equal("REPORTED",
                loaded.Email!["security_exposure"]?["password_exposure"]?.GetValue<string>());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void TimelineSurfacesPasswordChange()
    {
        var clean = Sample.EmailProfile();
        var exposed = ProfileWith(Breach("BigBreach", "Passwords"));
        List<string> changes = Evidence.CompareEmailProfiles(clean, exposed)
            .Where(r => r.Changed)
            .Select(r => r.Field)
            .ToList();
        Assert.Contains("PasswordExposure", changes);
        var history = new List<(DateTimeOffset Timestamp, EmailProfile Profile)>
        {
            (new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), clean),
            (new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero), exposed),
        };
        Assert.Contains(Evidence.EmailTimelineChanges(history),
            c => c.Contains("PasswordExposure"));
    }

    [Fact]
    public async Task MissingApiKeyMeansUnknown()
    {
        var fetcher = new FakeFetcher(_ => FakeFetcher.Json("[]"));
        var provider = new HibpBreachProvider(5, fetcher, "");
        EmailEvidence evidence = await provider.InvestigateAsync(
            EmailValidation.Parse("user@gmail.com"), new EmailContext(5, null, null));
        var breach = Assert.IsType<BreachEvidence>(evidence);
        Assert.False(breach.Success);
        EmailSecurityExposure exposure = BreachSecurity.Analyze(breach.Breaches, false);
        Assert.Equal(ExposureStatus.Unknown, exposure.PasswordExposure);
    }

    [Fact]
    public async Task Hibp404MeansNotReported()
    {
        var fetcher = new FakeFetcher(_ => throw new NotFoundException("gone"));
        var provider = new HibpBreachProvider(5, fetcher, "KEY");
        EmailEvidence evidence = await provider.InvestigateAsync(
            EmailValidation.Parse("user@gmail.com"), new EmailContext(5, "KEY", null));
        var breach = Assert.IsType<BreachEvidence>(evidence);
        Assert.True(breach.Success);
        Assert.Empty(breach.Breaches);
        EmailSecurityExposure exposure = BreachSecurity.Analyze(breach.Breaches, true);
        Assert.Equal(ExposureStatus.NotReported, exposure.PasswordExposure);
    }

    [Fact]
    public async Task MalformedHibpResponseIsFailureNotCrash()
    {
        var fetcher = new FakeFetcher(_ => FakeFetcher.Json("[{\"NoName\":1},\"str\",42]"));
        var provider = new HibpBreachProvider(5, fetcher, "KEY");
        EmailEvidence evidence = await provider.InvestigateAsync(
            EmailValidation.Parse("user@gmail.com"), new EmailContext(5, "KEY", null));
        var breach = Assert.IsType<BreachEvidence>(evidence);
        Assert.True(breach.Success);
        Assert.Empty(breach.Breaches);
    }

    [Fact]
    public async Task ApiKeyNeverInErrors()
    {
        var fetcher = new FakeFetcher(_ => throw new AuthException("bad key"));
        var provider = new HibpBreachProvider(5, fetcher, "SUPER-SECRET-KEY-123");
        EmailEvidence evidence = await provider.InvestigateAsync(
            EmailValidation.Parse("user@gmail.com"), new EmailContext(5, "SUPER-SECRET-KEY-123", null));
        string json = System.Text.Json.JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("SUPER-SECRET-KEY-123", json);
        Assert.DoesNotContain("SUPER-SECRET-KEY-123", CliApp.FriendlyError(new AuthException("bad key")));
    }

    [Fact]
    public void SeverityLadder()
    {
        Assert.Equal(BreachSeverity.Low, BreachSecurity.SeverityFor(
            [SecurityCategory.EmailAddresses]));
        Assert.Equal(BreachSeverity.Medium, BreachSecurity.SeverityFor(
            [SecurityCategory.EmailAddresses, SecurityCategory.Usernames]));
        Assert.Equal(BreachSeverity.High, BreachSecurity.SeverityFor(
            [SecurityCategory.EmailAddresses, SecurityCategory.SecurityQuestions]));
        Assert.Equal(BreachSeverity.Critical, BreachSecurity.SeverityFor(
            [SecurityCategory.EmailAddresses, SecurityCategory.SessionData]));
        Assert.Equal(BreachSeverity.None, BreachSecurity.SeverityFor([]));
    }

    [Fact]
    public void CategoryNormalizationHonest()
    {
        Assert.Equal(SecurityCategory.Passwords, BreachSecurity.NormalizeCategory("Passwords"));
        Assert.Equal(SecurityCategory.PasswordHints, BreachSecurity.NormalizeCategory("Password hints"));
        Assert.Equal(SecurityCategory.Other, BreachSecurity.NormalizeCategory("Some future class"));
        Assert.Equal(SecurityCategory.Other, BreachSecurity.NormalizeCategory(null));
    }
}
