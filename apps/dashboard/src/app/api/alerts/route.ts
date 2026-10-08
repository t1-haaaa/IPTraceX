import { NextRequest, NextResponse } from "next/server";
import { listAlerts, setAlertStatus } from "../../../lib/store";
import { getSession, canAcknowledge } from "../../../lib/auth";
import { cookies } from "next/headers";

async function session() {
  return getSession(cookies().get("iptracex_session")?.value);
}

export async function GET(req: NextRequest) {
  const s = await session();
  if (!s) {
    return NextResponse.json({ ok: false, error: "unauthorized" }, { status: 401 });
  }
  const status = req.nextUrl.searchParams.get("status") ?? undefined;
  return NextResponse.json({ ok: true, alerts: await listAlerts(status) });
}

export async function PATCH(req: NextRequest) {
  const s = await session();
  if (!s) {
    return NextResponse.json({ ok: false, error: "unauthorized" }, { status: 401 });
  }
  if (!canAcknowledge(s)) {
    return NextResponse.json({ ok: false, error: "forbidden" }, { status: 403 });
  }
  let body: { alert_id?: string; status?: string };
  try {
    body = await req.json();
  } catch {
    return NextResponse.json({ ok: false, error: "malformed JSON" }, { status: 400 });
  }
  if (!body.alert_id || (body.status !== "ACKNOWLEDGED" && body.status !== "RESOLVED")) {
    return NextResponse.json({ ok: false, error: "alert_id and status ACKNOWLEDGED|RESOLVED required" }, { status: 400 });
  }
  const updated = await setAlertStatus(body.alert_id, body.status, s.username);
  if (!updated) {
    return NextResponse.json({ ok: false, error: "not found" }, { status: 404 });
  }
  return NextResponse.json({ ok: true, alert: updated });
}
