import { createHash, randomBytes, timingSafeEqual } from "crypto";

// Dashboard auth: cookie sessions, server-side roles only.
// Secrets live in env (IPTRACEX_DASHBOARD_*); the ingest secret is
// never sent to the browser.

export type Role = "ADMIN" | "ANALYST" | "VIEWER";

export interface Session {
  username: string;
  role: Role;
  createdAt: number;
}

// NOTE: sessions persist in the dashboard store (store.ts: file or
// Postgres), never in a process-local map, so auth works across
// route handlers and instances.

function hashPassword(password: string, salt: string): string {
  return createHash("sha256").update(`${salt}:${password}`).digest("hex");
}

// Users configured via env: IPTRACTEX_DASHBOARD_USERS="alice:ADMIN,bob:VIEWER"
// with passwords IPTRACTEX_DASHBOARD_PASSWORD_ALICE="...". Missing env in
// dev falls back to a single admin (admin/iptracex-dev-only, dev only).
function users(): Map<string, { hash: string; salt: string; role: Role }> {
  const map = new Map<string, { hash: string; salt: string; role: Role }>();
  const list = (process.env.IPTRACEX_DASHBOARD_USERS || "").split(",").filter(Boolean);
  for (const entry of list) {
    const [name, role] = entry.split(":");
    const password = process.env[`IPTRACEX_DASHBOARD_PASSWORD_${name.trim().toUpperCase()}`];
    if (!name || !password) continue;
    const salt = process.env.IPTRACEX_DASHBOARD_SESSION_SECRET || "dev-salt";
    map.set(name.trim(), {
      hash: hashPassword(password, salt),
      salt,
      role: (role?.trim().toUpperCase() as Role) || "VIEWER",
    });
  }
  if (map.size === 0 && process.env.NODE_ENV !== "production") {
    map.set("admin", { hash: hashPassword("iptracex-dev-only", "dev-salt"), salt: "dev-salt", role: "ADMIN" });
  }
  return map;
}

export function login(username: string, password: string): Session | null {
  const u = users().get(username);
  if (!u) return null;
  const attempt = hashPassword(password, u.salt);
  const a = Buffer.from(attempt);
  const b = Buffer.from(u.hash);
  if (a.length !== b.length || !timingSafeEqual(a, b)) return null;
  return { username, role: u.role, createdAt: Date.now() };
}

export function canRead(s: Session | null): boolean {
  return s !== null;
}

export function canAcknowledge(s: Session | null): boolean {
  return s?.role === "ADMIN" || s?.role === "ANALYST";
}

export function canAdmin(s: Session | null): boolean {
  return s?.role === "ADMIN";
}

export async function createSessionToken(s: Session): Promise<string> {
  const { saveSession } = await import("./store");
  const token = randomBytes(32).toString("hex");
  await saveSession(token, s);
  return token;
}

export async function getSession(token: string | undefined): Promise<Session | null> {
  if (!token) return null;
  const { readSession } = await import("./store");
  const s = await readSession(token);
  if (!s) return null;
  const role = s.role === "ADMIN" || s.role === "ANALYST" ? s.role : "VIEWER";
  return { username: s.username, role, createdAt: s.createdAt };
}

export async function destroySession(token: string | undefined): Promise<void> {
  if (!token) return;
  const { removeSession } = await import("./store");
  await removeSession(token);
}

export function checkIngestAuth(header: string | null): boolean {
  const secret = process.env.IPTRACEX_AUDIT_INGEST_SECRET;
  if (!secret) return false;
  if (!header || !header.startsWith("Bearer ")) return false;
  const token = header.slice("Bearer ".length);
  const a = Buffer.from(token);
  const b = Buffer.from(secret);
  return a.length === b.length && timingSafeEqual(a, b);
}

// Simple in-memory rate limiter (per key, fixed window).
const buckets = new Map<string, { count: number; reset: number }>();
export function rateLimited(key: string, max: number, windowMs: number): boolean {
  const now = Date.now();
  const cur = buckets.get(key);
  if (!cur || now > cur.reset) {
    buckets.set(key, { count: 1, reset: now + windowMs });
    return false;
  }
  cur.count += 1;
  return cur.count > max;
}
