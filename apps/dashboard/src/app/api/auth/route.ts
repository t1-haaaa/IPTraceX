import { NextRequest, NextResponse } from "next/server";
import { login, createSessionToken, destroySession } from "../../../lib/auth";
import { cookies } from "next/headers";

export async function POST(req: NextRequest) {
  let body: { username?: string; password?: string };
  try {
    body = await req.json();
  } catch {
    return NextResponse.json({ ok: false, error: "malformed JSON" }, { status: 400 });
  }
  const session = login(String(body.username ?? ""), String(body.password ?? ""));
  if (!session) {
    return NextResponse.json({ ok: false, error: "invalid credentials" }, { status: 401 });
  }
  const token = await createSessionToken(session);
  cookies().set("iptracex_session", token, {
    httpOnly: true,
    sameSite: "lax",
    secure: process.env.NODE_ENV === "production",
    path: "/",
    maxAge: 12 * 3600,
  });
  return NextResponse.json({ ok: true, role: session.role });
}

export async function DELETE() {
  await destroySession(cookies().get("iptracex_session")?.value);
  cookies().delete("iptracex_session");
  return NextResponse.json({ ok: true });
}
