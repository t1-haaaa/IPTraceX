"use client";
import { usePoll, sevClass, timeOf, ApiEvent } from "./components";

export default function Overview() {
  const data = usePoll<{ ok: boolean; metrics: Record<string, number> }>(
    "/api/events/metrics",
    4000,
    { ok: false, metrics: {} }
  );
  const m = data.metrics;
  const live = usePoll<{ ok: boolean; events: ApiEvent[] }>(
    "/api/events?limit=8",
    4000,
    { ok: false, events: [] }
  );
  const card = (label: string, v: number | undefined) => (
    <div className="card">
      <b>{v ?? "—"}</b>
      <div>{label}</div>
    </div>
  );
  return (
    <>
      <h1>IPTraceX MONITOR</h1>
      <h2>ACTIVITY</h2>
      <div className="cards">
        {card("Events (window)", m.events)}
        {card("Security alerts", m.alerts)}
        {card("Auth failures", m.authFailures)}
        {card("Rate limits", m.rateLimits)}
        {card("Suspicious input", m.suspicious)}
        {card("Provider failures", m.providerErrors)}
      </div>
      <h2>RECENT</h2>
      <table>
        <tbody>
          {(live.events ?? []).map((e) => (
            <tr key={e.event_id}>
              <td>{timeOf(e.timestamp)}</td>
              <td className={sevClass(e.severity)}>{e.severity}</td>
              <td>{e.event_type}</td>
              <td>{e.provider ?? e.component ?? ""}</td>
              <td>{e.status ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
