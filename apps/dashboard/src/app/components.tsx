"use client";
import { useEffect, useState } from "react";

export interface ApiEvent {
  event_id: string;
  timestamp: string;
  severity: string;
  event_type: string;
  category?: string | null;
  component?: string | null;
  provider?: string | null;
  investigation_id?: string | null;
  correlation_id?: string | null;
  session_id?: string | null;
  target_type?: string | null;
  target_reference?: string | null;
  status?: string | null;
  duration_ms?: number | null;
}

export function usePoll<T>(url: string, ms: number, initial: T): T {
  const [data, setData] = useState<T>(initial);
  useEffect(() => {
    let alive = true;
    const load = async () => {
      try {
        const res = await fetch(url, { cache: "no-store" });
        if (res.ok && alive) setData(await res.json());
      } catch {
        // polling is best-effort; the table keeps its last state
      }
    };
    load();
    const t = setInterval(load, ms);
    return () => {
      alive = false;
      clearInterval(t);
    };
  }, [url, ms]);
  return data;
}

export function sevClass(s: string): string {
  return s === "ALERT" || s === "CRITICAL" ? "alert" : s === "WARNING" || s === "ERROR" ? "warn" : "";
}

export function timeOf(iso: string): string {
  try {
    return new Date(iso).toLocaleTimeString("en-GB", { hour12: false });
  } catch {
    return iso;
  }
}
