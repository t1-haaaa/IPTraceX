# Audit Logging

IPTraceX emits structured audit events for every important operation:
lookups, validations, provider queries, consensus/confidence/risk,
investigations, reports, batch runs, cache activity, and security
observations. Core intelligence never blocks on audit: local logging is
synchronous and cheap, remote shipping is async and fail-open.

## Event shape

`event_id, timestamp, severity, event_type, category, operation,
component, status, duration_ms, session_id, correlation_id,
investigation_id, target_type, target_reference, provider, http_status,
retry_count, error_code, error_type, message, metadata`.
See `docs/event-schema.md` for the full taxonomy.

## Correlation

- `session_id` (`SES-YYYYMMDD-XXXXXX`): one CLI execution.
- `correlation_id` (`COR-XXXXXX`): one operation within the session.
- `investigation_id` (`IPX-`/`EMX-`): links events to an investigation.

Dashboard navigation: Session → Correlation → Investigation → Events.

## Local logging

Daily JSONL under `logs/YYYY-MM-DD/audit-YYYY-MM-DD.jsonl`:

| Variable | Default | Purpose |
|----------|---------|---------|
| `IPTRACEX_AUDIT_ENABLED` | `1` | master switch (local + remote) |
| `IPTRACEX_LOG_DIR` | `<root>/logs` | log directory override |
| `IPTRACEX_LOG_MAX_MB` | `100` | cap per daily file (stops growing, never crashes) |
| `IPTRACEX_LOG_RETENTION_DAYS` | `30` | purge older day-dirs |

Secrets in metadata are redacted (`[REDACTED]`) before disk. Corrupt
lines are skipped by readers, never fatal.

## Remote logging

| Variable | Default | Purpose |
|----------|---------|---------|
| `IPTRACEX_AUDIT_ENDPOINT` | empty (disabled) | Vercel dashboard base URL |
| `IPTRACEX_AUDIT_SECRET` | empty | Bearer ingest secret (env only) |
| `IPTRACEX_AUDIT_PROJECT` / `IPTRACEX_AUDIT_INSTANCE` | empty | optional agent labels |
| `IPTRACEX_AUDIT_BATCH_SIZE` | `50` | events per POST (1–500) |
| `IPTRACEX_AUDIT_FLUSH_INTERVAL` | `5` | shipper cadence seconds (1–300) |
| `IPTRACEX_AUDIT_TIMEOUT` | `10` | per-request seconds (2–120) |

Bounded queue (1000), batch POST to `POST {endpoint}/api/v1/events`,
timeout + fail-open retry. Queue-full drops the remote copy but keeps
it locally and records `AUDIT_QUEUE_FULL`. If the backend is down,
`IP analysis: SUCCESS` while `Remote audit: OFFLINE` — results first.

Production (Render): point `IPTRACEX_AUDIT_ENDPOINT` at the Render web
service URL and `IPTRACEX_AUDIT_SECRET` at the ingest secret; both are
plain env vars, never committed. See `render.yaml` and
`apps/dashboard/README.md`.

## CLI viewer

```bash
./iptracex.sh --logs --today
./iptracex.sh --logs --errors
./iptracex.sh --logs --security
./iptracex.sh --logs --investigation EMX-20261008-A7F31C
./iptracex.sh --logs --provider RIPEstat
./iptracex.sh --logs --last 100
./iptracex.sh --logs --json --last 200
./iptracex.sh --security-status
```

`--logs --json` prints pure JSONL (safe with `--no-color` pipelines).
`--version`/`--help` stay quiet by design.
