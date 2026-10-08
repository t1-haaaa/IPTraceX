using System.Text.Json;
using System.Text.Json.Nodes;

namespace IPTraceX.Core;

/// <summary>AuditEvent JSON: snake_case contract shared with the Vercel ingest API.</summary>
public static class AuditJson
{
    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static JsonObject ToNode(AuditEvent e)
    {
        var node = new JsonObject
        {
            ["event_id"] = e.EventId,
            ["timestamp"] = e.TimestampUtc.ToString("O"),
            ["severity"] = e.Severity,
            ["event_type"] = e.EventType,
            ["category"] = e.Category,
        };
        void Put(string key, string? value)
        {
            if (value is not null)
            {
                node[key] = value;
            }
        }

        Put("operation", e.Operation);
        Put("component", e.Component);
        Put("status", e.Status);
        if (e.DurationMs.HasValue)
        {
            node["duration_ms"] = e.DurationMs.Value;
        }

        Put("session_id", e.SessionId);
        Put("correlation_id", e.CorrelationId);
        Put("investigation_id", e.InvestigationId);
        Put("target_type", e.TargetType);
        Put("target_reference", e.TargetReference);
        Put("provider", e.Provider);
        if (e.HttpStatus.HasValue)
        {
            node["http_status"] = e.HttpStatus.Value;
        }

        if (e.RetryCount.HasValue)
        {
            node["retry_count"] = e.RetryCount.Value;
        }

        Put("error_code", e.ErrorCode);
        Put("error_type", e.ErrorType);
        Put("message", e.Message);
        if (e.Metadata is not null && e.Metadata.Count != 0)
        {
            var meta = new JsonObject();
            foreach (var (key, value) in e.Metadata)
            {
                meta[key] = value;
            }

            node["metadata"] = meta;
        }

        return node;
    }

    public static string ToJsonLines(AuditEvent e)
        => ToNode(e).ToJsonString(Compact);

    /// <summary>Parse + validate one ingest payload. Throws UsageException listing problems.</summary>
    public static AuditEvent Parse(JsonObject node)
    {
        var errors = new List<string>();
        string? Str(string key) => node[key]?.GetValue<string?>();
        string eventId = Str("event_id") ?? AuditIds.NewEvent();
        string? severity = Str("severity");
        string? eventType = Str("event_type");
        string? category = Str("category") ?? (eventType is null ? null : AuditEventTypes.CategoryFor(eventType));
        if (!AuditSeverity.IsValid(severity))
        {
            errors.Add("severity must be one of TRACE/DEBUG/INFO/WARNING/ERROR/ALERT/CRITICAL");
        }

        if (!AuditEventTypes.IsKnown(eventType))
        {
            errors.Add("event_type is not in the controlled taxonomy");
        }

        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        if (node["timestamp"]?.GetValue<string?>() is string rawTs)
        {
            if (!DateTimeOffset.TryParse(rawTs, out timestamp))
            {
                errors.Add("timestamp is not a valid date-time");
            }
        }

        int? httpStatus = node["http_status"]?.GetValue<int?>();
        if (httpStatus is < 100 or > 599)
        {
            errors.Add("http_status must be 100-599");
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node["metadata"] is JsonObject meta)
        {
            if (meta.Count > 32)
            {
                errors.Add("metadata has too many entries (max 32)");
            }

            foreach (var (key, value) in meta)
            {
                string text = value?.GetValue<string?>() ?? "";
                if (text.Length > 512)
                {
                    errors.Add($"metadata['{key}'] exceeds 512 chars");
                    break;
                }

                metadata[key] = text;
            }
        }

        if (errors.Count != 0)
        {
            throw new UsageException("Invalid audit event: " + string.Join("; ", errors));
        }

        return new AuditEvent(
            eventId, timestamp, severity!, eventType!, category!,
            Str("operation"), Str("component"), Str("status"),
            node["duration_ms"]?.GetValue<long?>(),
            Str("session_id"), Str("correlation_id"), Str("investigation_id"),
            Str("target_type"), Str("target_reference"), Str("provider"),
            httpStatus, node["retry_count"]?.GetValue<int?>(),
            Str("error_code"), Str("error_type"), Str("message"), metadata);
    }

    public static AuditEvent ParseLine(string line)
    {
        JsonObject node;
        try
        {
            node = JsonNode.Parse(line)?.AsObject()
                ?? throw new UsageException("Invalid audit event: not an object.");
        }
        catch (JsonException)
        {
            throw new UsageException("Invalid audit event: malformed JSON.");
        }

        return Parse(node);
    }
}
