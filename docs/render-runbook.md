# Render Runbook (deploy / verify / rollback)

## Deploy

1. Render → New → Blueprint → select `render.yaml` in repo root.
2. Enter secrets (all `sync: false`, never committed):
   `IPTRACEX_AUDIT_INGEST_SECRET`,
   `IPTRACEX_DASHBOARD_SESSION_SECRET`,
   `IPTRACEX_DASHBOARD_USERS` (e.g. `alice:ADMIN,bob:VIEWER`),
   `IPTRACEX_DASHBOARD_PASSWORD_ALICE`, … (one per user).
3. Deploy. `preDeployCommand` runs `npm run migrate` (idempotent
   `IF NOT EXISTS` schema); a failed migration fails the deploy
   before the new code starts.
4. Check `/api/health` → 200 `{status:"ok",database:"ok"}`.
5. Point CLI clients:
   `IPTRACEX_AUDIT_ENABLED=1`,
   `IPTRACEX_AUDIT_ENDPOINT=https://<service>.onrender.com`,
   `IPTRACEX_AUDIT_SECRET=<ingest secret>`.

## Verify

- Ingest: valid single → 200, batch → 200, bad auth → 401,
  malformed → 400 (all covered by automated tests + manual curl).
- Login/logout/roles against the dashboard; sessions persist
  server-side (file dev / Postgres prod).
- CLI lookup → events visible in dashboard search within seconds.
- `rate limit → 429` fires at 120/min/IP (do not load-test production).

## Rollback

- Code: Render → service → Rollback to the previous successful deploy
  (or `git revert <commit>` + redeploy). Last known-good: `c365c7d`.
- Migrations are additive-only (`IF NOT EXISTS`, no drops/alters),
  so rolling code back never requires a DB downgrade.
- If a bad release breaks ingest: clients fail open automatically
  (local `AUDIT_SEND_ERROR`, results unaffected). To silence retries
  fleet-wide, unset `IPTRACEX_AUDIT_ENDPOINT` on clients.
- Dashboard-only rollback never touches CLI behavior.

## Backup / recovery

- Render PostgreSQL: automated backups + point-in-time recovery are
  platform features (availability depends on the database plan —
  verify on the Render dashboard for the provisioned instance; NOT
  verified from this environment — no Render access here).
- CLI-side audit JSONL is the secondary copy: any shipped event also
  exists in the originating machine's `logs/` until retention purges
  it, so recent history can be re-shipped if ever needed.
- Never delete production data to "test" recovery.

## Troubleshooting

- `Remote: ERROR` in `--security-status` → read `--logs --errors`
  (look for `AUDIT_SEND_ERROR` with HTTP status).
- Dashboard `503` from `/api/health` → `DATABASE_URL` wrong or DB
  down; CLI keeps working (fail-open).
- Empty dashboard → ingest secret mismatch (server logs 401s) or
  payload validation failures (400s with field details).
