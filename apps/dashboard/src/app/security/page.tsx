"use client";
import { usePoll, sevClass, timeOf, ApiEvent } from "../components";
import { useState } from "react";

interface Alert {
  alert_id: string;
  timestamp: string;
  rule: string;
  severity: string;
  reason: string;
  count: number;
  status: string;
}

function Alerts() {
  const data = usePoll<{ ok: boolean; alerts: Alert[] }>("/api/alerts", 5000, { ok: false, alerts: [] });
  const [msg, setMsg] = useState("");
  const act = async (alert_id: string, status: string) => {
    const res = await fetch("/api/alerts", {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ alert_id, status }),
    });
    setMsg(res.ok ? `${alert_id} → ${status}` : `failed (${res.status})`);
  };
  const rows = data.alerts ?? [];
  return (
    <>
      <h2>SECURITY ALERTS</h2>
      {msg && <p>{msg}</p>}
      <table>
        <thead><tr><th>Time</th><th>Rule</th><th>Severity</th><th>Reason</th><th>Status</th><th>Action</th></tr></thead>
        <tbody>
          {rows.map((a) => (
            <tr key={a.alert_id}>
              <td>{timeOf(a.timestamp)}</td>
              <td>{a.rule}</td>
              <td className={sevClass(a.severity)}>{a.severity}</td>
              <td>{a.reason}</td>
              <td>{a.status}</td>
              <td>
                {a.status === "NEW" && <button onClick={() => act(a.alert_id, "ACKNOWLEDGED")}>acknowledge</button>}{" "}
                {a.status !== "RESOLVED" && <button onClick={() => act(a.alert_id, "RESOLVED")}>resolve</button>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

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
      <Alerts />
    </>
  );
}
