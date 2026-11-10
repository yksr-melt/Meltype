/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

export function json(data, init = {}) {
  const headers = new Headers(init.headers);
  headers.set("content-type", "application/json; charset=utf-8");
  if (!headers.has("cache-control")) headers.set("cache-control", "no-store");
  headers.set("x-content-type-options", "nosniff");
  return new Response(JSON.stringify(data), { ...init, headers });
}

export function error(status, message, extra = {}) {
  const headers = {};
  if (extra.retryAfter) headers["retry-after"] = String(extra.retryAfter);
  return json({ error: message, ...extra }, { status, headers });
}

export function now() {
  return Math.floor(Date.now() / 1000);
}

export async function sha256Hex(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function randomToken() {
  const bytes = crypto.getRandomValues(new Uint8Array(32));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

export async function readJson(request, maxBytes) {
  if (!(request.headers.get("content-type") || "").toLowerCase().startsWith("application/json")) {
    return { error: error(415, "送信の形式が正しくありません") };
  }
  const declared = Number(request.headers.get("content-length") || 0);
  if (declared > maxBytes) return { error: error(413, "ンァーッ！報告の文が長すぎます！") };
  const text = await request.text();
  if (new TextEncoder().encode(text).length > maxBytes) return { error: error(413, "ンァーッ！報告の文が長すぎます！") };
  try {
    return { body: JSON.parse(text) };
  } catch {
    return { error: error(400, "送信の形式が正しくありません") };
  }
}
