/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { error } from "./http.js";

const CERTS_TTL_MS = 60 * 60 * 1000;
const CERTS_REFRESH_MS = 60 * 1000;
const LEEWAY = 60;
let certsCache = { domain: "", keys: null, fetchedAt: 0 };

function base64UrlDecode(text) {
  const padded = text.replace(/-/g, "+").replace(/_/g, "/") + "=".repeat((4 - (text.length % 4)) % 4);
  return Uint8Array.from(atob(padded), (c) => c.charCodeAt(0));
}

function decodeJson(part) {
  return JSON.parse(new TextDecoder().decode(base64UrlDecode(part)));
}

function teamDomain(env) {
  return String(env.ACCESS_TEAM_DOMAIN || "").trim().replace(/^https?:\/\//, "").replace(/\/+$/, "");
}

async function loadKeys(domain, force = false) {
  const age = certsCache.domain === domain && certsCache.keys ? Date.now() - certsCache.fetchedAt : Infinity;
  if (age < (force ? CERTS_REFRESH_MS : CERTS_TTL_MS)) return certsCache.keys;
  const response = await fetch(`https://${domain}/cdn-cgi/access/certs`);
  if (!response.ok) throw new Error(`Access certs: ${response.status}`);
  const { keys } = await response.json();
  const imported = new Map();
  for (const jwk of keys || []) {
    if (jwk.kty !== "RSA" || !jwk.kid) continue;
    imported.set(jwk.kid, await crypto.subtle.importKey("jwk", jwk, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["verify"]));
  }
  certsCache = { domain, keys: imported, fetchedAt: Date.now() };
  return imported;
}

export async function verifyAccess(request, env) {
  const domain = teamDomain(env);
  const aud = String(env.ACCESS_AUD || "").trim();
  if (!domain || !aud) return null;
  const parts = (request.headers.get("cf-access-jwt-assertion") || "").trim().split(".");
  if (parts.length !== 3) return null;
  let header;
  let payload;
  try {
    header = decodeJson(parts[0]);
    payload = decodeJson(parts[1]);
  } catch {
    return null;
  }
  if (header.alg !== "RS256" || !header.kid) return null;
  let key = (await loadKeys(domain)).get(header.kid);
  if (!key) key = (await loadKeys(domain, true)).get(header.kid);
  if (!key) return null;
  let signature;
  try {
    signature = base64UrlDecode(parts[2]);
  } catch {
    return null;
  }
  const ok = await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, signature, new TextEncoder().encode(`${parts[0]}.${parts[1]}`));
  if (!ok) return null;
  const at = Math.floor(Date.now() / 1000);
  const audiences = Array.isArray(payload.aud) ? payload.aud : [payload.aud];
  if (!audiences.includes(aud)) return null;
  if (payload.iss !== `https://${domain}`) return null;
  if (typeof payload.exp !== "number" || payload.exp + LEEWAY < at) return null;
  if (typeof payload.nbf === "number" && payload.nbf - LEEWAY > at) return null;
  return payload;
}

export async function requireAccess(request, env) {
  if (env.DASHBOARD_DEV_BYPASS === "1" && ["localhost", "127.0.0.1"].includes(new URL(request.url).hostname)) return null;
  if (!teamDomain(env) || !String(env.ACCESS_AUD || "").trim()) {
    console.error("ACCESS_TEAM_DOMAIN と ACCESS_AUD が設定されていないため、ダッシュボードを閉じています");
    return error(503, "ダッシュボードは準備中です");
  }
  try {
    if (await verifyAccess(request, env)) return null;
  } catch (e) {
    console.error("Access の確認に失敗しました", e);
    return error(503, "ダッシュボードは準備中です");
  }
  return error(403, "メンテナーだけが見られます");
}

export function _resetAccessCache() {
  certsCache = { domain: "", keys: null, fetchedAt: 0 };
}
