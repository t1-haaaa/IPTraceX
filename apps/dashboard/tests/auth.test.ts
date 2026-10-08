import { describe, it, expect } from "vitest";
import { checkIngestAuth, rateLimited, canAcknowledge, canAdmin, login } from "../src/lib/auth";

describe("dashboard auth", () => {
  it("rejects missing or wrong ingest secret", () => {
    delete process.env.IPTRACEX_AUDIT_INGEST_SECRET;
    expect(checkIngestAuth("Bearer x")).toBe(false);
    process.env.IPTRACEX_AUDIT_INGEST_SECRET = "s3cret";
    expect(checkIngestAuth(null)).toBe(false);
    expect(checkIngestAuth("Bearer wrong")).toBe(false);
    expect(checkIngestAuth("Bearer s3cret")).toBe(true);
  });
  it("accepts hyphenated usernames via underscore env vars", () => {
    process.env.IPTRACEX_DASHBOARD_USERS = "e2e-admin:ADMIN";
    process.env.IPTRACEX_DASHBOARD_PASSWORD_E2E_ADMIN = "pw12345";
    expect(login("e2e-admin", "pw12345")?.role).toBe("ADMIN");
    expect(login("e2e-admin", "wrong")).toBe(null);
    delete process.env.IPTRACEX_DASHBOARD_USERS;
    delete process.env.IPTRACEX_DASHBOARD_PASSWORD_E2E_ADMIN;
  });
  it("rate limits bursts", () => {
    const key = `t-${Date.now()}`;
    expect(rateLimited(key, 2, 60_000)).toBe(false);
    expect(rateLimited(key, 2, 60_000)).toBe(false);
    expect(rateLimited(key, 2, 60_000)).toBe(true);
  });
  it("enforces server-side roles", () => {
    expect(canAcknowledge(null)).toBe(false);
    expect(canAcknowledge({ username: "v", role: "VIEWER", createdAt: 0 })).toBe(false);
    expect(canAcknowledge({ username: "a", role: "ANALYST", createdAt: 0 })).toBe(true);
    expect(canAdmin({ username: "a", role: "ANALYST", createdAt: 0 })).toBe(false);
    expect(canAdmin({ username: "r", role: "ADMIN", createdAt: 0 })).toBe(true);
  });
});
