"use client";
import { useState } from "react";

export default function Login() {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [msg, setMsg] = useState("");
  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    const res = await fetch("/api/auth", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ username, password }),
    });
    if (res.ok) {
      window.location.href = "/";
    } else {
      setMsg("invalid credentials");
    }
  };
  return (
    <>
      <h1>DASHBOARD LOGIN</h1>
      <form className="login" onSubmit={submit}>
        <input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="username" />
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} placeholder="password" />
        <button type="submit">sign in</button>
      </form>
      {msg && <p className="alert">{msg}</p>}
    </>
  );
}
