import { NextRequest, NextResponse } from "next/server";
import { validateIngestBody, MAX_BODY_BYTES } from "@/lib/events";
import { storeEvents, StoredEvent } from "@/lib/store";
import { checkIngestAuth, rateLimited } from "@/lib/auth";

export async function POST(req: NextRequest) {
  const ip =
    req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() || "unknown";
  if (rateLimited(`ingest:${ip}`, 120, 60_000)) {
    return NextResponse.json(
      { ok: false, error: "rate limited" },
      { status: 429 }
    );
  }
  if (!checkIngestAuth(req.headers.get("authorization"))) {
    return NextResponse.json(
      { ok: false, error: "unauthorized" },
      { status: 401 }
    );
  }
  let body: unknown;
  try {
    const text = await req.text();
    if (text.length > MAX_BODY_BYTES) {
      return NextResponse.json(
        { ok: false, error: "payload too large" },
        { status: 413 }
      );
    }
    body = JSON.parse(text || "{}");
  } catch {
    return NextResponse.json(
      { ok: false, error: "malformed JSON" },
      { status: 400 }
    );
  }
  const { events, errors } = validateIngestBody(body);
  if (errors.length > 0) {
    return NextResponse.json(
      { ok: false, error: "invalid payload", details: errors.slice(0, 10) },
      { status: 400 }
    );
  }
  const stored: StoredEvent[] = events.map((e) => ({
    event_id: String(e.event_id ?? `EVT-${Date.now()}-${Math.random().toString(36).slice(2)}`),
    timestamp: e.timestamp ? new Date(String(e.timestamp)).toISOString() : new Date().toISOString(),
    severity: String(e.severity),
    event_type: String(e.event_type),
    category: String(e.category ?? "other"),
    operation: (e.operation as string) ?? null,
    component: (e.component as string) ?? null,
    status: (e.status as string) ?? null,
    duration_ms: (e.duration_ms as number) ?? null,
    session_id: (e.session_id as string) ?? null,
    correlation_id: (e.correlation_id as string) ?? null,
    investigation_id: (e.investigation_id as string) ?? null,
    target_type: (e.target_type as string) ?? null,
    target_reference: (e.target_reference as string) ?? null,
    provider: (e.provider as string) ?? null,
    http_status: (e.http_status as number) ?? null,
    retry_count: (e.retry_count as number) ?? null,
    error_code: (e.error_code as string) ?? null,
    error_type: (e.error_type as string) ?? null,
    message: (e.message as string) ?? null,
    metadata: (e.metadata as Record<string, string>) ?? null,
  }));
  try {
    await storeEvents(stored);
  } catch {
    return NextResponse.json(
      { ok: false, error: "storage error" },
      { status: 500 }
    );
  }
  return NextResponse.json({ ok: true, received: stored.length });
}
