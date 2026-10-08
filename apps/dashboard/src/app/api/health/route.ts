import { NextResponse } from "next/server";

// Liveness + dependency status. Never exposes secrets.
export async function GET() {
  let database: "ok" | "degraded" | "unconfigured" = "unconfigured";
  if (process.env.DATABASE_URL) {
    try {
      const { default: pgMod } = (await import("pg")) as unknown as {
        default: { Pool: new (cfg: unknown) => { query: (q: string) => Promise<unknown>; end: () => Promise<void> } };
      };
      const pool = new pgMod.Pool({
        connectionString: process.env.DATABASE_URL,
        max: 1,
        connectionTimeoutMillis: 3000,
      });
      try {
        await pool.query("SELECT 1");
        database = "ok";
      } catch {
        database = "degraded";
      } finally {
        await pool.end().catch(() => undefined);
      }
    } catch {
      database = "degraded";
    }
  }
  const status = database === "degraded" ? "degraded" : "ok";
  return NextResponse.json(
    { status, database, audit: database === "degraded" ? "degraded" : "ok" },
    { status: database === "degraded" ? 503 : 200 }
  );
}
