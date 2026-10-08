namespace IPTraceX.Core;

/// <summary>
/// Email-side consensus, confidence, reputation and risk. Same ladders
/// and honesty rules as the IP path, computed from email evidence.
/// </summary>
public static class EmailIntel
{
    public static IReadOnlyDictionary<string, int> DefaultEmailWeights() =>
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["disposable"] = 20,
            ["breach"] = 10,
            ["suspicious"] = 15,
            ["password"] = 30,
            ["authtoken"] = 35,
            ["hints"] = 20,
            ["recovery"] = 20,
        };

    public static Dictionary<string, int> ParseEmailWeights(string? raw)
    {
        var weights = new Dictionary<string, int>(DefaultEmailWeights(), StringComparer.OrdinalIgnoreCase);
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

    public static EmailReputation BuildReputation(
        bool? disposable,
        IReadOnlyList<BreachInfo> breaches,
        bool domainIsNew,
        bool hasMx)
    {
        var reasons = new List<string>();
        if (domainIsNew)
        {
            reasons.Add("newly registered domain");
        }

        if (!hasMx)
        {
            reasons.Add("domain publishes no MX records");
        }

        string suspicious = reasons.Count == 0 ? "NOT DETECTED" : "DETECTED";
        string confidence = reasons.Count == 0
            ? (disposable is null && breaches.Count == 0 ? "UNKNOWN" : "MEDIUM")
            : "MEDIUM";
        return new EmailReputation(
            disposable is true ? "DETECTED" : disposable is false ? "NOT DETECTED" : "UNKNOWN",
            breaches.Count,
            [.. breaches.Select(b => b.Name)],
            suspicious,
            [.. reasons],
            confidence,
            ["email-domain", "hibp"]);
    }

    public static RiskAssessment EvaluateRisk(
        bool? disposable,
        int breachCount,
        bool suspiciousDomain,
        IReadOnlyDictionary<string, int>? weights = null)
        => EvaluateRisk(disposable, breachCount, suspiciousDomain, null, weights);

    /// <summary>
    /// Evidence-driven email risk. No double counting: a password/token
    /// breach contributes its major weight INSTEAD of the generic breach
    /// weight; email/usernames inside that breach are supporting evidence.
    /// Hint/recovery contributions apply only when passwords are absent
    /// (covered by the password contribution otherwise).
    /// </summary>
    public static RiskAssessment EvaluateRisk(
        bool? disposable,
        int breachCount,
        bool suspiciousDomain,
        EmailSecurityExposure? security,
        IReadOnlyDictionary<string, int>? weights = null)
    {
        weights ??= DefaultEmailWeights();
        var evidence = new List<RiskEvidence>();
        int Weight(string key, int fallback)
            => weights.TryGetValue(key, out int value) ? value : fallback;
        if (disposable is true)
        {
            evidence.Add(new RiskEvidence(
                "Disposable email", "MEDIUM", "email-domain",
                "Disposable-mail service detected for this domain.",
                Weight("disposable", 20),
                "Temporary addresses correlate with abuse and throwaway use."));
        }

        bool token = security?.AuthData == ExposureStatus.Reported;
        bool password = security?.PasswordExposure == ExposureStatus.Reported;
        bool hints = !password
            && (security?.PasswordHints == ExposureStatus.Reported
                || security?.RecoveryExposure == ExposureStatus.Reported);
        if (token)
        {
            evidence.Add(new RiskEvidence(
                "Authentication data exposure", "CRITICAL", "hibp",
                "A breach reports auth tokens or session material for this address.",
                Weight("authtoken", 35),
                "Token theft enables session hijack without the password."));
        }

        if (password)
        {
            evidence.Add(new RiskEvidence(
                "Password exposure", "HIGH", "hibp",
                "A breach reports password data for this address (existence only; value never retrieved).",
                Weight("password", 30),
                "Exposed passwords enable credential-stuffing and takeover where reused."));
        }
        else if (breachCount > 0 && !token)
        {
            int extra = Math.Min(5, (breachCount - 1) * 5);
            evidence.Add(new RiskEvidence(
                "Breach exposure", "MEDIUM", "hibp",
                $"{breachCount} breache(s) expose this address (metadata only).",
                Weight("breach", 10) + extra,
                "Presence in breach corpora raises takeover/phishing exposure."));
        }

        if (hints && !password)
        {
            evidence.Add(new RiskEvidence(
                "Password hint exposure", "MEDIUM", "hibp",
                "A breach reports password hints or recovery data for this address.",
                Weight("hints", 20),
                "Hints and recovery answers help attackers guess credentials."));
        }

        if (suspiciousDomain)
        {
            evidence.Add(new RiskEvidence(
                "Suspicious domain", "MEDIUM", "email-domain",
                "Newly registered domain or no MX records published.",
                Weight("suspicious", 15),
                "Fresh or mail-incapable domains are common in abuse."));
        }

        if (evidence.Count == 0)
        {
            return new RiskAssessment(null, "UNKNOWN", [], false);
        }

        int total = Math.Min(100, evidence.Sum(e => e.Weight));
        return new RiskAssessment(total, RiskEngine.LevelFor(total), evidence, true);
    }

    public static string ConfidenceFor(int agreeing, int total, bool hasValue)
    {
        if (!hasValue || total == 0 || agreeing == 0)
        {
            return "unknown";
        }

        if (total == 1)
        {
            return "medium";
        }

        double ratio = (double)agreeing / total;
        return ratio >= 1.0 ? "high" : ratio >= 0.5 ? "medium" : "low";
    }
}
