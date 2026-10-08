import { promises as fs } from "fs";
import path from "path";

// Storage abstraction: Postgres when DATABASE_URL is set, otherwise a
// local JSONL file (dev/preview). Same query shapes; bounded results.

export interface StoredEvent {
  event_id: string;
  timestamp: string;
  severity: string;
  event_type: string;
  category: string;
  operation?: string | null;
  component?: string | null;
  status?: string | null;
  duration_ms?: number | null;
  session_id?: string | null;
  correlation_id?: string | null;
  investigation_id?: string | null;
  target_type?: string | null;
  target_reference?: string | null;
  provider?: string | null;
  http_status?: number | null;
  retry_count?: number | null;
  error_code?: string | null;
  error_type?: string | null;
  message?: string | null;
  metadata?: Record<string, string> | null;
}

export interface EventFilter {
  severity?: string;
  event_type?: string;
  component?: string;
  provider?: string;
  target_type?: string;
  status?: string;
  investigation_id?: string;
  correlation_id?: string;
  session_id?: string;
  search?: string;
  since?: string;
  until?: string;
  limit?: number;
  cursor?: string; // event_id offset (exclusive, newest-first)
}

function dataFile(): string {
  const dir = process.env.AUDIT_DATA_DIR || path.join(process.cwd(), ".audit-data");
  return path.join(dir, "events.jsonl");
}

async function readFileEvents(): Promise<StoredEvent[]> {
  try {
    const raw = await fs.readFile(dataFile(), "utf8");
    const out: StoredEvent[] = [];
    for (const line of raw.split("\n")) {
      if (!line.trim()) continue;
      try {
        out.push(JSON.parse(line));
      } catch {
        // skip corrupt lines
      }
    }
    return out;
  } catch {
    return [];
  }
}

function applyFilter(events: StoredEvent[], f: EventFilter): StoredEvent[] {
  let list = [...events].sort((a, b) => (a.timestamp < b.timestamp ? 1 : -1));
  if (f.cursor) {
    const idx = list.findIndex((e) => e.event_id === f.cursor);
    if (idx >= 0) list = list.slice(idx + 1);
  }
  const eq = (v: string | null | undefined, want?: string) =>
    !want || (v ?? "").toLowerCase() === want.toLowerCase();
  list = list.filter(
    (e) =>
      eq(e.severity, f.severity) &&
      eq(e.event_type, f.event_type) &&
      eq(e.component, f.component) &&
      eq(e.provider, f.provider) &&
      eq(e.target_type, f.target_type) &&
      eq(e.status, f.status) &&
      eq(e.investigation_id, f.investigation_id) &&
      eq(e.correlation_id, f.correlation_id) &&
      eq(e.session_id, f.session_id) &&
      (!f.since || e.timestamp >= f.since) &&
      (!f.until || e.timestamp <= f.until) &&
      (!f.search ||
        JSON.stringify(e).toLowerCase().includes(f.search.toLowerCase()))
  );
  return list.slice(0, Math.min(Math.max(f.limit ?? 100, 1), 500));
}

let pgPool: { query: (text: string, params?: unknown[]) => Promise<{ rows: StoredEvent[] }> } | null = null;
let pgTried = false;

async function pg(): Promise<typeof pgPool> {
  if (pgTried) return pgPool;
  pgTried = true;
  if (!process.env.DATABASE_URL) return null;
  try {
    const { default: pgMod } = await import("pg") as unknown as {
      default: { Pool: new (cfg: unknown) => NonNullable<typeof pgPool> };
    };
    pgPool = new pgMod.Pool({ connectionString: process.env.DATABASE_URL, max: 5 });
    return pgPool;
  } catch {
    return null;
  }
}

export async function storeEvents(events: StoredEvent[]): Promise<void> {
  const pool = await pg();
  if (pool) {
    for (const e of events) {
      await pool.query(
        `INSERT INTO audit_events (event_id, timestamp, severity, event_type, category,
          operation, component, status, duration_ms, session_id, correlation_id,
          investigation_id, target_type, target_reference, provider, http_status,
          retry_count, error_code, error_type, message, metadata)
         VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21)
         ON CONFLICT (event_id) DO NOTHING`,
        [
          e.event_id, e.timestamp, e.severity, e.event_type, e.category,
          e.operation ?? null, e.component ?? null, e.status ?? null,
          e.duration_ms ?? null, e.session_id ?? null, e.correlation_id ?? null,
          e.investigation_id ?? null, e.target_type ?? null, e.target_reference ?? null,
          e.provider ?? null, e.http_status ?? null, e.retry_count ?? null,
          e.error_code ?? null, e.error_type ?? null, e.message ?? null,
          e.metadata ? JSON.stringify(e.metadata) : null,
        ]
      );
      if (e.severity === "ALERT" || e.severity === "CRITICAL") {
        await pool.query(
          `INSERT INTO security_alerts (alert_id, timestamp, rule, severity, reason, count, source, correlation_id, status)
           VALUES ($1,$2,$3,$4,$5,$6,$7,$8,'NEW') ON CONFLICT (alert_id) DO NOTHING`,
          [`ALT-${e.event_id}`, e.timestamp, e.event_type, e.severity,
            e.message ?? e.event_type, 1, e.component ?? null, e.correlation_id ?? null]
        );
      }
    }
    return;
  }
  await fs.mkdir(path.dirname(dataFile()), { recursive: true });
  const lines = events.map((e) => JSON.stringify(e)).join("\n") + "\n";
  await fs.appendFile(dataFile(), lines, "utf8");
  for (const e of events) {
    if (e.severity === "ALERT" || e.severity === "CRITICAL") {
      await saveAlert({
        alert_id: `ALT-${e.event_id}`,
        timestamp: e.timestamp,
        rule: e.event_type,
        severity: e.severity,
        reason: e.message ?? e.event_type,
        count: 1,
        source: e.component ?? null,
        correlation_id: e.correlation_id ?? null,
        status: "NEW",
      });
    }
  }
}

