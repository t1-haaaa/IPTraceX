"use client";
import { usePoll, ApiEvent } from "../components";

export default function Reports() {
  const data = usePoll<{ ok: boolean; events: ApiEvent[] }>(
    "/api/events?limit=200",
    5000,
    { ok: false, events: [] }
  );
  const rows = (data.events ?? []).filter((e) => e.event_type.startsWith("REPORT_"));
  const ok = rows.filter((e) => e.event_type === "REPORT_GENERATION_COMPLETE").length;
  const fail = rows.filter((e) => e.event_type === "REPORT_GENERATION_ERROR").length;
  return (
    <>
      <h1>REPORTS</h1>
      <div className="cards">
        <div className="card"><b>{ok}</b><div>generated</div></div>
        <div className="card"><b>{fail}</b><div>failed</div></div>
      </div>
      <table>
        <thead><tr><th>Time</th><th>Event</th><th>Target</th><th>Status</th></tr></thead>
        <tbody>
          {rows.map((e) => (
            <tr key={e.event_id}><td>{e.timestamp}</td><td>{e.event_type}</td>
              <td>{e.investigation_id ?? e.target_reference ?? ""}</td><td>{e.status ?? ""}</td></tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
