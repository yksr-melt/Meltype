/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング調査 (/typing/) の受付。どのキーが抜けやすいかを調べるため、お手本の文を打ったときの打鍵を集める。
// 決まり: 参加者が自分で書いた文章は受け付けない (お手本の id と、それを打った打鍵だけ)。
// 決まり: IP はハッシュにして回数制限にだけ使い、調査のデータには入れない (rate_events は 1 日で消える)。
import { sentenceById } from "../public/assets/js/typing-sentences.js";
import { error, json, now, randomToken, readJson, sha256Hex } from "./http.js";
import { claimRate, hashIp, verifyTurnstile } from "./reports.js";

const TYPING_RATE = { kind: "typing", window: 10 * 60, windowLimit: 5, dayLimit: 20, unit: "回", verb: "始められる" };
// 参加を始めてから打鍵を送れる時間と、1 回の参加で送れる文の数
const SESSION_HOURS = 12;
const MAX_TRIALS = 600;
const MAX_BODY = 128 * 1024;
const MAX_KEYS = 3000;
const MAX_TYPED = 300;
const MAX_MS = 10 * 60 * 1000;

export const PROFILE_CHOICES = {
  touch: ["yes", "partly", "no", "unknown"],
  layout: ["jis", "us", "other", "unknown"],
  method: ["romaji", "kana", "other", "unknown"],
  os: ["windows", "mac", "linux", "chromeos", "other"],
};

function validProfile(input) {
  const profile = {};
  for (const [name, choices] of Object.entries(PROFILE_CHOICES)) {
    const value = input && input[name];
    profile[name] = choices.includes(value) ? value : choices.includes("unknown") ? "unknown" : "other";
  }
  return profile;
}

async function sessionFor(request, env) {
  const auth = request.headers.get("authorization") || "";
  const token = auth.startsWith("Bearer ") ? auth.slice(7).trim() : "";
  if (!token || token.length > 128) return null;
  return env.DB.prepare("SELECT id, created_at FROM typing_sessions WHERE token_hash = ?").bind(await sha256Hex(token)).first();
}

export async function createSession(request, env) {
  const { body, error: invalid } = await readJson(request, 8 * 1024);
  if (invalid) return invalid;
  if (!body || body.consent !== true) return error(400, "参加への同意を確かめられませんでした");
  if (!env.IP_HASH_SALT) {
    console.error("IP_HASH_SALT が設定されていないため、タイピング調査を受け付けていません");
    return error(503, "いまは受け付けられません。時間をおいてもう一度試してください");
  }
  const human = await verifyTurnstile(request, env, body.turnstile, "typing");
  if (human) return error(human.status, human.message);

  const at = now();
  const limited = await claimRate(env, await hashIp(request, env), at, TYPING_RATE);
  if (limited) return error(429, limited.message, { retryAfter: limited.retryAfter });

  const id = randomToken().slice(0, 16);
  const token = randomToken();
  await env.DB.prepare("INSERT INTO typing_sessions (id, token_hash, profile, created_at) VALUES (?, ?, ?, ?)")
    .bind(id, await sha256Hex(token), JSON.stringify(validProfile(body.profile)), at)
    .run();
  return json({ id, token }, { status: 201 });
}

const isInt = (value, min, max) => Number.isSafeInteger(value) && value >= min && value <= max;

// 打鍵は [code, key, 押した時刻 ms, 離した時刻 ms か null] の並び。時刻は文を打ち始めてからの経過
export function validKeys(keys) {
  if (!Array.isArray(keys) || keys.length < 1 || keys.length > MAX_KEYS) return null;
  const out = [];
  let last = 0;
  for (const entry of keys) {
    if (!Array.isArray(entry) || entry.length !== 4) return null;
    const [code, key, down, up] = entry;
    if (typeof code !== "string" || !/^[A-Za-z0-9]{0,32}$/.test(code)) return null;
    if (typeof key !== "string" || key.length > 16) return null;
    if (!isInt(down, 0, MAX_MS) || down < last) return null;
    if (up !== null && !isInt(up, down, MAX_MS)) return null;
    last = down;
    out.push([code, key, down, up]);
  }
  return out;
}

