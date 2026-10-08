using IPTraceX.Core;

namespace IPTraceX.Infrastructure;

/// <summary>
/// Local audit sink: daily JSONL files under logs/YYYY-MM-DD/audit-*.jsonl.
/// Bounded disk use (max MB per day-dir, retention purge). Secrets in
/// metadata are redacted before anything touches disk.
/// </summary>
public sealed class FileAuditSink
{
    private static readonly string[] SecretKeys =
    [
        "hibp-api-key", "authorization", "api_key", "apikey", "token",
        "secret", "cookie", "session", "password", "passwd", "audit-secret",
    ];

    private readonly string _root;
    private readonly long _maxBytes;
    private readonly int _retentionDays;
    private readonly object _gate = new();

    public FileAuditSink(string projectRoot, string? logDir = null, long maxMb = 100, int retentionDays = 30)
    {
        string dir = string.IsNullOrWhiteSpace(logDir)
            ? Path.Combine(projectRoot, "logs")
            : logDir;
        _root = Path.GetFullPath(dir);
        _maxBytes = Math.Max(1, maxMb) * 1024L * 1024L;
        _retentionDays = Math.Max(1, retentionDays);
    }

    public void Append(AuditEvent e)
    {
        string line = AuditJson.ToJsonLines(Redact(e)) + "\n";
        string day = e.TimestampUtc.ToString("yyyy-MM-dd");
        lock (_gate)
        {
            try
            {
                string dir = Path.Combine(_root, day);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, $"audit-{day}.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length + line.Length > _maxBytes)
                {
                    // Cap: stop growing today's file instead of failing the app.
                    return;
                }

                File.AppendAllText(path, line, System.Text.Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Local logging must never break the application.
            }
        }
    }

    public void PurgeOld(DateTimeOffset? now = null)
    {
        DateTimeOffset anchor = now ?? DateTimeOffset.UtcNow;
        if (!Directory.Exists(_root))
        {
            return;
        }

        foreach (string dir in Directory.GetDirectories(_root))
        {
            string name = Path.GetFileName(dir);
            if (DateTimeOffset.TryParseExact(
                    name, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out DateTimeOffset day)
                && (anchor - day).TotalDays > _retentionDays)
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>Read today's (or a day's) events with optional filters.</summary>
    public static IReadOnlyList<AuditEvent> Read(
        string projectRoot, string? day = null, string? logDir = null, int maxLines = 5000)
    {
        string root = string.IsNullOrWhiteSpace(logDir)
            ? Path.GetFullPath(Path.Combine(projectRoot, "logs"))
            : Path.GetFullPath(logDir);
        string target = day ?? DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");
        string path = Path.Combine(root, target, $"audit-{target}.jsonl");
        var events = new List<AuditEvent>();
        if (!File.Exists(path))
        {
            return events;
        }

        // ReadWrite share: the sink may append while we read.
        List<string> lines;
        try
        {
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            lines = [];
            while (reader.ReadLine() is string line)
            {
                lines.Add(line);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return events;
        }

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                events.Add(AuditJson.ParseLine(line));
            }
            catch (UsageException)
            {
                // Skip corrupt lines when reading; never crash the viewer.
            }

            if (events.Count >= maxLines)
            {
                break;
            }
        }

        return events;
    }

    internal static AuditEvent Redact(AuditEvent e)
    {
        if (e.Metadata is null || e.Metadata.Count == 0)
        {
            return e;
        }

        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in e.Metadata)
        {
            string lower = key.ToLowerInvariant();
            clean[key] = SecretKeys.Any(lower.Contains) ? "[REDACTED]" : value;
        }

        return e with { Metadata = clean };
    }
}
