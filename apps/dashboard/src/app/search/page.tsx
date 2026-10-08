"use client";
import { useState } from "react";
import { sevClass, timeOf, ApiEvent } from "../components";

export default function Search() {
  const [q, setQ] = useState("EMX-");
  const [rows, setRows] = useState<ApiEvent[]>([]);
  const [ran, setRan] = useState(false);
  const run = async () => {
    const res = await fetch(`/api/events?limit=200&search=${encodeURIComponent(q)}`, { cache: "no-store" });
    const data = await res.json();
    setRows(data.events ?? []);
    setRan(true);
  };
  return (
    <>
      <h1>SEARCH</h1>
      <p>
        <input value={q} onChange={(e) => setQ(e.target.value)} placeholder="event, session, correlation, investigation, provider…" />{" "}
        <button onClick={run}>search</button>
      </p>
      {ran && <p>{rows.length} matching events</p>}
      <table>
        <tbody>
          {rows.map((e) => (
            <tr key={e.event_id}>
              <td>{timeOf(e.timestamp)}</td>
              <td className={sevClass(e.severity)}>{e.severity}</td>
              <td><a href={`/events/${e.event_id}`}>{e.event_type}</a></td>
              <td>{e.investigation_id ?? e.correlation_id ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
