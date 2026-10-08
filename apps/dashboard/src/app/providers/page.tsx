"use client";
import { usePoll, ApiEvent } from "../components";

export default function Providers() {
  const data = usePoll<{ ok: boolean; events: ApiEvent[] }>(
    "/api/events?limit=500",
    5000,
    { ok: false, events: [] }
  );
  const agg = new Map<string, { ok: number; fail: number; timeouts: number; lat: number[]; last: string }>();
  for (const e of data.events ?? []) {
    if (!e.provider) continue;
    if (!agg.has(e.provider)) agg.set(e.provider, { ok: 0, fail: 0, timeouts: 0, lat: [], last: e.timestamp });
    const a = agg.get(e.provider)!;
    if (e.event_type === "PROVIDER_QUERY_COMPLETE") {
      a.ok += 1;
      if (e.duration_ms != null) a.lat.push(e.duration_ms);
    } else if (e.event_type === "PROVIDER_TIMEOUT") {
      a.timeouts += 1;
      a.fail += 1;
    } else if (e.event_type === "PROVIDER_QUERY_ERROR" || e.event_type === "PROVIDER_RATE_LIMITED") {
      a.fail += 1;
    }
    if (e.timestamp > a.last) a.last = e.timestamp;
  }
  return (
    <>
      <h1>PROVIDER HEALTH</h1>
      <table>
        <thead><tr><th>Provider</th><th>Status</th><th>Success</th><th>Failures</th><th>Timeouts</th><th>Avg latency</th><th>Last event</th></tr></thead>
        <tbody>
          {[...agg.entries()].map(([p, a]) => {
            const total = a.ok + a.fail;
            const rate = total ? ((100 * a.ok) / total).toFixed(1) : "—";
            const avg = a.lat.length ? `${Math.round(a.lat.reduce((x, y) => x + y, 0) / a.lat.length)}ms` : "—";
            const status = a.fail === 0 ? "ONLINE" : rate >= "50" ? "DEGRADED" : "OFFLINE";
            return (
              <tr key={p}>
                <td>{p}</td>
                <td className={status === "ONLINE" ? "ok" : "warn"}>{status}</td>
                <td>{rate}%</td><td>{a.fail}</td><td>{a.timeouts}</td><td>{avg}</td><td>{a.last}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </>
  );
}