export interface Alert {
  alert_id: string;
  timestamp: string;
  rule: string;
  severity: string;
  reason: string;
  count: number;
  source?: string | null;
  correlation_id?: string | null;
  status: string;
  changed_by?: string | null;
  changed_at?: string | null;
}

function alertsFile(): string {
  const dir = process.env.AUDIT_DATA_DIR || path.join(process.cwd(), ".audit-data");
  return path.join(dir, "alerts.json");
}

async function readAlertsFile(): Promise<Alert[]> {
  try {
    const raw = await fs.readFile(alertsFile(), "utf8");
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as Alert[]) : [];
  } catch {
    return [];
  }
}

async function auditLog(username: string | null, action: string, details: string): Promise<void> {
  const pool = await pg();
  if (pool) {
    await pool.query(
      `INSERT INTO dashboard_audit (username, action, details) VALUES ($1,$2,$3)`,
      [username, action, details]
    );
    return;
  }
  await fs.mkdir(path.dirname(dataFile()), { recursive: true });
  await fs.appendFile(
    dataFile(),
    JSON.stringify({
      event_id: `EVT-${Date.now()}-${Math.random().toString(36).slice(2)}`,
      timestamp: new Date().toISOString(),
      severity: "INFO",
      event_type: action,
      category: "dashboard",
      message: `${username ?? "anonymous"}: ${details}`,
    }) + "\n",
    "utf8"
  );
}

export async function saveAlert(a: Alert): Promise<void> {
  const pool = await pg();
  if (pool) {
    await pool.query(
      `INSERT INTO security_alerts (alert_id, timestamp, rule, severity, reason, count, source, correlation_id, status)
       VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9) ON CONFLICT (alert_id) DO NOTHING`,
      [a.alert_id, a.timestamp, a.rule, a.severity, a.reason, a.count, a.source ?? null, a.correlation_id ?? null, a.status]
    );
    return;
  }
  const all = await readAlertsFile();
  if (!all.some((x) => x.alert_id === a.alert_id)) {
    all.push(a);
    await fs.mkdir(path.dirname(alertsFile()), { recursive: true });
    await fs.writeFile(alertsFile(), JSON.stringify(all), "utf8");
  }
}

export async function listAlerts(status?: string, limit = 100): Promise<Alert[]> {
  const pool = await pg();
  if (pool) {
    const res = await pool.query(
      `SELECT * FROM security_alerts` +
        (status ? ` WHERE status = $1` : ``) +
        ` ORDER BY timestamp DESC LIMIT ${Math.min(Math.max(limit, 1), 200)}`,
      status ? [status] : []
    );
    return res.rows as unknown as Alert[];
  }
  const all = await readAlertsFile();
  const filtered = status ? all.filter((a) => a.status === status) : all;
  return filtered
    .sort((a, b) => (a.timestamp < b.timestamp ? 1 : -1))
    .slice(0, Math.min(Math.max(limit, 1), 200));
}

export async function setAlertStatus(
  id: string,
  status: "ACKNOWLEDGED" | "RESOLVED",
  username: string
): Promise<Alert | null> {
  const pool = await pg();
  if (pool) {
    const res = await pool.query(
      `UPDATE security_alerts SET status = $1, changed_by = $2, changed_at = NOW()
       WHERE alert_id = $3 RETURNING *`,
      [status, username, id]
    );
    const row = (res.rows as unknown as Alert[])[0] ?? null;
    if (row) await auditLog(username, status === "ACKNOWLEDGED" ? "ALERT_ACKNOWLEDGED" : "ALERT_RESOLVED", id);
    return row;
  }
  const all = await readAlertsFile();
  const found = all.find((a) => a.alert_id === id) ?? null;
  if (found) {
    found.status = status;
    found.changed_by = username;
    found.changed_at = new Date().toISOString();
    await fs.writeFile(alertsFile(), JSON.stringify(all), "utf8");
    await auditLog(username, status === "ACKNOWLEDGED" ? "ALERT_ACKNOWLEDGED" : "ALERT_RESOLVED", id);
  }
  return found;
}

