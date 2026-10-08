"use client";
import { useState } from "react";
import { usePoll, sevClass, timeOf, ApiEvent } from "../components";

export default function Events() {
  const [severity, setSeverity] = useState("");
  const [type, setType] = useState("");
  const [cursor, setCursor] = useState("");
  const q = `/api/events?limit=100${severity ? `&severity=${severity}` : ""}${type ? `&event_type=${type}` : ""}${cursor ? `&cursor=${cursor}` : ""}`;
  const data = usePoll<{ ok: boolean; events: ApiEvent[] }>(q, 3000, { ok: false, events: [] });
  const rows = data.events ?? [];
  return (
    <>
      <h1>LIVE EVENTS</h1>
      <p>
        <select value={severity} onChange={(e) => { setSeverity(e.target.value); setCursor(""); }}>
          <option value="">all severities</option>
          {["INFO", "WARNING", "ERROR", "ALERT", "CRITICAL"].map((s) => (
            <option key={s} value={s}>{s}</option>
          ))}
        </select>{" "}
        <input placeholder="event type" value={type} onChange={(e) => { setType(e.target.value); setCursor(""); }} />
      </p>
      <table>
        <thead>
          <tr><th>TIME</th><th>SEVERITY</th><th>EVENT</th><th>COMPONENT</th><th>PROVIDER</th><th>TARGET</th><th>STATUS</th><th>DURATION</th></tr>
        </thead>
        <tbody>
          {rows.map((e) => (
            <tr key={e.event_id}>
              <td>{timeOf(e.timestamp)}</td>
              <td className={sevClass(e.severity)}>{e.severity}</td>
              <td><a href={`/events/${e.event_id}`}>{e.event_type}</a></td>
              <td>{e.component ?? ""}</td>
              <td>{e.provider ?? ""}</td>
              <td>{e.investigation_id ?? e.target_reference ?? ""}</td>
              <td>{e.status ?? ""}</td>
              <td>{e.duration_ms != null ? `${e.duration_ms}ms` : ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {rows.length > 0 && (
        <p><button onClick={() => setCursor(rows[rows.length - 1].event_id)}>older →</button></p>
      )}
    </>
  );
}
