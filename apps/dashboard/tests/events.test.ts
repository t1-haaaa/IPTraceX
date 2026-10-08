import { describe, it, expect } from "vitest";
import { validateIngestBody } from "../src/lib/events";

const good = {
  event_id: "EVT-1",
  timestamp: new Date().toISOString(),
  severity: "INFO",
  event_type: "CLI_COMMAND",
  category: "cli",
};

describe("ingest validation", () => {
  it("accepts single event and batch", () => {
    expect(validateIngestBody(good).errors).toEqual([]);
    const batch = validateIngestBody({ events: [good, { ...good, event_id: "EVT-2" }] });
    expect(batch.errors).toEqual([]);
    expect(batch.events).toHaveLength(2);
  });
  it("rejects unknown types, bad severity, bad http status", () => {
    expect(validateIngestBody({ ...good, event_type: "HACK" }).errors.length).toBeGreaterThan(0);
    expect(validateIngestBody({ ...good, severity: "BOGUS" }).errors.length).toBeGreaterThan(0);
    expect(validateIngestBody({ ...good, http_status: 99 }).errors.length).toBeGreaterThan(0);
    expect(validateIngestBody({}).errors.length).toBeGreaterThan(0);
  });
  it("rejects oversized batches and metadata", () => {
    const many = Array.from({ length: 201 }, (_, i) => ({ ...good, event_id: `EVT-${i}` }));
    expect(validateIngestBody({ events: many }).errors.length).toBeGreaterThan(0);
    const big: Record<string, string> = {};
    for (let i = 0; i < 33; i++) big[`k${i}`] = "v";
    expect(
      validateIngestBody({ ...good, metadata: big }).errors.length
    ).toBeGreaterThan(0);
    expect(
      validateIngestBody({ ...good, metadata: { k: "x".repeat(513) } }).errors.length
    ).toBeGreaterThan(0);
  });
  it("rejects malformed timestamps", () => {
    expect(
      validateIngestBody({ ...good, timestamp: "not-a-date" }).errors.length
    ).toBeGreaterThan(0);
  });
});
