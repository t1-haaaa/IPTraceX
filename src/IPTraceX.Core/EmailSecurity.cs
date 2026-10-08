namespace IPTraceX.Core;

/// <summary>Exposure states. NOT_REPORTED means "no known report", never "safe".</summary>
public static class ExposureStatus
{
    public const string Unknown = "UNKNOWN";
    public const string NotReported = "NOT_REPORTED";
    public const string Reported = "REPORTED";
}

/// <summary>Normalized breach data-class tokens (mapped only from real provider values).</summary>
public static class SecurityCategory
{
    public const string Passwords = "PASSWORDS";
    public const string PasswordHints = "PASSWORD_HINTS";
    public const string SecurityQuestions = "SECURITY_QUESTIONS";
    public const string AuthTokens = "AUTH_TOKENS";
    public const string SessionData = "SESSION_DATA";
    public const string RecoveryData = "RECOVERY_DATA";
    public const string Usernames = "USERNAMES";
    public const string EmailAddresses = "EMAIL_ADDRESSES";
    public const string PhoneNumbers = "PHONE_NUMBERS";
    public const string PhysicalAddresses = "PHYSICAL_ADDRESSES";
    public const string PaymentData = "PAYMENT_DATA";
    public const string Other = "OTHER";
}

/// <summary>Breach severity levels (evidence-based, never mere presence).</summary>
public static class BreachSeverity
{
    public const string Critical = "CRITICAL";
    public const string High = "HIGH";
    public const string Medium = "MEDIUM";
    public const string Low = "LOW";
    public const string None = "NONE";
}

/// <summary>Evidence-backed security exposure for one email target.</summary>
public sealed record EmailSecurityExposure(
    string BreachExposure,
    string PasswordExposure,
    string PasswordHints,
    string RecoveryExposure,
    string AuthData,
    int BreachCount,
    int PasswordBreachCount,
    int HintBreachCount,
    int TokenBreachCount,
    int OtherBreachCount,
    string Severity,
    bool ActionRequired,
    string[] Recommendations,
    string[] Sources);

/// <summary>
/// Breach-exposure analysis over safe metadata (data-class names only).
/// Never sees, stores, or returns secrets — providers only supply names.
/// </summary>
public static class BreachSecurity
{
    public static string NormalizeCategory(string? raw)
    {
        string text = (raw ?? "").Trim().ToLowerInvariant();
        return text switch
        {
            "passwords" => SecurityCategory.Passwords,
            "password hints" or "password hint" => SecurityCategory.PasswordHints,
            "security questions and answers" or "security questions"
                or "security question and answers" => SecurityCategory.SecurityQuestions,
            "auth tokens" or "auth token" or "authentication tokens"
                or "authentication token" or "api tokens" or "api keys" => SecurityCategory.AuthTokens,
            "session cookies" or "session cookie" or "cookies"
                or "sessions" => SecurityCategory.SessionData,
            "recovery emails" or "recovery email" or "account recovery"
                or "recovery information" => SecurityCategory.RecoveryData,
            "usernames" or "username" => SecurityCategory.Usernames,
            "email addresses" or "email address" or "emails" => SecurityCategory.EmailAddresses,
            "phone numbers" or "phone number" or "phone" => SecurityCategory.PhoneNumbers,
            "physical addresses" or "physical address" or "addresses" => SecurityCategory.PhysicalAddresses,
            "credit cards" or "credit card" or "payment methods"
                or "payment information" => SecurityCategory.PaymentData,
            _ => SecurityCategory.Other,
        };
    }

    public static IReadOnlyList<string> NormalizedCategories(IEnumerable<string> raw)
        => raw.Select(NormalizeCategory).Distinct(StringComparer.Ordinal).ToList();

    public static EmailSecurityExposure Analyze(
        IReadOnlyList<BreachInfo> breaches, bool sourceAvailable)
        => AnalyzeCategories(
            breaches.Select(b => (IReadOnlyList<string>)b.Categories).ToList(),
            sourceAvailable);

