import { NextRequest, NextResponse } from "next/server";
import { queryEvents, getEvent, metrics, EventFilter } from "../../../../lib/store";
import { getSession } from "../../../../lib/auth";
import { cookies } from "next/headers";

function session() {
  return getSession(cookies().get("iptracex_session")?.value);
}

function deny() {
  return NextResponse.json({ ok: false, error: "unauthorized" }, { status: 401 });
}
function filterFrom(req: NextRequest): EventFilter {
  const q = req.nextUrl.searchParams;
  const pick = (k: string) => q.get(k) ?? undefined;
  const limit = q.get("limit") ? Number(q.get("limit")) : undefined;
  return {
    severity: pick("severity"),
    event_type: pick("event_type"),
    component: pick("component"),
    provider: pick("provider"),
    target_type: pick("target_type"),
    status: pick("status"),
    investigation_id: pick("investigation_id"),
    correlation_id: pick("correlation_id"),
    session_id: pick("session_id"),
    search: pick("search"),
    since: pick("since"),
    until: pick("until"),
    cursor: pick("cursor"),
    limit: limit && Number.isFinite(limit) ? limit : undefined,
  };
}

export async function GET(req: NextRequest) {
  if (!(await session())) return deny();
  const { pathname } = req.nextUrl;
  try {
    if (pathname.endsWith("/metrics")) {
      return NextResponse.json({ ok: true, metrics: await metrics() });
    }
    const idMatch = pathname.match(/\/api\/events\/([^/]+)$/);
    if (idMatch) {
      const event = await getEvent(decodeURIComponent(idMatch[1]));
      if (!event) {
        return NextResponse.json({ ok: false, error: "not found" }, { status: 404 });
      }
      const related = event.correlation_id
        ? await queryEvents({ correlation_id: event.correlation_id, limit: 50 })
        : [];
      return NextResponse.json({ ok: true, event, related });
    }
    return NextResponse.json({ ok: true, events: await queryEvents(filterFrom(req)) });
  } catch {
    return NextResponse.json({ ok: false, error: "storage error" }, { status: 500 });
  }
}
