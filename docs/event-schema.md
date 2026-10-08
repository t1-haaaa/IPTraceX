# Event Schema

Shared contract between the C# emitter (`AuditJson`) and the Vercel
ingest API (`apps/dashboard/src/lib/events.ts`). Both sides validate
it; keep them in sync.

## Required fields

`severity` (TRACE/DEBUG/INFO/WARNING/ERROR/ALERT/CRITICAL),
`event_type` (controlled taxonomy below), `category`.

## Optional fields

`event_id` (server assigns if missing), `timestamp` (ISO-8601),
`operation`, `component`, `status`, `duration_ms`, `session_id`,
`correlation_id`, `investigation_id`, `target_type`,
`target_reference`, `provider`, `http_status` (100–599),
`retry_count`, `error_code`, `error_type`, `message`,
`metadata` (≤32 string entries, ≤512 chars each).

## Limits

Single event or batch (`{"events": [...]}`), max 200/batch,
16KB/event, 256KB/body.

## Event taxonomy

Application: APPLICATION_START/READY/EXIT/ERROR.
CLI: CLI_COMMAND/MENU_ACTION/INPUT/ERROR.
Validation: TARGET_VALIDATION/TARGET_REJECTED/INVALID_REQUEST.
Lookup: IP_LOOKUP_START/COMPLETE, DOMAIN_LOOKUP_START/COMPLETE,
DNS_QUERY, RDNS_QUERY, EMAIL_LOOKUP_START/COMPLETE, EMAIL_VALIDATION,
EMAIL_BREACH_CHECK, EMAIL_SECURITY_ALERT.
Providers: PROVIDER_QUERY_START/COMPLETE/ERROR, PROVIDER_TIMEOUT,
PROVIDER_RATE_LIMITED, PROVIDER_SKIPPED.
Intelligence: CONSENSUS_START/COMPLETE, CONFIDENCE_CALCULATION,
RISK_CALCULATION, EVIDENCE_BUILT.
Investigations: INVESTIGATION_CREATED/LOADED/SAVED/COMPARED/DELETED,
TIMELINE_GENERATED.
Reports: REPORT_GENERATION_START/COMPLETE/ERROR.
Cache: CACHE_HIT/MISS/WRITE/EXPIRED/INVALID.
Batch: BATCH_START/ITEM_START/ITEM_COMPLETE/ITEM_ERROR/COMPLETE.
Security: AUTH_SUCCESS/FAILURE, ACCESS_DENIED, RATE_LIMIT_TRIGGERED,
SUSPICIOUS_INPUT, INVALID_PAYLOAD/METHOD, UNEXPECTED_REQUEST,
SECURITY_ALERT.
System: CONFIG_LOAD/ERROR, FILE_ERROR, NETWORK_ERROR, INTERNAL_ERROR.
Audit: AUDIT_SEND_START/COMPLETE/ERROR, AUDIT_QUEUE_FULL.
Dashboard: DASHBOARD_LOGIN/LOGIN_FAILURE/LOGOUT, ALERT_ACKNOWLEDGED,
ALERT_RESOLVED.

Unknown types are rejected (400), never stored.
