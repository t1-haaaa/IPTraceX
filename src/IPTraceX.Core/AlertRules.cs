namespace IPTraceX.Core;

/// <summary>Fired security alert (evidence trail, never a bare claim).</summary>
public sealed record SecurityAlert(
    string AlertId,
    DateTimeOffset TimestampUtc,
    string Rule,
    string Severity,
    string Reason,
    int Count,
    string? Source = null,
    string? CorrelationId = null,
    string Status = "NEW");

/// <summary>
/// Evidence-based alert rules evaluated over recent audit events.
/// Pure function over (rule, window) — ordinary failures never qualify.
/// </summary>
public static class AlertRules
{
    public const string AuthFailures = "AUTH_FAILURES_10_IN_60S";
    public const string InvalidRequests = "INVALID_REQUESTS_100_IN_60S";
    public const string PathTraversal = "PATH_TRAVERSAL_PATTERN";
    public const string InvalidMethods = "INVALID_METHODS_REPEATED";
    public const string RemoteOffline = "REMOTE_AUDIT_OFFLINE";
    public const string ProviderWarning = "PROVIDER_TIMEOUT_SINGLE";

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    public static IReadOnlyList<SecurityAlert> Evaluate(
        IReadOnlyList<AuditEvent> recent, DateTimeOffset now)
    {
        var alerts = new List<SecurityAlert>();
        IReadOnlyList<AuditEvent> window = recent
            .Where(e => now - e.TimestampUtc <= Window && e.TimestampUtc <= now)
            .ToList();

        int authFailures = window.Count(e =>
            e.EventType is AuditEventTypes.AuthFailure or AuditEventTypes.DashboardLoginFailure);
        if (authFailures >= 10)
        {
            alerts.Add(new SecurityAlert(NewId(), now, AuthFailures,
                AuditSeverity.Alert,
                $"{authFailures} authentication failures in 60 seconds.",
                authFailures));
        }

        int invalid = window.Count(e =>
            e.EventType is AuditEventTypes.InvalidRequest or AuditEventTypes.InvalidPayload
                or AuditEventTypes.InvalidMethod or AuditEventTypes.UnexpectedRequest);
        if (invalid >= 100)
        {
            alerts.Add(new SecurityAlert(NewId(), now, InvalidRequests,
                AuditSeverity.Alert,
                $"{invalid} invalid requests in 60 seconds.",
                invalid));
        }

        int traversal = window.Count(e =>
            e.EventType == AuditEventTypes.SuspiciousInput
            && (e.Message?.Contains("traversal", StringComparison.OrdinalIgnoreCase) == true
                || e.ErrorCode == "PATH_TRAVERSAL"));
        if (traversal >= 3)
        {
            alerts.Add(new SecurityAlert(NewId(), now, PathTraversal,
                AuditSeverity.Alert,
                $"{traversal} path-traversal patterns in 60 seconds.",
                traversal));
        }

        int badMethods = window.Count(e => e.EventType == AuditEventTypes.InvalidMethod);
        if (badMethods >= 20)
        {
            alerts.Add(new SecurityAlert(NewId(), now, InvalidMethods,
                AuditSeverity.Warning,
                $"{badMethods} invalid API methods in 60 seconds.",
                badMethods));
        }

        return alerts;
    }

    /// <summary>Operational (non-attack) notices: remote audit down, provider timeout.</summary>
    public static SecurityAlert? Operational(string rule, string reason, int count, DateTimeOffset now)
        => rule switch
        {
            RemoteOffline => new SecurityAlert(NewId(), now, rule,
                AuditSeverity.Warning, reason, count),
            ProviderWarning => new SecurityAlert(NewId(), now, rule,
                AuditSeverity.Warning, reason, count),
            _ => null,
        };

    private static string NewId() => $"ALT-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
}
