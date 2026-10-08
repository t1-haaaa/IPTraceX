# Changelog

All notable changes to IPTraceX are documented here.
Format follows Keep a Changelog; versions follow SemVer.

## 2.3.0 — 2026-10-08

- feat: central audit logging — structured `AuditEvent` taxonomy
  (60+ types, 7 severities), session/correlation/investigation IDs,
  per-lookup provider + consensus/confidence/risk telemetry, local
  daily JSONL (`IPTRACEX_LOG_DIR/MAX_MB/RETENTION_DAYS`, secret
  redaction, corruption-tolerant reads), bounded async remote shipping
  (`IPTRACEX_AUDIT_ENDPOINT/SECRET`, batching, timeout, fail-open
  retry, queue-full accounting), `--logs` viewer (`--today/--errors/
  --security/--investigation/--provider/--last/--json`) and
  `--security-status` summary.
- feat: Vercel monitoring dashboard (`apps/dashboard`, Next.js+TS) —
  authenticated `POST /api/v1/events` ingest (Bearer, validation,
  rate limits), Postgres-or-file store with migrations, overview/
  live-events/event-detail/security/investigations/providers/reports/
  search/system/settings pages, role-based auth (ADMIN/ANALYST/VIEWER),
  3s polling + cursor pagination. Verified end-to-end (CLI → API →
  dashboard); Vercel cloud deploy itself left to release step.
- fix: launcher header test tracks `APP_VERSION` instead of a frozen
  string; audit file reader shares access with the live writer.
- deploy: Render Blueprint (`render.yaml` — dashboard web service +
  PostgreSQL, `DATABASE_URL` injection, idempotent `preDeployCommand`
  migration, `/api/health` checks, secrets via `sync: false`);
  no worker/cron (none required — documented); production CLI wiring
  via `IPTRACEX_AUDIT_ENDPOINT`/`IPTRACEX_AUDIT_SECRET`.

## 2.2.0 — 2026-10-08

- feat: email intelligence (public OSINT only) — `--email`, `--email-file`,
  `[03] Analyze email`: normalization + validation, domain/MX/SPF/DMARC/
  DNSSEC/RDAP intel, disposable + free-mail + mail-provider classification,
  public avatar (Gravatar), public footprint (GitHub handle/commit tiers),
  key-gated HIBP breach metadata, email reputation + transparent risk
  (`IPTRACEX_EMAIL_RISK_WEIGHTS`), explained confidence, `EMX-` investigations
  with compare/timeline/batch/reports, `"target_type": "email"` JSON.
  Keys (`IPTRACEX_HIBP_API_KEY`, `IPTRACEX_GITHUB_TOKEN`) are env-only and
  never stored; see `docs/email-intelligence.md`, `docs/email-providers.md`,
  `docs/email-risk.md`, `docs/email-privacy.md`.
- feat: email breach exposure alerts — normalized password/hint/token/
  recovery signals (`REPORTED`/`NOT_REPORTED`/`UNKNOWN`), breach severity
  (`CRITICAL`/`HIGH`/`MEDIUM`/`LOW`), `[!] PASSWORD DATA EXPOSED` and
  `[CRITICAL] AUTHENTICATION DATA EXPOSED` warnings, evidence-driven
  security recommendations, `EMAIL SECURITY STATUS` block, `security_exposure`
  JSON section, per-breach `data_classes` + `password_exposure`, report
  `SECURITY EXPOSURE` sections, password-exposure timeline/comparison
  fields, extended email risk (password +30 / authtoken +35 / hints +20,
  no double counting). Secret values never retrieved, displayed, logged,
  or stored.

## 2.1.0 — 2026-10-08

- feat: investigations (IPX-IDs, file store with atomic writes, open/
  list/delete/compare/timeline, batch integration, interactive submenu).
- feat: evidence matrix (SUPPORT/CONFLICT/MISSING/ERROR/CACHED) and
  explained per-field confidence with reasons and provider lists.
- feat: source reliability tiers (display + tie-break only).
- feat: freshness metadata (LIVE/CACHED + age) on provider details.
- feat: risk evidence confidence; reports embed ID/evidence/timeline.
- feat: JSON `schema_version` 2.1 plus `target`, `dns`, `security`,
  `risk`, `asn`, `consensus`, `metadata` sections (legacy keys frozen).
- feat: `--investigate`, `--investigation`, `--list-investigations`,
  `--delete-investigation`, `--compare`, `--compare-ip`, redesigned
  main menu with investigations + configuration entries.

## 1.1.0 — 2026-10-08

- feat: IP Intelligence 2.0 — full profiles (ASN, DNS, anonymity, risk),
  plugin provider framework (10 adapters: 3 geo, RIPEstat, 2× DoH, system
  DNS, Tor exits, cloud ranges, optional AbuseIPDB), bounded-concurrency
  orchestrator with health tracking, transparent risk engine, domain and
  reverse-DNS analysis, txt/json/html investigation reports, redesigned
  main menu, additive JSON sections (legacy keys frozen).
- feat: clean launch experience — terminal clear on interactive start,
  quiet official-release bootstrap (no curl progress, visible errors),
  developer branding (`[::] Developer: t1_haaa`), launcher `--debug`
  details, `IPTraceX_LAUNCHER_UI` de-duplication with the app header.

- fix: production launcher no longer publishes/executes a Linux ELF binary
  on Windows (clear unsupported-platform message instead); canonical binary
  name `IPTraceX` staged next to `iptracex.sh`; dev publish moved to
  `scripts/dev-publish-linux.sh`; `IPTRACEX_*` documented as official with
  `IPGHOST_*` as deprecated alias.
- feat: launcher auto-bootstrap from the official GitHub Release
  (HTTPS-only download, SHA256 verified, `.iptracex/bin` cache, `--update`
  refresh, x86_64 gate, Windows platform guard); launcher behavior tests
  in `tests/launcher/`; release ships `SHA256SUMS`.

## 1.0.0 — 2026-10-07

- Complete migration from Python to C# / .NET 8, renamed IPGHOST → IPTraceX.
  The Python lineage (IPGHOST 0.1.x) remains visible in git history.
- Same CLI contract: interactive / direct / `--json` / `--file` /
  `--stdin` / `--map` / `--self` / `--help` / `--version` / `--no-color` /
  `--debug` / `--timeout`, same exit codes, same JSON keys (plus additive
  `geoip_quality` + `providers`).
- Multi-provider consensus engine (ipwho.is + ipapi.co + ipinfo.io),
  confidence scoring, disagreement display, provider-aware cache.
- xUnit suite (unit, mocked; integration opt-in), GitHub Actions CI,
  self-contained linux-x64 single-file publish via `./iptracex.sh`.
