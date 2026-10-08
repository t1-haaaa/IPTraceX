# Vercel Dashboard (repo overview)

The monitoring dashboard lives in `apps/dashboard` with its own
README (deploy, env, migrations). Summary for IPTraceX operators:

- Ingest: `POST /api/v1/events` — Bearer `IPTRACEX_AUDIT_INGEST_SECRET`,
  single or batch (≤200), schema-validated, rate-limited (120/min/IP).
  Responses: `{ok:true,received:N}` / `401` / `400` + details / `413` / `429`.
- Auth: cookie sessions persisted server-side (file or Postgres, never
  in-memory-only); roles ADMIN/ANALYST/VIEWER enforced server-side;
  ingest secret never reaches the browser.
- CLI delivery: `IPTRACEX_AUDIT_ENDPOINT` + `IPTRACEX_AUDIT_SECRET`
  (both env-only), bounded queue, batching, timeout, fail-open retry.
- See also: `docs/audit-logging.md`, `docs/event-schema.md`,
  `docs/security-monitoring.md`, `docs/dashboard-auth.md`,
  `docs/audit-server.md`.
