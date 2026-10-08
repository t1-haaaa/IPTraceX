export default function Settings() {
  return (
    <>
      <h1>SETTINGS</h1>
      <p>Roles: ADMIN (everything) · ANALYST (read + acknowledge) · VIEWER (read-only).</p>
      <p>Configure via environment: IPTRACEX_DASHBOARD_USERS, IPTRACEX_DASHBOARD_PASSWORD_&lt;NAME&gt;.</p>
      <p>Ingest secret: IPTRACEX_AUDIT_INGEST_SECRET (server only, never in the browser).</p>
      <p>Session secret: IPTRACEX_DASHBOARD_SESSION_SECRET.</p>
    </>
  );
}
