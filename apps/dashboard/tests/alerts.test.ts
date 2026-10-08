import { describe, it, expect } from "vitest";
import { saveAlert, listAlerts, setAlertStatus } from "../src/lib/store";

describe("alert lifecycle", () => {
  it("creates, acknowledges, and resolves with audit", async () => {
    process.env.AUDIT_DATA_DIR = `audit-test-${Date.now()}`;
    await saveAlert({
      alert_id: "ALT-TEST-1",
      timestamp: new Date().toISOString(),
      rule: "AUTH_FAILURES_10_IN_60S",
      severity: "ALERT",
      reason: "burst",
      count: 12,
      status: "NEW",
    });
    const open = await listAlerts("NEW");
    expect(open.some((a) => a.alert_id === "ALT-TEST-1")).toBe(true);
    const ack = await setAlertStatus("ALT-TEST-1", "ACKNOWLEDGED", "tester");
    expect(ack?.status).toBe("ACKNOWLEDGED");
    expect(ack?.changed_by).toBe("tester");
    const done = await setAlertStatus("ALT-TEST-1", "RESOLVED", "tester");
    expect(done?.status).toBe("RESOLVED");
    expect(await setAlertStatus("ALT-NOPE", "RESOLVED", "tester")).toBe(null);
    delete process.env.AUDIT_DATA_DIR;
  });
});
