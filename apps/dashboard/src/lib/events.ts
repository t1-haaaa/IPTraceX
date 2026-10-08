// Event taxonomy mirror — keep in sync with src/IPTraceX.Core/AuditEvents.cs
export const SEVERITIES = [
  "TRACE",
  "DEBUG",
  "INFO",
  "WARNING",
  "ERROR",
  "ALERT",
  "CRITICAL",
] as const;

export const EVENT_TYPES = [
  "APPLICATION_START",
  "APPLICATION_READY",
  "APPLICATION_EXIT",
  "APPLICATION_ERROR",
  "CLI_COMMAND",
  "CLI_MENU_ACTION",
  "CLI_INPUT",
  "CLI_ERROR",
  "TARGET_VALIDATION",
  "TARGET_REJECTED",
  "INVALID_REQUEST",
  "IP_LOOKUP_START",
  "IP_LOOKUP_COMPLETE",
  "DOMAIN_LOOKUP_START",
  "DOMAIN_LOOKUP_COMPLETE",
  "DNS_QUERY",
  "RDNS_QUERY",
  "EMAIL_LOOKUP_START",
  "EMAIL_LOOKUP_COMPLETE",
  "EMAIL_VALIDATION",
  "EMAIL_BREACH_CHECK",
  "EMAIL_SECURITY_ALERT",
  "PROVIDER_QUERY_START",
  "PROVIDER_QUERY_COMPLETE",
  "PROVIDER_QUERY_ERROR",
  "PROVIDER_TIMEOUT",
  "PROVIDER_RATE_LIMITED",
  "PROVIDER_SKIPPED",
  "CONSENSUS_START",
  "CONSENSUS_COMPLETE",
  "CONFIDENCE_CALCULATION",
  "RISK_CALCULATION",
  "EVIDENCE_BUILT",
  "INVESTIGATION_CREATED",
  "INVESTIGATION_LOADED",
  "INVESTIGATION_SAVED",
  "INVESTIGATION_COMPARED",
  "INVESTIGATION_DELETED",
  "TIMELINE_GENERATED",
  "REPORT_GENERATION_START",
  "REPORT_GENERATION_COMPLETE",
  "REPORT_GENERATION_ERROR",
  "CACHE_HIT",
  "CACHE_MISS",
  "CACHE_WRITE",
  "CACHE_EXPIRED",
  "CACHE_INVALID",
  "BATCH_START",
  "BATCH_ITEM_START",
  "BATCH_ITEM_COMPLETE",
  "BATCH_ITEM_ERROR",
  "BATCH_COMPLETE",
  "AUTH_SUCCESS",
  "AUTH_FAILURE",
  "ACCESS_DENIED",
  "RATE_LIMIT_TRIGGERED",
  "SUSPICIOUS_INPUT",
  "INVALID_PAYLOAD",
  "INVALID_METHOD",
  "UNEXPECTED_REQUEST",
  "SECURITY_ALERT",
  "CONFIG_LOAD",
  "CONFIG_ERROR",
  "FILE_ERROR",
  "NETWORK_ERROR",
  "INTERNAL_ERROR",
  "AUDIT_SEND_START",
  "AUDIT_SEND_COMPLETE",
  "AUDIT_SEND_ERROR",
  "AUDIT_QUEUE_FULL",
  "DASHBOARD_LOGIN",
  "DASHBOARD_LOGIN_FAILURE",
  "DASHBOARD_LOGOUT",
  "ALERT_ACKNOWLEDGED",
  "ALERT_RESOLVED",
] as const;

const KNOWN = new Set<string>(EVENT_TYPES);

export interface ValidationError {
  field: string;
  message: string;
}

export interface ValidEvent {
  [key: string]: unknown;
}

const MAX_BODY_BYTES = 256 * 1024;
const MAX_BATCH = 200;

export function validateIngestBody(body: unknown): {
  events: ValidEvent[];
  errors: ValidationError[];
} {
  const errors: ValidationError[] = [];
  if (!body || typeof body !== "object") {
    return { events: [], errors: [{ field: "body", message: "body must be an object" }] };
  }
  const obj = body as Record<string, unknown>;
  const raw = Array.isArray(obj.events) ? obj.events : obj.event_id ? [obj] : null;
  if (!raw) {
    return { events: [], errors: [{ field: "events", message: "events array (or single event) required" }] };
  }
  if (raw.length > MAX_BATCH) {
    return { events: [], errors: [{ field: "events", message: `batch too large (max ${MAX_BATCH})` }] };
  }
  const events: ValidEvent[] = [];
  raw.forEach((item, i) => {
    const prefix = `events[${i}]`;
    if (!item || typeof item !== "object") {
      errors.push({ field: prefix, message: "must be an object" });
      return;
    }
    const e = item as Record<string, unknown>;
    if (typeof e.severity !== "string" || !SEVERITIES.includes(e.severity as never)) {
      errors.push({ field: `${prefix}.severity`, message: "invalid severity" });
      return;
    }
    if (typeof e.event_type !== "string" || !KNOWN.has(e.event_type)) {
      errors.push({ field: `${prefix}.event_type`, message: "unknown event type" });
      return;
    }
    if (e.timestamp !== undefined && isNaN(Date.parse(String(e.timestamp)))) {
      errors.push({ field: `${prefix}.timestamp`, message: "invalid timestamp" });
      return;
    }
    if (e.http_status !== undefined) {
      const code = Number(e.http_status);
      if (!Number.isInteger(code) || code < 100 || code > 599) {
        errors.push({ field: `${prefix}.http_status`, message: "must be 100-599" });
        return;
      }
    }
    if (e.metadata !== undefined) {
      if (typeof e.metadata !== "object" || e.metadata === null || Array.isArray(e.metadata)) {
        errors.push({ field: `${prefix}.metadata`, message: "must be an object" });
        return;
      }
      const keys = Object.keys(e.metadata);
      if (keys.length > 32) {
        errors.push({ field: `${prefix}.metadata`, message: "too many entries (max 32)" });
        return;
      }
      for (const k of keys) {
        const v = (e.metadata as Record<string, unknown>)[k];
        if (typeof v !== "string" || v.length > 512) {
          errors.push({ field: `${prefix}.metadata.${k}`, message: "must be a string <= 512 chars" });
          return;
        }
      }
    }
    const size = JSON.stringify(e).length;
    if (size > 16 * 1024) {
      errors.push({ field: prefix, message: "event too large (max 16KB)" });
      return;
    }
    events.push(e);
  });
  return { events, errors };
}

export { MAX_BODY_BYTES, MAX_BATCH };