    /// <summary>Stored-payload path: same logic over serialized category lists.</summary>
    public static EmailSecurityExposure AnalyzeCategories(
        IReadOnlyList<IReadOnlyList<string>> categoriesPerBreach, bool sourceAvailable)
    {
        if (!sourceAvailable)
        {
            return Empty(ExposureStatus.Unknown, []);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<IReadOnlyList<string>>();
        foreach (IReadOnlyList<string> categories in categoriesPerBreach)
        {
            // Deduplicate repeat entries of the same breach shape.
            string key = string.Join("|", categories
                .Select(c => (c ?? "").Trim().ToLowerInvariant())
                .OrderBy(c => c, StringComparer.Ordinal));
            if (seen.Add(key))
            {
                unique.Add(categories);
            }
        }

        if (unique.Count == 0)
        {
            return Empty(ExposureStatus.NotReported, ["hibp"]);
        }

        var normalized = unique.Select(NormalizedCategories).ToList();
        int password = normalized.Count(c => c.Contains(SecurityCategory.Passwords));
        int hints = normalized.Count(c => c.Contains(SecurityCategory.PasswordHints));
        int recovery = normalized.Count(c =>
            c.Contains(SecurityCategory.SecurityQuestions) || c.Contains(SecurityCategory.RecoveryData));
        int tokens = normalized.Count(c =>
            c.Contains(SecurityCategory.AuthTokens) || c.Contains(SecurityCategory.SessionData));
        int other = normalized.Count(c =>
            !c.Contains(SecurityCategory.Passwords)
            && !c.Contains(SecurityCategory.PasswordHints)
            && !c.Contains(SecurityCategory.SecurityQuestions)
            && !c.Contains(SecurityCategory.RecoveryData)
            && !c.Contains(SecurityCategory.AuthTokens)
            && !c.Contains(SecurityCategory.SessionData));

        var all = normalized.SelectMany(c => c).Distinct().ToList();
        string severity = SeverityFor(all);
        var exposure = new EmailSecurityExposure(
            ExposureStatus.Reported,
            password > 0 ? ExposureStatus.Reported : ExposureStatus.NotReported,
            hints > 0 ? ExposureStatus.Reported : ExposureStatus.NotReported,
            recovery > 0 ? ExposureStatus.Reported : ExposureStatus.NotReported,
            tokens > 0 ? ExposureStatus.Reported : ExposureStatus.NotReported,
            unique.Count, password, hints, tokens, other,
            severity,
            severity is BreachSeverity.Critical or BreachSeverity.High or BreachSeverity.Medium,
            RecommendationsFor(
                password > 0, hints > 0, tokens > 0, recovery > 0,
                unique.Count > 0),
            ["hibp"]);
        return exposure;
    }

    /// <summary>
    /// Severity from actual data classes (presence alone never raises it):
    /// auth/session material = CRITICAL; passwords/hints/recovery = HIGH;
    /// usernames/phones/addresses/PII = MEDIUM; email only = LOW.
    /// </summary>
    public static string SeverityFor(IReadOnlyList<string> normalized)
    {
        if (normalized.Count == 0)
        {
            return BreachSeverity.None;
        }

        if (normalized.Contains(SecurityCategory.AuthTokens)
            || normalized.Contains(SecurityCategory.SessionData))
        {
            return BreachSeverity.Critical;
        }

        if (normalized.Contains(SecurityCategory.Passwords)
            || normalized.Contains(SecurityCategory.PasswordHints)
            || normalized.Contains(SecurityCategory.SecurityQuestions)
            || normalized.Contains(SecurityCategory.RecoveryData))
        {
            return BreachSeverity.High;
        }

        var pii = new[]
        {
            SecurityCategory.Usernames, SecurityCategory.PhoneNumbers,
            SecurityCategory.PhysicalAddresses, SecurityCategory.PaymentData,
            SecurityCategory.Other,
        };
        if (normalized.Any(pii.Contains))
        {
            return BreachSeverity.Medium;
        }

        return BreachSeverity.Low;
    }

    public static string[] RecommendationsFor(
        bool password, bool hints, bool tokens, bool recovery, bool anyBreach)
    {
        var items = new List<string>();
        if (tokens)
        {
            items.Add("Revoke active sessions and sign out of all devices.");
            items.Add("Revoke and rotate API tokens and app passwords.");
            items.Add("Review authorized applications and account activity.");
            items.Add("Enable multi-factor authentication.");
        }

        if (password)
        {
            items.Add("Change the affected password immediately.");
            items.Add("Change the same password anywhere it was reused.");
            items.Add("Enable multi-factor authentication.");
            items.Add("Review active sessions and sign out unknown devices.");
            items.Add("Check for suspicious account activity.");
            items.Add("Use a unique password; consider a password manager.");
        }

        if (hints || recovery)
        {
            items.Add("Change the password and update security questions and recovery info.");
            items.Add("Password hints help attackers: treat hint exposure like password exposure.");
        }

        if (!tokens && !password && !hints && !recovery && anyBreach)
        {
            items.Add("Be alert for phishing and password-reset lures.");
            items.Add("Enable multi-factor authentication where available.");
            items.Add("Use a unique password for this account.");
        }

        return [.. items];
    }

    private static EmailSecurityExposure Empty(string status, string[] sources)
        => new(status, status is ExposureStatus.Unknown ? status : ExposureStatus.NotReported,
            status is ExposureStatus.Unknown ? status : ExposureStatus.NotReported,
            status is ExposureStatus.Unknown ? status : ExposureStatus.NotReported,
            status is ExposureStatus.Unknown ? status : ExposureStatus.NotReported,
            0, 0, 0, 0, 0, BreachSeverity.None, false,
            RecommendationsFor(false, false, false, false, status == ExposureStatus.Reported),
            sources);
}
