-- IPTraceX audit database (Postgres). Run with: npm run migrate
-- (requires DATABASE_URL) or psql -f schema.sql.

CREATE TABLE IF NOT EXISTS audit_events (
  event_id        TEXT PRIMARY KEY,
  timestamp       TIMESTAMPTZ NOT NULL,
  severity        TEXT NOT NULL,
  event_type      TEXT NOT NULL,
  category        TEXT NOT NULL,
  operation       TEXT,
  component       TEXT,
  status          TEXT,
  duration_ms     BIGINT,
  session_id      TEXT,
  correlation_id  TEXT,
  investigation_id TEXT,
  target_type     TEXT,
  target_reference TEXT,
  provider        TEXT,
  http_status     INT,
  retry_count     INT,
  error_code      TEXT,
  error_type      TEXT,
  message         TEXT,
  metadata        JSONB
);

CREATE INDEX IF NOT EXISTS idx_events_timestamp ON audit_events (timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_events_type ON audit_events (event_type);
CREATE INDEX IF NOT EXISTS idx_events_severity ON audit_events (severity);
CREATE INDEX IF NOT EXISTS idx_events_provider ON audit_events (provider);
CREATE INDEX IF NOT EXISTS idx_events_investigation ON audit_events (investigation_id);
CREATE INDEX IF NOT EXISTS idx_events_correlation ON audit_events (correlation_id);
CREATE INDEX IF NOT EXISTS idx_events_session ON audit_events (session_id);
CREATE INDEX IF NOT EXISTS idx_events_status ON audit_events (status);

CREATE TABLE IF NOT EXISTS sessions (
  session_id  TEXT PRIMARY KEY,
  first_seen  TIMESTAMPTZ NOT NULL,
  last_seen   TIMESTAMPTZ NOT NULL,
  event_count INT NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS investigations (
  investigation_id TEXT PRIMARY KEY,
  target_type      TEXT,
  target_reference TEXT,
  first_seen       TIMESTAMPTZ NOT NULL,
  last_seen        TIMESTAMPTZ NOT NULL,
  event_count      INT NOT NULL DEFAULT 0,
  max_severity     TEXT
);

CREATE TABLE IF NOT EXISTS provider_health (
  provider      TEXT PRIMARY KEY,
  category      TEXT,
  success       INT NOT NULL DEFAULT 0,
  failure       INT NOT NULL DEFAULT 0,
  timeouts      INT NOT NULL DEFAULT 0,
  total_latency BIGINT NOT NULL DEFAULT 0,
  last_success  TIMESTAMPTZ,
  last_error    TIMESTAMPTZ,
  last_status   TEXT
);

CREATE TABLE IF NOT EXISTS security_alerts (
  alert_id       TEXT PRIMARY KEY,
  timestamp      TIMESTAMPTZ NOT NULL,
  rule           TEXT NOT NULL,
  severity       TEXT NOT NULL,
  reason         TEXT NOT NULL,
  count          INT NOT NULL DEFAULT 1,
  source         TEXT,
  correlation_id TEXT,
  status         TEXT NOT NULL DEFAULT 'NEW',
  changed_by     TEXT,
  changed_at     TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_alerts_status ON security_alerts (status);
CREATE INDEX IF NOT EXISTS idx_alerts_timestamp ON security_alerts (timestamp DESC);

CREATE TABLE IF NOT EXISTS dashboard_sessions (
  token       TEXT PRIMARY KEY,
  username    TEXT NOT NULL,
  role        TEXT NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS dashboard_users (
  username        TEXT PRIMARY KEY,
  password_hash   TEXT NOT NULL,
  role            TEXT NOT NULL DEFAULT 'VIEWER',
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS dashboard_audit (
  id            BIGSERIAL PRIMARY KEY,
  timestamp     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  username      TEXT,
  action        TEXT NOT NULL,
  details       TEXT
);
