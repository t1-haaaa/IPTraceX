# Dashboard Auth

- Login: `POST /api/auth` (username + password, SHA-256 + per-deploy
  salt, timing-safe compare). Failures return 401 without user
  enumeration detail.
- Sessions: 32-byte random token in an `HttpOnly`, `SameSite=lax`
  cookie (`Secure` in production), 12h expiry, persisted in the
  dashboard store (works across instances; verified end-to-end).
- Middleware redirects unauthenticated page/API reads to `/login`;
  every read API re-checks the session server-side. The browser never
  sees the ingest secret or other users' sessions.
- Roles: ADMIN (everything), ANALYST (read + acknowledge alerts +
  investigate), VIEWER (read-only). Privileged actions (acknowledge,
  resolve, settings) must emit dashboard audit events.
- Production requires `IPTRACEX_DASHBOARD_USERS`,
  `IPTRACEX_DASHBOARD_PASSWORD_<NAME>`,
  `IPTRACEX_DASHBOARD_SESSION_SECRET`. Dev-only fallback
  (`admin`/`iptracex-dev-only`) exists only when `NODE_ENV` is not
  production and no users are configured.
