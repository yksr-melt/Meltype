/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング調査 (/typing/) の受付。どのキーが抜けやすいかを調べるため、お手本の文を打ったときの打鍵を集める。
// ページは誰でも遊べ、打鍵は調査に同意した人のものだけを受け付ける。
// 決まり: 受け付けるのはお手本の id と、それを打った打鍵だけ (自由に書いた文章を送る欄は作らない)。
// 決まり: IP はハッシュにして回数制限と利用禁止 (typing_bans) にだけ使い、書き出しには入れない。
import { align, finalKana } from "../public/assets/js/romaji.js";
import { sentenceById } from "../public/assets/js/typing-sentences.js";
import { error, json, now, randomToken, readJson, sha256Hex } from "./http.js";
import { claimRate, hashIp, verifyTurnstile } from "./reports.js";

// 参加 (同意) を始められる回数。1 回の参加で SESSION_HOURS のあいだ打てるので、人が 1 日に始め直すのは数回で足りる。
// 自動で何度も始め直して書き込み続けるのを止めるための上限
const TYPING_RATE = { kind: "typing", window: 10 * 60, windowLimit: 3, dayLimit: 6, unit: "回", verb: "参加を始められる" };
// 1 回の参加で打鍵を送れる時間と文の数。休みながら続けて打っても足りる量にしてある
const SESSION_HOURS = 12;
const MAX_TRIALS = 1200;
// 1 文の打鍵の上限は、読み 1 字あたりローマ字 3 打 + Shift・打ち直しの余裕を見て 12 打。
// 文ごとの書き込みの大きさをお手本の長さで抑える (いちばん長い文で 1 文 10 KB ほど)
const KEYS_PER_KANA = 12;
const TYPED_PER_KANA = 6;
const MAX_BODY = 32 * 1024;
const MAX_MS = 10 * 60 * 1000;
// かなにしたときのお手本との一致率がこれより低い文を、ダッシュボードで「お手本とかけ離れた送信」として出す
export const FLAG_ACCURACY = 0.5;

// 打った場所。ブラウザーのページ (web) と、ブラウザーで同意して出した番号で送るターミナルの CLI (cli, public/typing/meltype-typing.mjs)
const CLIENTS = ["web", "cli"];

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

const BANNED = "このページから送ることはできません";

async function banned(env, ipHash) {
  return Boolean(await env.DB.prepare("SELECT 1 FROM typing_bans WHERE ip_hash = ?").bind(ipHash).first());
}

function bearer(request) {
  const auth = request.headers.get("authorization") || "";
  const token = auth.startsWith("Bearer ") ? auth.slice(7).trim() : "";
  return token && token.length <= 128 ? token : "";
}

async function sessionFor(token, env) {
  return env.DB.prepare("SELECT id, ip_hash, created_at FROM typing_sessions WHERE token_hash = ?").bind(await sha256Hex(token)).first();
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

  const ipHash = await hashIp(request, env);
  if (await banned(env, ipHash)) return error(403, BANNED);
  const at = now();
  const limited = await claimRate(env, ipHash, at, TYPING_RATE);
  if (limited) return error(429, limited.message, { retryAfter: limited.retryAfter });

  const id = randomToken().slice(0, 16);
  const token = randomToken();
  await env.DB.prepare("INSERT INTO typing_sessions (id, token_hash, ip_hash, profile, created_at) VALUES (?, ?, ?, ?, ?)")
    .bind(id, await sha256Hex(token), ipHash, JSON.stringify(validProfile(body.profile)), at)
    .run();
  return json({ id, token }, { status: 201 });
}

const isInt = (value, min, max) => Number.isSafeInteger(value) && value >= min && value <= max;

