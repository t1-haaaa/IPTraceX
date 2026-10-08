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
    const mod = await import("pg");
    const Pool = (mod as { Pool: new (cfg: unknown) => typeof pgPool }).Pool;
    pgPool = new Pool({ connectionString: process.env.DATABASE_URL, max: 5 });
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
    }
    return;
  }
  await fs.mkdir(path.dirname(dataFile()), { recursive: true });
  const lines = events.map((e) => JSON.stringify(e)).join("\n") + "\n";
  await fs.appendFile(dataFile(), lines, "utf8");
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
    where.push(`(message ILIKE $${params.length} OR event_type ILIKE $${params.length})`);
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
