# Vercel Dashboard

Monitoring UI in `apps/dashboard` (Next.js 14 + TypeScript). It is
observability for the CLI, not a replacement.

## Run locally

```bash
cd apps/dashboard
cp .env.example .env   # never commit real values
npm install
npm run dev            # file-backed store, no database needed
```

## Deploy

Vercel project root: `apps/dashboard`. Required env:

| Variable | Purpose |
|----------|---------|
| `DATABASE_URL` | Postgres connection (production store) |
| `IPTRACEX_AUDIT_INGEST_SECRET` | Bearer secret for `POST /api/v1/events` |
| `IPTRACEX_DASHBOARD_SESSION_SECRET` | cookie session HMAC salt |
| `IPTRACEX_DASHBOARD_USERS` | `name:ROLE,...` (ADMIN/ANALYST/VIEWER) |
| `IPTRACEX_DASHBOARD_PASSWORD_<NAME>` | per-user dashboard passwords |

Without `DATABASE_URL` the app uses a local JSONL file (dev/preview).
Production uses Postgres: `npm run migrate` applies `schema.sql`
(tables: audit_events, sessions, investigations, provider_health,
security_alerts, dashboard_users, dashboard_audit, dashboard_sessions;
indexes on timestamp, type, severity, provider, investigation,
correlation, session, status).

## Pages

OVERVIEW (status + activity + recent) · LIVE EVENTS (poll 3s, filters,
cursor pagination) · event detail (+ related by correlation) ·
SECURITY · INVESTIGATIONS · PROVIDERS (success/timeout/latency) ·
REPORTS · SEARCH · SYSTEM · SETTINGS · LOGIN.

## Failure isolation

CLI works with the backend down (fail-open, verified end-to-end).
Dashboard degrades to its file store without a database.
