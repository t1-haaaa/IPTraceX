namespace IPTraceX.Core;

/// <summary>Structured audit event. Free-form text stays in Message only.</summary>
public sealed record AuditEvent(
    string EventId,
    DateTimeOffset TimestampUtc,
    string Severity,
    string EventType,
    string Category,
    string? Operation = null,
    string? Component = null,
    string? Status = null,
    long? DurationMs = null,
    string? SessionId = null,
    string? CorrelationId = null,
    string? InvestigationId = null,
    string? TargetType = null,
    string? TargetReference = null,
    string? Provider = null,
    int? HttpStatus = null,
    int? RetryCount = null,
    string? ErrorCode = null,
    string? ErrorType = null,
    string? Message = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Severity ladder for audit events and alerts.</summary>
public static class AuditSeverity
{
    public const string Trace = "TRACE";
    public const string Debug = "DEBUG";
    public const string Info = "INFO";
    public const string Warning = "WARNING";
    public const string Error = "ERROR";
    public const string Alert = "ALERT";
    public const string Critical = "CRITICAL";

    public static bool IsValid(string? value) => value is
        Trace or Debug or Info or Warning or Error or Alert or Critical;
}

/// <summary>Controlled event taxonomy. Unknown types are rejected at ingest.</summary>
public static class AuditEventTypes
{
    public const string ApplicationStart = "APPLICATION_START";
    public const string ApplicationReady = "APPLICATION_READY";
    public const string ApplicationExit = "APPLICATION_EXIT";
    public const string ApplicationError = "APPLICATION_ERROR";

    public const string CliCommand = "CLI_COMMAND";
    public const string CliMenuAction = "CLI_MENU_ACTION";
    public const string CliInput = "CLI_INPUT";
    public const string CliError = "CLI_ERROR";

    public const string TargetValidation = "TARGET_VALIDATION";
    public const string TargetRejected = "TARGET_REJECTED";
    public const string InvalidRequest = "INVALID_REQUEST";

    public const string IpLookupStart = "IP_LOOKUP_START";
    public const string IpLookupComplete = "IP_LOOKUP_COMPLETE";

    public const string DomainLookupStart = "DOMAIN_LOOKUP_START";
    public const string DomainLookupComplete = "DOMAIN_LOOKUP_COMPLETE";
    public const string DnsQuery = "DNS_QUERY";
    public const string RdnsQuery = "RDNS_QUERY";

    public const string EmailLookupStart = "EMAIL_LOOKUP_START";
    public const string EmailLookupComplete = "EMAIL_LOOKUP_COMPLETE";
    public const string EmailValidation = "EMAIL_VALIDATION";
    public const string EmailBreachCheck = "EMAIL_BREACH_CHECK";
    public const string EmailSecurityAlert = "EMAIL_SECURITY_ALERT";

    public const string ProviderQueryStart = "PROVIDER_QUERY_START";
    public const string ProviderQueryComplete = "PROVIDER_QUERY_COMPLETE";
    public const string ProviderQueryError = "PROVIDER_QUERY_ERROR";
    public const string ProviderTimeout = "PROVIDER_TIMEOUT";
    public const string ProviderRateLimited = "PROVIDER_RATE_LIMITED";
    public const string ProviderSkipped = "PROVIDER_SKIPPED";

    public const string ConsensusStart = "CONSENSUS_START";
    public const string ConsensusComplete = "CONSENSUS_COMPLETE";
    public const string ConfidenceCalculation = "CONFIDENCE_CALCULATION";
    public const string RiskCalculation = "RISK_CALCULATION";
    public const string EvidenceBuilt = "EVIDENCE_BUILT";

    public const string InvestigationCreated = "INVESTIGATION_CREATED";
    public const string InvestigationLoaded = "INVESTIGATION_LOADED";
    public const string InvestigationSaved = "INVESTIGATION_SAVED";
    public const string InvestigationCompared = "INVESTIGATION_COMPARED";
    public const string InvestigationDeleted = "INVESTIGATION_DELETED";
    public const string TimelineGenerated = "TIMELINE_GENERATED";

    public const string ReportGenerationStart = "REPORT_GENERATION_START";
    public const string ReportGenerationComplete = "REPORT_GENERATION_COMPLETE";
    public const string ReportGenerationError = "REPORT_GENERATION_ERROR";

    public const string CacheHit = "CACHE_HIT";
    public const string CacheMiss = "CACHE_MISS";
    public const string CacheWrite = "CACHE_WRITE";
    public const string CacheExpired = "CACHE_EXPIRED";
    public const string CacheInvalid = "CACHE_INVALID";

    public const string BatchStart = "BATCH_START";
    public const string BatchItemStart = "BATCH_ITEM_START";
    public const string BatchItemComplete = "BATCH_ITEM_COMPLETE";
    public const string BatchItemError = "BATCH_ITEM_ERROR";
    public const string BatchComplete = "BATCH_COMPLETE";

    public const string AuthSuccess = "AUTH_SUCCESS";
    public const string AuthFailure = "AUTH_FAILURE";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string RateLimitTriggered = "RATE_LIMIT_TRIGGERED";
    public const string SuspiciousInput = "SUSPICIOUS_INPUT";
    public const string InvalidPayload = "INVALID_PAYLOAD";
    public const string InvalidMethod = "INVALID_METHOD";
    public const string UnexpectedRequest = "UNEXPECTED_REQUEST";
    public const string SecurityAlert = "SECURITY_ALERT";

    public const string ConfigLoad = "CONFIG_LOAD";
    public const string ConfigError = "CONFIG_ERROR";
    public const string FileError = "FILE_ERROR";
    public const string NetworkError = "NETWORK_ERROR";
    public const string InternalError = "INTERNAL_ERROR";

    public const string AuditSendStart = "AUDIT_SEND_START";
    public const string AuditSendComplete = "AUDIT_SEND_COMPLETE";
    public const string AuditSendError = "AUDIT_SEND_ERROR";
    public const string AuditQueueFull = "AUDIT_QUEUE_FULL";

    public const string DashboardLogin = "DASHBOARD_LOGIN";
    public const string DashboardLoginFailure = "DASHBOARD_LOGIN_FAILURE";
    public const string DashboardLogout = "DASHBOARD_LOGOUT";
    public const string AlertAcknowledged = "ALERT_ACKNOWLEDGED";
    public const string AlertResolved = "ALERT_RESOLVED";

    private static readonly HashSet<string> All = new(
        typeof(AuditEventTypes).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => (string)f.GetValue(null)!),
        StringComparer.Ordinal);

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);

    public static string CategoryFor(string eventType)
    {
        string prefix = eventType.Contains('_')
            ? eventType[..eventType.IndexOf('_')]
            : eventType;
        return prefix switch
        {
            "APPLICATION" => "application",
            "CLI" => "cli",
            "TARGET" or "INVALID" => "validation",
            "IP" or "DOMAIN" or "DNS" or "RDNS" => "lookup",
            "EMAIL" => "email",
            "PROVIDER" => "provider",
            "CONSENSUS" or "CONFIDENCE" or "RISK" or "EVIDENCE" => "intelligence",
            "INVESTIGATION" or "TIMELINE" => "investigation",
            "REPORT" => "report",
            "CACHE" => "cache",
            "BATCH" => "batch",
            "AUTH" or "ACCESS" or "RATE" or "SUSPICIOUS" or "SECURITY"
                or "UNEXPECTED" => "security",
            "CONFIG" or "FILE" or "NETWORK" or "INTERNAL" => "system",
            "AUDIT" => "audit",
            "DASHBOARD" or "ALERT" => "dashboard",
            _ => "other",
        };
    }
}

/// <summary>Session/correlation/event ID generation (no traversal-unsafe chars).</summary>
public static class AuditIds
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public static string NewSession()
        => $"SES-{DateTimeOffset.UtcNow:yyyyMMdd}-{Suffix(6)}";

    public static string NewCorrelation()
        => $"COR-{Suffix(6)}";

    public static string NewEvent()
        => $"EVT-{Guid.NewGuid():N}";

    private static string Suffix(int length)
    {
        byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
        char[] chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
