"use client";
import { useEffect, useState } from "react";
import { ApiEvent } from "../../components";

export default function EventDetail({ params }: { params: { id: string } }) {
  const [data, setData] = useState<{ ok: boolean; event?: ApiEvent & Record<string, unknown>; related?: ApiEvent[] } | null>(null);
  useEffect(() => {
    fetch(`/api/events/${encodeURIComponent(params.id)}`, { cache: "no-store" })
      .then((r) => r.json())
      .then(setData)
      .catch(() => setData({ ok: false }));
  }, [params.id]);
  if (!data) return <p>loading…</p>;
  if (!data.ok || !data.event) return <p>event not found.</p>;
  const e = data.event;
  const rows: [string, unknown][] = [
    ["Event ID", e.event_id], ["Timestamp", e.timestamp], ["Severity", e.severity],
    ["Event type", e.event_type], ["Category", e.category], ["Component", e.component],
    ["Operation", e.operation], ["Status", e.status], ["Duration", e.duration_ms],
    ["Session", e.session_id], ["Correlation", e.correlation_id],
    ["Investigation", e.investigation_id], ["Provider", e.provider],
    ["Error code", e.error_code], ["HTTP status", e.http_status], ["Message", e.message],
  ];
  return (
    <>
      <h1>EVENT DETAIL</h1>
      <table><tbody>
        {rows.map(([k, v]) => (
          <tr key={k}><th>{k}</th><td>{v == null ? "" : String(v)}</td></tr>
        ))}
      </tbody></table>
      <h2>RELATED EVENTS{(e.correlation_id ? ` — ${e.correlation_id}` : "")}</h2>
      <table><tbody>
        {(data.related ?? []).map((r) => (
          <tr key={r.event_id}><td>{r.timestamp}</td><td>{r.event_type}</td><td>{r.status ?? ""}</td></tr>
        ))}
      </tbody></table>
    </>
  );
}
