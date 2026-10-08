"use client";
import { useState } from "react";

export default function System() {
  const [out, setOut] = useState("not checked");
  const check = async () => {
    try {
      const res = await fetch("/api/events?limit=1", { cache: "no-store" });
      setOut(res.ok ? "API ONLINE · DATABASE ONLINE" : `API DEGRADED (${res.status})`);
    } catch {
      setOut("API OFFLINE");
    }
  };
  return (
    <>
      <h1>SYSTEM</h1>
      <p><button onClick={check}>check status</button></p>
      <p>{out}</p>
      <h2>Configuration</h2>
      <p>DATABASE_URL: {process.env.NEXT_PUBLIC_DB_HINT ?? "server-side only (never exposed)"}</p>
    </>
  );
}