export async function queryEvents(f: EventFilter): Promise<StoredEvent[]> {
  const pool = await pg();
  if (!pool) return applyFilter(await readFileEvents(), f);
  const where: string[] = [];
  const params: unknown[] = [];
  const add = (col: string, v?: string) => {
    if (v) {
      params.push(v);
      where.push(`${col} = $${params.length}`);
    }
  };
  add("severity", f.severity);
  add("event_type", f.event_type);
  add("component", f.component);
  add("provider", f.provider);
  add("target_type", f.target_type);
  add("status", f.status);
  add("investigation_id", f.investigation_id);
  add("correlation_id", f.correlation_id);
  add("session_id", f.session_id);
  if (f.since) {
    params.push(f.since);
    where.push(`timestamp >= $${params.length}`);
  }
  if (f.until) {
    params.push(f.until);
    where.push(`timestamp <= $${params.length}`);
  }
  if (f.search) {
    params.push(`%${f.search}%`);
    const p = `$${params.length}`;
    where.push(`(message ILIKE ${p} OR event_type ILIKE ${p} OR target_reference ILIKE ${p} OR investigation_id ILIKE ${p} OR provider ILIKE ${p} OR correlation_id ILIKE ${p} OR session_id ILIKE ${p} OR status ILIKE ${p})`);
  }
  const limit = Math.min(Math.max(f.limit ?? 100, 1), 500);
  params.push(limit);
  const sql =
    `SELECT * FROM audit_events` +
    (where.length ? ` WHERE ${where.join(" AND ")}` : "") +
    ` ORDER BY timestamp DESC LIMIT $${params.length}`;
  const res = await pool.query(sql, params);
  return res.rows;
}

export async function getEvent(id: string): Promise<StoredEvent | null> {
  const pool = await pg();
  if (!pool) {
    const all = await readFileEvents();
    return all.find((e) => e.event_id === id) ?? null;
  }
  const res = await pool.query(`SELECT * FROM audit_events WHERE event_id = $1`, [id]);
  return res.rows[0] ?? null;
}

export interface PersistedSession {
  username: string;
  role: string;
  createdAt: number;
}

function sessionsFile(): string {
  const dir = process.env.AUDIT_DATA_DIR || path.join(process.cwd(), ".audit-data");
  return path.join(dir, "sessions.json");
}

async function readSessionsFile(): Promise<Record<string, PersistedSession>> {
  try {
    return JSON.parse(await fs.readFile(sessionsFile(), "utf8"));
  } catch {
    return {};
  }
}

export async function saveSession(token: string, s: PersistedSession): Promise<void> {
  const pool = await pg();
  if (pool) {
    await pool.query(
      `INSERT INTO dashboard_sessions (token, username, role, created_at)
       VALUES ($1,$2,$3,$4) ON CONFLICT (token) DO NOTHING`,
      [token, s.username, s.role, new Date(s.createdAt).toISOString()]
    );
    return;
  }
  await fs.mkdir(path.dirname(sessionsFile()), { recursive: true });
  const all = await readSessionsFile();
  all[token] = s;
  await fs.writeFile(sessionsFile(), JSON.stringify(all), "utf8");
}

export async function readSession(token: string): Promise<PersistedSession | null> {
  const pool = await pg();
  if (pool) {
    const res = await pool.query(`SELECT * FROM dashboard_sessions WHERE token = $1`, [token]);
    const row = res.rows[0] as unknown as
      | { username: string; role: string; created_at: string }
      | undefined;
    if (!row) return null;
    return { username: row.username, role: row.role, createdAt: Date.parse(row.created_at) };
  }
  return (await readSessionsFile())[token] ?? null;
}

export async function removeSession(token: string): Promise<void> {
  const pool = await pg();
  if (pool) {
    await pool.query(`DELETE FROM dashboard_sessions WHERE token = $1`, [token]);
    return;
  }
  const all = await readSessionsFile();
  delete all[token];
  await fs.writeFile(sessionsFile(), JSON.stringify(all), "utf8");
}

export async function metrics(): Promise<Record<string, number>> {
  const pool = await pg();
  const all = pool
    ? (await pool.query(`SELECT severity, event_type FROM audit_events ORDER BY timestamp DESC LIMIT 5000`)).rows.map(
        (r) => ({ severity: r.severity as string, event_type: r.event_type as string })
      )
    : (await readFileEvents()).slice(-5000);
  const count = (fn: (e: { severity: string; event_type: string }) => boolean) =>
    all.filter(fn).length;
  return {
    events: all.length,
    alerts: count((e) => e.severity === "ALERT" || e.severity === "CRITICAL"),
    authFailures: count((e) => e.event_type === "AUTH_FAILURE" || e.event_type === "DASHBOARD_LOGIN_FAILURE"),
    rateLimits: count((e) => e.event_type === "RATE_LIMIT_TRIGGERED"),
    suspicious: count((e) => e.event_type === "SUSPICIOUS_INPUT"),
    providerErrors: count(
      (e) => e.event_type === "PROVIDER_QUERY_ERROR" || e.event_type === "PROVIDER_TIMEOUT"
    ),
  };
}
