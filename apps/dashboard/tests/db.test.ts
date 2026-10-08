import { describe, it, expect, beforeAll } from "vitest";
import { readFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";
import { newDb } from "pg-mem";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const schema = readFileSync(join(root, "schema.sql"), "utf8");
// pg-mem does not parse IF NOT EXISTS on CREATE TABLE: strip it for the
// emulator only. Production Postgres runs schema.sql verbatim (idempotency
// there comes from the IF NOT EXISTS clauses, asserted statically below).
const emulatorSchema = schema.replaceAll("IF NOT EXISTS ", "");

describe("postgres migration (pg-mem)", () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let db: any;
  beforeAll(() => {
    db = newDb();
    db.public.none(emulatorSchema);
  });

  it("is written idempotently (IF NOT EXISTS on every CREATE)", () => {
    const creates = schema.match(/^CREATE (TABLE|INDEX).*/gim) ?? [];
    expect(creates.length).toBeGreaterThan(0);
    for (const stmt of creates) {
      expect(stmt).toMatch(/IF NOT EXISTS/i);
    }
  });

  it("creates all tables and indexes", () => {
    const tables = db.public
      .many(
        `SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'`
      )
      .map((r: { table_name: string }) => r.table_name);
    for (const t of [
      "audit_events",
      "security_alerts",
      "sessions",
      "investigations",
      "provider_health",
      "dashboard_users",
      "dashboard_audit",
      "dashboard_sessions",
    ]) {
      expect(tables).toContain(t);
    }
    // pg-mem has no pg_catalog views: index definitions are asserted
    // statically from schema.sql (production Postgres builds them).
    for (const i of [
      "idx_events_timestamp",
      "idx_events_type",
      "idx_events_severity",
      "idx_events_provider",
      "idx_events_investigation",
      "idx_events_correlation",
      "idx_events_session",
      "idx_events_status",
      "idx_alerts_status",
      "idx_alerts_timestamp",
    ]) {
      expect(schema).toContain(`CREATE INDEX IF NOT EXISTS ${i}`);
    }
  });

  it("stores and retrieves an audit event", () => {
    db.public.none(
      `INSERT INTO audit_events (event_id, timestamp, severity, event_type, category,
        operation, component, status, duration_ms, session_id, correlation_id,
        investigation_id, target_type, target_reference, provider, message, metadata)
       VALUES ('EVT-TEST-1', NOW(), 'ALERT', 'EMAIL_SECURITY_ALERT', 'email',
        'security-alert', 'cli', 'HIGH', 218, 'SES-1', 'COR-1',
        'EMX-20261008-ABCDEF', 'email', 'user@example.com', 'hibp',
        'password data reported', '{"breaches": "1"}')
       ON CONFLICT (event_id) DO NOTHING`
    );
    const row = db.public.one(
      `SELECT event_id, severity, investigation_id FROM audit_events WHERE event_id = 'EVT-TEST-1'`
    );
    expect(row.severity).toBe("ALERT");
    expect(row.investigation_id).toBe("EMX-20261008-ABCDEF");
  });

  it("supports investigation / provider / severity / correlation lookup", () => {
    const byInv = db.public.many(
      `SELECT event_id FROM audit_events WHERE investigation_id = 'EMX-20261008-ABCDEF'`
    );
    expect(byInv.length).toBeGreaterThan(0);
    const byProv = db.public.many(
      `SELECT event_id FROM audit_events WHERE provider = 'hibp'`
    );
    expect(byProv.length).toBeGreaterThan(0);
    const byCorr = db.public.many(
      `SELECT event_id FROM audit_events WHERE correlation_id = 'COR-1'`
    );
    expect(byCorr.length).toBeGreaterThan(0);
    const bySev = db.public.many(
      `SELECT event_id FROM audit_events WHERE severity = 'ALERT' ORDER BY timestamp DESC LIMIT 100`
    );
    expect(bySev.length).toBeGreaterThan(0);
  });

  it("manages security alert lifecycle", () => {
    db.public.none(
      `INSERT INTO security_alerts (alert_id, timestamp, rule, severity, reason, count, status)
       VALUES ('ALT-1', NOW(), 'AUTH_FAILURES_10_IN_60S', 'ALERT', 'burst', 12, 'NEW')`
    );
    db.public.none(
      `UPDATE security_alerts SET status = 'ACKNOWLEDGED', changed_by = 'tester',
        changed_at = NOW() WHERE alert_id = 'ALT-1'`
    );
    const row = db.public.one(
      `SELECT status, changed_by FROM security_alerts WHERE alert_id = 'ALT-1'`
    );
    expect(row.status).toBe("ACKNOWLEDGED");
    expect(row.changed_by).toBe("tester");
  });

  it("dedupes repeat event delivery", () => {
    db.public.none(
      `INSERT INTO audit_events (event_id, timestamp, severity, event_type, category)
       VALUES ('EVT-TEST-1', NOW(), 'ALERT', 'EMAIL_SECURITY_ALERT', 'email')
       ON CONFLICT (event_id) DO NOTHING`
    );
    const rows = db.public.many(
      `SELECT event_id FROM audit_events WHERE event_id = 'EVT-TEST-1'`
    );
    expect(rows).toHaveLength(1);
  });
});