export async function addTrial(request, env) {
  const session = await sessionFor(request, env);
  if (!session) return error(401, "参加の番号が見つかりません。ページを読み込み直してください");
  if (session.created_at + SESSION_HOURS * 3600 < now()) return error(410, "参加を始めてから時間がたちました。もう一度始めてください");
  const { body, error: invalid } = await readJson(request, MAX_BODY);
  if (invalid) return invalid;
  const sentence = sentenceById(body && body.sentence);
  if (!sentence) return error(400, "お手本の文が見つかりません");
  const typed = body.typed;
  if (typeof typed !== "string" || typed.length > MAX_TYPED || !/^[\x20-\x7e]*$/.test(typed)) return error(400, "打った文字の形式が正しくありません");
  const keys = validKeys(body.keys);
  if (!keys) return error(400, "打鍵の記録の形式が正しくありません");
  if (!isInt(body.duration, 1, MAX_MS)) return error(400, "かかった時間の形式が正しくありません");

  const inserted = await env.DB.prepare(
    "INSERT INTO typing_trials (session_id, sentence_id, typed, keys, duration_ms, created_at) " +
      "SELECT ?1, ?2, ?3, ?4, ?5, ?6 WHERE (SELECT COUNT(*) FROM typing_trials WHERE session_id = ?1) < ?7 RETURNING id",
  )
    .bind(session.id, sentence.id, typed, JSON.stringify(keys), body.duration, now(), MAX_TRIALS)
    .first();
  if (!inserted) return error(429, `1 回の参加で送れるのは ${MAX_TRIALS} 文までです。ありがとうございました！`);
  return json({ id: inserted.id }, { status: 201 });
}

export async function deleteSession(request, env) {
  const session = await sessionFor(request, env);
  if (!session) return error(404, "消すデータが見つかりません (もう消してあるかもしれません)");
  await env.DB.batch([
    env.DB.prepare("DELETE FROM typing_trials WHERE session_id = ?").bind(session.id),
    env.DB.prepare("DELETE FROM typing_sessions WHERE id = ?").bind(session.id),
  ]);
  return json({ ok: true });
}

export async function typingStats(env) {
  const row = await env.DB.prepare(
    "SELECT (SELECT COUNT(*) FROM typing_sessions) AS sessions, (SELECT COUNT(*) FROM typing_trials) AS trials, " +
      "(SELECT COALESCE(SUM(json_array_length(keys)), 0) FROM typing_trials) AS keystrokes",
  ).first();
  return json(row);
}

// 研究用に書き出す。after より大きい id から limit 件ずつ返すので、next が null になるまで続けて読む
export async function exportTrials(env, url) {
  const after = Math.max(0, Number(url.searchParams.get("after")) || 0);
  const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get("limit")) || 500));
  const { results } = await env.DB.prepare(
    "SELECT t.id, t.session_id, s.profile, t.sentence_id, t.typed, t.keys, t.duration_ms, t.created_at " +
      "FROM typing_trials t JOIN typing_sessions s ON s.id = t.session_id WHERE t.id > ? ORDER BY t.id LIMIT ?",
  )
    .bind(after, limit + 1)
    .all();
  const more = results.length > limit;
  const trials = results.slice(0, limit).map((row) => {
    const sentence = sentenceById(row.sentence_id);
    return {
      id: row.id,
      session: row.session_id,
      profile: JSON.parse(row.profile),
      sentence: row.sentence_id,
      text: sentence ? sentence.text : null,
      reading: sentence ? sentence.reading : null,
      typed: row.typed,
      keys: JSON.parse(row.keys),
      duration_ms: row.duration_ms,
      created_at: row.created_at,
    };
  });
  return json({ trials, next: more ? trials[trials.length - 1].id : null });
}
