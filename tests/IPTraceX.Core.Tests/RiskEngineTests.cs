using IPTraceX.Core;
using Xunit;

namespace IPTraceX.Core.Tests;

public sealed class RiskEngineTests
{
    private static AnonymityIntelligence Anon(
        DetectionStatus tor = DetectionStatus.NotDetected,
        DetectionStatus vpn = DetectionStatus.Unknown,
        DetectionStatus proxy = DetectionStatus.Unknown,
        DetectionStatus hosting = DetectionStatus.NotDetected)
    {
        AnonymitySignal Sig(DetectionStatus status, string source)
            => new(status, status == DetectionStatus.Unknown ? "UNKNOWN" : "HIGH",
                status == DetectionStatus.Unknown ? [] : [source], "test evidence");
        return new AnonymityIntelligence(
            Sig(tor, "tor-exits"), Sig(vpn, "abuseipdb"),
            Sig(proxy, "abuseipdb"), Sig(hosting, "cloud-ranges"));
    }

    [Fact]
    public void EmptyEvidenceIsUnknown()
    {
        RiskAssessment risk = RiskEngine.Evaluate(Anon(), null);
        Assert.Equal("UNKNOWN", risk.Level);
        Assert.Null(risk.Score);
        Assert.False(risk.HasSufficientEvidence);
        Assert.Empty(risk.Evidence);
    }

    [Fact]
    public void TorAloneScoresHigh()
    {
        RiskAssessment risk = RiskEngine.Evaluate(Anon(tor: DetectionStatus.Detected), null);
        Assert.Equal(35, risk.Score);
        Assert.Equal("LOW", risk.Level);
        Assert.Single(risk.Evidence);
        Assert.Equal("tor-exits", risk.Evidence[0].Source);
    }

    [Fact]
    public void CombinedEvidenceCapsAt100()
    {
        var anon = new AnonymityIntelligence(
            new AnonymitySignal(DetectionStatus.Detected, "HIGH", ["tor-exits"], "e"),
            new AnonymitySignal(DetectionStatus.Detected, "MEDIUM", ["abuseipdb"], "e"),
            new AnonymitySignal(DetectionStatus.Detected, "MEDIUM", ["abuseipdb"], "e"),
            new AnonymitySignal(DetectionStatus.Detected, "HIGH", ["cloud-ranges"], "e"));
        RiskAssessment risk = RiskEngine.Evaluate(anon, 100);
        Assert.Equal(100, risk.Score);
        Assert.Equal("CRITICAL", risk.Level);
        Assert.Equal(5, risk.Evidence.Count);
    }

    [Theory]
    [InlineData(0, "VERY LOW")]
    [InlineData(19, "VERY LOW")]
    [InlineData(20, "LOW")]
    [InlineData(39, "LOW")]
    [InlineData(40, "MEDIUM")]
    [InlineData(59, "MEDIUM")]
    [InlineData(60, "HIGH")]
    [InlineData(79, "HIGH")]
    [InlineData(80, "CRITICAL")]
    [InlineData(100, "CRITICAL")]
    public void LevelBoundaries(int score, string level)
    {
        Assert.Equal(level, RiskEngine.LevelFor(score));
    }

    [Fact]
    public void AbuseScoreScales()
    {
        RiskAssessment low = RiskEngine.Evaluate(Anon(), 4);
        Assert.Equal(1, low.Score);
        RiskAssessment high = RiskEngine.Evaluate(Anon(), 100);
        Assert.Equal(25, high.Score);
    }

    [Fact]
    public void CustomWeightsRespected()
    {
        var weights = RiskEngine.ParseWeights("tor=10,proxy=99,bogus=5");
        Assert.Equal(10, weights["tor"]);
        Assert.Equal(99, weights["proxy"]);
        Assert.Equal(15, weights["hosting"]);
        RiskAssessment risk = RiskEngine.Evaluate(Anon(tor: DetectionStatus.Detected), null, weights);
        Assert.Equal(10, risk.Score);
    }
}
