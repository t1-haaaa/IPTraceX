// Applies schema.sql to DATABASE_URL (Postgres). Usage: npm run migrate
import { readFileSync } from "fs";
import { join, dirname } from "path";
import { fileURLToPath } from "url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const url = process.env.DATABASE_URL;
if (!url) {
  console.error("DATABASE_URL is not set; skipping migration.");
  process.exit(2);
}
const { default: pg } = await import("pg");
const sql = readFileSync(join(root, "schema.sql"), "utf8");
const pool = new pg.Pool({ connectionString: url });
try {
  await pool.query(sql);
  console.log("migration applied");
} finally {
  await pool.end();
}
