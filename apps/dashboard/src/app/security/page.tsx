"use client";
import { usePoll, sevClass, timeOf, ApiEvent } from "../components";

function Section({ title, types }: { title: string; types: string }) {
  const data = usePoll<{ ok: boolean; events: ApiEvent[] }>(
    `/api/events?limit=100&search=${encodeURIComponent(types.split(" ")[0])}`,
    5000,
    { ok: false, events: [] }
  );
  const rows = (data.events ?? []).filter((e) => types.split(" ").includes(e.event_type));
  const latest = rows[0]?.timestamp ? timeOf(rows[0].timestamp) : "—";
  return (
    <div className="card">
      <b>{rows.length}</b>
      <div>{title}</div>
      <div>latest: {latest}</div>
    </div>
  );
}

export default function Security() {
  return (
    <>
      <h1>SECURITY</h1>
      <div className="cards">
        <Section title="Authentication failures" types="AUTH_FAILURE DASHBOARD_LOGIN_FAILURE" />
        <Section title="Access denied" types="ACCESS_DENIED" />
        <Section title="Rate limits" types="RATE_LIMIT_TRIGGERED" />
        <Section title="Suspicious input" types="SUSPICIOUS_INPUT" />
        <Section title="Invalid requests" types="INVALID_REQUEST INVALID_PAYLOAD INVALID_METHOD UNEXPECTED_REQUEST" />
        <Section title="Security alerts" types="SECURITY_ALERT EMAIL_SECURITY_ALERT" />
      </div>
      <p className="warn">Labels describe observed signals (e.g. SUSPICIOUS_INPUT), never unproven compromise.</p>
    </>
  );
}
