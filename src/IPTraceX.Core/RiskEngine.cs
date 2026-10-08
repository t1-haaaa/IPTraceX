namespace IPTraceX.Core;

/// <summary>
/// Transparent, evidence-driven risk scoring. Every point carries its
/// indicator, severity, source, evidence, weight and explanation.
/// Default weights are documented and overridable via
/// IPTraceX_RISK_WEIGHTS ("tor=35,proxy=20,..."). Unknown when there is
/// no evidence at all — never manufactured certainty.
/// </summary>
public static class RiskEngine
{
    public static readonly IReadOnlyDictionary<string, int> DefaultWeights =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["tor"] = 35,
            ["proxy"] = 20,
            ["hosting"] = 15,
            ["vpn"] = 15,
            ["abuse"] = 25,
        };

    public static Dictionary<string, int> ParseWeights(string? raw)
    {
        var weights = new Dictionary<string, int>(DefaultWeights, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return weights;
        }

        foreach (string part in raw.Split(','))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            string key = kv[0].Trim().ToLowerInvariant();
            if (weights.ContainsKey(key)
                && int.TryParse(kv[1].Trim(), out int value)
                && value >= 0 && value <= 100)
            {
                weights[key] = value;
            }
        }

        return weights;
    }

    public static string LevelFor(int score)
        => score switch
        {
            < 0 => "UNKNOWN",
            < 20 => "VERY LOW",
            < 40 => "LOW",
            < 60 => "MEDIUM",
            < 80 => "HIGH",
            _ => "CRITICAL",
        };

    /// <summary>Score one profile's signals. Pure function of its inputs.</summary>
    public static RiskAssessment Evaluate(
        AnonymityIntelligence anonymity,
        int? abuseConfidenceScore,
        IReadOnlyDictionary<string, int>? weights = null)
    {
        weights ??= DefaultWeights;
        var evidence = new List<RiskEvidence>();

        int Weight(string key, int fallback)
            => weights.TryGetValue(key, out int value) ? value : fallback;

        if (anonymity.Tor.Status == DetectionStatus.Detected)
        {
            evidence.Add(new RiskEvidence(
                "Tor exit node", "HIGH",
                Join(anonymity.Tor.Sources),
                anonymity.Tor.Evidence,
                Weight("tor", 35),
                "IP appears on the official Tor Project bulk exit list."));
        }

        if (anonymity.Proxy.Status == DetectionStatus.Detected)
        {
            evidence.Add(new RiskEvidence(
                "Proxy detected", "MEDIUM",
                Join(anonymity.Proxy.Sources),
                anonymity.Proxy.Evidence,
                Weight("proxy", 20),
                "A reputation source classifies this IP as a proxy."));
        }

        if (anonymity.Vpn.Status == DetectionStatus.Detected)
        {
            evidence.Add(new RiskEvidence(
                "VPN detected", "MEDIUM",
                Join(anonymity.Vpn.Sources),
                anonymity.Vpn.Evidence,
                Weight("vpn", 15),
                "A reputation source classifies this IP as VPN infrastructure."));
        }

        if (anonymity.Hosting.Status == DetectionStatus.Detected)
        {
            evidence.Add(new RiskEvidence(
                "Hosting/datacenter", "LOW",
                Join(anonymity.Hosting.Sources),
                anonymity.Hosting.Evidence,
                Weight("hosting", 15),
                "IP falls inside an official cloud/hosting range, not eyeball space."));
        }
        else if (anonymity.Hosting is { Status: DetectionStatus.NotDetected }
                 && anonymity.Hosting.Sources.Length != 0)
        {
            // A checked-and-clear hosting signal is mild good news, not points.
        }

        if (abuseConfidenceScore.HasValue && abuseConfidenceScore.Value > 0)
        {
            int scaled = Math.Min(25, abuseConfidenceScore.Value / 4);
            evidence.Add(new RiskEvidence(
                "Abuse reports", abuseConfidenceScore.Value >= 75 ? "HIGH" : "MEDIUM",
                "AbuseIPDB",
                $"abuseConfidenceScore={abuseConfidenceScore.Value}",
                scaled,
                "Community abuse reports within the last 90 days."));
        }

        if (evidence.Count == 0)
        {
            return new RiskAssessment(null, "UNKNOWN", [], false);
        }

        int total = Math.Min(100, evidence.Sum(e => e.Weight));
        return new RiskAssessment(total, LevelFor(total), evidence, true);
    }

    private static string Join(string[] sources)
        => sources.Length == 0 ? "unknown source" : string.Join(", ", sources);
}
