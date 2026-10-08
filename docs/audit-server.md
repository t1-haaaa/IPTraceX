# Audit Server (ingest API)

`POST /api/v1/events` in `apps/dashboard`.

## Contract

Accepts one event or `{"events": [...]}` matching `docs/event-schema.md`.
Validation: taxonomy, severity, timestamp, http_status range, metadata
count/size, 16KB/event, 200/batch, 256KB/body. Malformed → 400 with
field details; oversized → 413; no/bad Bearer →
401; burst → 429 (120/min/IP).

## Storage

`storeEvents`: Postgres when `DATABASE_URL` is set (parameterized
queries only, `ON CONFLICT DO NOTHING` dedupe by `event_id`,
bounded `LIMIT ≤ 500` reads, indexed columns), else local JSONL
append. Read APIs paginate (cursor = last `event_id`, newest-first)
and never dump tables.

## Monitoring the monitor

Auth failures, rate hits, invalid payloads/methods, storage errors,
and dashboard logins are themselves recorded; alert rules in
`docs/security-monitoring.md` apply to these too.
