"use client";
import { usePoll, ApiEvent } from "../components";

export default function Investigations() {
  const data = usePoll<{ ok: boolean; events: ApiEvent[] }>(
    "/api/events?limit=500",
    5000,
    { ok: false, events: [] }
  );
  const byId = new Map<string, ApiEvent[]>();
  for (const e of data.events ?? []) {
    if (!e.investigation_id) continue;
    if (!byId.has(e.investigation_id)) byId.set(e.investigation_id, []);
    byId.get(e.investigation_id)!.push(e);
  }
  return (
    <>
      <h1>INVESTIGATIONS</h1>
      <table>
        <thead><tr><th>ID</th><th>Target type</th><th>Target</th><th>Events</th><th>Last</th></tr></thead>
        <tbody>
          {[...byId.entries()].map(([id, list]) => (
            <tr key={id}>
              <td>{id}</td>
              <td>{list[0].target_type ?? ""}</td>
              <td>{list[0].target_reference ?? ""}</td>
              <td>{list.length}</td>
              <td>{list[0].timestamp}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