// 打鍵は [code, key, 押した時刻 ms, 離した時刻 ms か null] の並び。時刻は文を打ち始めてからの経過
export function validKeys(keys, max) {
  if (!Array.isArray(keys) || keys.length < 1 || keys.length > max) return null;
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

// かなにしたときの、お手本の読みとの一致率 (結果の画面の「正確さ」と同じ数え方)
export function accuracyOf(reading, typed) {
  const length = [...reading].length;
  const wrong = align(reading, finalKana(typed)).filter((op) => op.op !== "same").length;
  return Math.max(0, 1 - wrong / length);
}

export async function addTrial(request, env) {
  const token = bearer(request);
  const session = token && (await sessionFor(token, env));
  if (!session) return error(401, "参加の番号が見つかりません。ページを読み込み直してください");
  if (session.created_at + SESSION_HOURS * 3600 < now()) return error(410, "参加を始めてから時間がたちました。もう一度同意してから送ってください");
  if (await banned(env, session.ip_hash)) return error(403, BANNED);
  const { body, error: invalid } = await readJson(request, MAX_BODY);
  if (invalid) return invalid;
  const sentence = sentenceById(body && body.sentence);
  if (!sentence) return error(400, "お手本の文が見つかりません");
  const length = [...sentence.reading].length;
  const typed = body.typed;
  if (typeof typed !== "string" || typed.length > length * TYPED_PER_KANA || !/^[\x20-\x7e]*$/.test(typed)) return error(400, "打った文字の形式が正しくありません");
  const keys = validKeys(body.keys, length * KEYS_PER_KANA);
  if (!keys) return error(400, "打鍵の記録の形式が正しくありません");
  if (!isInt(body.duration, 1, MAX_MS)) return error(400, "かかった時間の形式が正しくありません");
  const client = body.client === undefined ? "web" : body.client;
  if (!CLIENTS.includes(client)) return error(400, "打った場所の形式が正しくありません");

  const inserted = await env.DB.prepare(
    "INSERT INTO typing_trials (session_id, sentence_id, typed, keys, keystrokes, accuracy, client, duration_ms, created_at) " +
      "SELECT ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9 WHERE (SELECT COUNT(*) FROM typing_trials WHERE session_id = ?1) < ?10 RETURNING id",
  )
    .bind(session.id, sentence.id, typed, JSON.stringify(keys), keys.length, accuracyOf(sentence.reading, typed), client, body.duration, now(), MAX_TRIALS)
    .first();
  if (!inserted) return error(429, `1 回の参加で送れるのは ${MAX_TRIALS} 文までです。ありがとうございました！`);
  return json({ id: inserted.id }, { status: 201 });
}

// 参加者が自分の参加のデータを消す。ページは、このブラウザーで同意した参加の番号をすべて覚えていて、1 つずつ消す
export async function deleteSession(request, env) {
  const token = bearer(request);
  const session = token && (await sessionFor(token, env));
  if (!session) return error(404, "消すデータが見つかりません (もう消してあるかもしれません)");
  await env.DB.batch([
    env.DB.prepare("DELETE FROM typing_trials WHERE session_id = ?").bind(session.id),
    env.DB.prepare("DELETE FROM typing_sessions WHERE id = ?").bind(session.id),
  ]);
  return json({ ok: true });
}

export async function typingStats(env) {
  const row = await env.DB.prepare(
    "SELECT (SELECT COUNT(*) FROM typing_sessions) AS sessions, COUNT(*) AS trials, COALESCE(SUM(keystrokes), 0) AS keystrokes FROM typing_trials",
  ).first();
  return json(row);
}

// お手本とかけ離れた文を送った参加を、多い順に出す。利用禁止にするかはダッシュボードで人が決める
export async function flaggedSessions(env) {
  const { results } = await env.DB.prepare(
    "SELECT s.id, s.created_at, COUNT(*) AS flagged, " +
      "(SELECT COUNT(*) FROM typing_trials a WHERE a.session_id = s.id) AS trials, " +
      "(SELECT json_group_array(typed) FROM (SELECT typed FROM typing_trials b WHERE b.session_id = s.id AND b.accuracy < ?1 ORDER BY b.id LIMIT 5)) AS samples, " +
      "EXISTS (SELECT 1 FROM typing_bans x WHERE x.ip_hash = s.ip_hash) AS banned " +
      "FROM typing_trials t JOIN typing_sessions s ON s.id = t.session_id WHERE t.accuracy < ?1 " +
      "GROUP BY s.id ORDER BY flagged DESC, s.created_at DESC LIMIT 100",
  )
    .bind(FLAG_ACCURACY)
    .all();
  return json({
    threshold: FLAG_ACCURACY,
    sessions: results.map((row) => ({ ...row, samples: JSON.parse(row.samples), banned: Boolean(row.banned) })),
  });
}

// 参加の IP を利用禁止にする・解く。ダッシュボードは Access の内側だが、別のサイトから送らせる書き込みを防ぐため同じオリジンからだけ受け付ける
export async function setBan(request, env, url, sessionId, ban) {
  if (request.headers.get("origin") !== url.origin) return error(403, "このページからは操作できません");
  const session = await env.DB.prepare("SELECT ip_hash FROM typing_sessions WHERE id = ?").bind(sessionId).first();
  if (!session) return error(404, "参加が見つかりません");
  if (ban) await env.DB.prepare("INSERT OR IGNORE INTO typing_bans (ip_hash, created_at) VALUES (?, ?)").bind(session.ip_hash, now()).run();
  else await env.DB.prepare("DELETE FROM typing_bans WHERE ip_hash = ?").bind(session.ip_hash).run();
  return json({ banned: ban });
}

// 研究用に書き出す。after より大きい id から limit 件ずつ返すので、next が null になるまで続けて読む
export async function exportTrials(env, url) {
  const after = Math.max(0, Number(url.searchParams.get("after")) || 0);
  const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get("limit")) || 500));
  const { results } = await env.DB.prepare(
    "SELECT t.id, t.session_id, s.profile, t.sentence_id, t.typed, t.keys, t.accuracy, t.client, t.duration_ms, t.created_at " +
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
      accuracy: row.accuracy,
      client: row.client,
      duration_ms: row.duration_ms,
      created_at: row.created_at,
    };
  });
  return json({ trials, next: more ? trials[trials.length - 1].id : null });
}
