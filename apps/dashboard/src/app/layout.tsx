import "./globals.css";
import Link from "next/link";

const NAV = [
  ["OVERVIEW", "/"],
  ["LIVE EVENTS", "/events"],
  ["SECURITY", "/security"],
  ["INVESTIGATIONS", "/investigations"],
  ["PROVIDERS", "/providers"],
  ["REPORTS", "/reports"],
  ["SEARCH", "/search"],
  ["SYSTEM", "/system"],
  ["SETTINGS", "/settings"],
];

export const metadata = { title: "IPTraceX Monitor" };

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <body>
        <header className="top">
          <span className="brand">IPTraceX MONITOR</span>
          <nav>
            {NAV.map(([label, href]) => (
              <Link key={href} href={href}>
                {label}
              </Link>
            ))}
          </nav>
        </header>
        <main>{children}</main>
      </body>
    </html>
  );
}
