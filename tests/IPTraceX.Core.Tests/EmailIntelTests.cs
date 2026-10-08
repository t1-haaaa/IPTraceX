using IPTraceX.Core;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class EmailIntelTests
{
    [Fact]
    public void RiskWeightsAndLevels()
    {
        var weights = EmailIntel.ParseEmailWeights("disposable=30,bogus=5");
        Assert.Equal(30, weights["disposable"]);
        Assert.Equal(10, weights["breach"]);
        Assert.Equal(15, weights["suspicious"]);

        RiskAssessment empty = EmailIntel.EvaluateRisk(null, 0, false);
        Assert.Equal("UNKNOWN", empty.Level);
        Assert.Null(empty.Score);

        RiskAssessment disposable = EmailIntel.EvaluateRisk(true, 0, false);
        Assert.Equal(20, disposable.Score);
        Assert.Equal("LOW", disposable.Level);

        RiskAssessment breach = EmailIntel.EvaluateRisk(false, 2, false);
        Assert.Equal(15, breach.Score);

        RiskAssessment combo = EmailIntel.EvaluateRisk(true, 3, true);
        Assert.Equal(20 + 15 + 15, combo.Score);
        Assert.Equal("MEDIUM", combo.Level);
    }

    [Fact]
    public void ReputationHonesty()
    {
        EmailReputation clean = EmailIntel.BuildReputation(false, [], false, true);
        Assert.Equal("NOT DETECTED", clean.Disposable);
        Assert.Equal("NOT DETECTED", clean.SuspiciousDomain);

        EmailReputation unknown = EmailIntel.BuildReputation(null, [], false, true);
        Assert.Equal("UNKNOWN", unknown.Disposable);

        EmailReputation sus = EmailIntel.BuildReputation(false, [], true, false);
        Assert.Equal("DETECTED", sus.SuspiciousDomain);
        Assert.Contains("no MX", string.Join(";", sus.SuspiciousReasons));
    }

    [Fact]
    public void ConfidenceLadder()
    {
        Assert.Equal("unknown", EmailIntel.ConfidenceFor(0, 0, true));
        Assert.Equal("unknown", EmailIntel.ConfidenceFor(0, 3, false));
        Assert.Equal("medium", EmailIntel.ConfidenceFor(1, 1, true));
        Assert.Equal("high", EmailIntel.ConfidenceFor(2, 2, true));
        Assert.Equal("medium", EmailIntel.ConfidenceFor(1, 2, true));
        Assert.Equal("low", EmailIntel.ConfidenceFor(1, 3, true));
    }
}
