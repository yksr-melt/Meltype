/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング調査 (/typing/) の受付。どのキーが抜けやすいかを調べるため、お手本の文を打ったときの打鍵を集める。
// ページは誰でも遊べ、打鍵は調査に同意した人のものだけを受け付ける。
// 決まり: 受け付けるのはお手本の id と、それを打った打鍵 (keys) だけ (打った文字は打鍵から組み立てる。自由に書いた文章を送る欄は作らない)。
// 決まり: IP はハッシュにして回数制限にだけ使い、調査のデータには入れない (rate_events は 1 日で消える)。
import { accuracyOf, replay, suspicious } from "../public/assets/js/typing-check.js";
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
const KEYS_PER_CHAR = 12;
const MAX_BODY = 32 * 1024;
const MAX_MS = 10 * 60 * 1000;
// かなにしたときのお手本との一致率がこれより低い文を、ダッシュボードで「お手本とかけ離れた送信」として出す
export const FLAG_ACCURACY = 0.5;

export const PROFILE_CHOICES = {
  touch: ["yes", "partly", "no", "unknown"],
  layout: ["jis", "us", "other", "unknown"],
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

// 打鍵は [code, key, 押した時刻 ms, 離した時刻 ms か null] の並び。時刻は文を打ち始めてからの経過。
// Backspace の押しっぱなしは、繰り返しのたびに 1 つ (離した時刻は null) 入る
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

export async function addTrial(request, env) {
  const session = await sessionFor(request, env);
  if (!session) return error(401, "参加の番号が見つかりません。ページを読み込み直してください");
  if (session.created_at + SESSION_HOURS * 3600 < now()) return error(410, "参加を始めてから時間がたちました。もう一度同意してから送ってください");
  const { body, error: invalid } = await readJson(request, MAX_BODY);
  if (invalid) return invalid;
  const sentence = sentenceById(body && body.sentence);
  if (!sentence) return error(400, "お手本の文が見つかりません");
  const keys = validKeys(body.keys, [...sentence.reading].length * KEYS_PER_CHAR);
  if (!keys) return error(400, "打鍵の記録の形式が正しくありません");
  if (!isInt(body.duration, 1, MAX_MS)) return error(400, "かかった時間の形式が正しくありません");
  const typed = replay(keys);
  // 名前など、お手本と関係ない入力が続く文は残さない (ページも送る前に同じ判定で止める)
  if (suspicious(sentence.reading, typed)) return error(422, "お手本と関係ない入力が続いたので、この文は受け付けません");

  const inserted = await env.DB.prepare(
    "INSERT INTO typing_trials (session_id, sentence_id, sentence_text, reading, keys, keystrokes, accuracy, duration_ms, created_at) " +
      "SELECT ?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9 WHERE (SELECT COUNT(*) FROM typing_trials WHERE session_id = ?1) < ?10 RETURNING id",
  )
    .bind(session.id, sentence.id, sentence.text, sentence.reading, JSON.stringify(keys), keys.length, accuracyOf(sentence.reading, typed), body.duration, now(), MAX_TRIALS)
    .first();
  if (!inserted) return error(429, `1 回の参加で送れるのは ${MAX_TRIALS} 文までです。ありがとうございました！`);
  return json({ id: inserted.id }, { status: 201 });
}

// ダッシュボードで、どれだけ集まったかを見る
export async function typingStats(env) {
  const row = await env.DB.prepare(
    "SELECT (SELECT COUNT(*) FROM typing_sessions) AS sessions, COUNT(*) AS trials, COALESCE(SUM(keystrokes), 0) AS keystrokes FROM typing_trials",
  ).first();
  return json(row);
}

// お手本とかけ離れた文を送った参加を、多い順に出す。消すかはダッシュボードで人が決める
export async function flaggedSessions(env) {
  const { results } = await env.DB.prepare(
    "SELECT s.id, s.created_at, COUNT(*) AS flagged, " +
      "(SELECT COUNT(*) FROM typing_trials a WHERE a.session_id = s.id) AS trials, " +
      "(SELECT json_group_array(keys) FROM (SELECT keys FROM typing_trials b WHERE b.session_id = s.id AND b.accuracy < ?1 ORDER BY b.id LIMIT 5)) AS samples " +
      "FROM typing_trials t JOIN typing_sessions s ON s.id = t.session_id WHERE t.accuracy < ?1 " +
      "GROUP BY s.id ORDER BY flagged DESC, s.created_at DESC LIMIT 100",
  )
    .bind(FLAG_ACCURACY)
    .all();
  return json({
    threshold: FLAG_ACCURACY,
    // 打った文字は打鍵から組み立てて見せる
    sessions: results.map((row) => ({ ...row, samples: JSON.parse(row.samples).map((keys) => replay(JSON.parse(keys))) })),
  });
}

// お手本とかけ離れた文 (一致率が FLAG_ACCURACY 未満) を、その参加から消す。
// ダッシュボードは Access の内側だが、別のサイトから送らせる書き込みを防ぐため同じオリジンからだけ受け付ける
export async function deleteFlagged(request, env, url, sessionId) {
  if (request.headers.get("origin") !== url.origin) return error(403, "このページからは操作できません");
  const result = await env.DB.prepare("DELETE FROM typing_trials WHERE session_id = ? AND accuracy < ?").bind(sessionId, FLAG_ACCURACY).run();
  return json({ deleted: result.meta.changes });
}

// 研究用に書き出す。after より大きい id から limit 件ずつ返すので、next が null になるまで続けて読む
export async function exportTrials(env, url) {
  const after = Math.max(0, Number(url.searchParams.get("after")) || 0);
  const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get("limit")) || 500));
  const { results } = await env.DB.prepare(
    "SELECT t.id, t.session_id, s.profile, t.sentence_id, t.sentence_text, t.reading, t.keys, t.accuracy, t.duration_ms, t.created_at " +
      "FROM typing_trials t JOIN typing_sessions s ON s.id = t.session_id WHERE t.id > ? ORDER BY t.id LIMIT ?",
  )
    .bind(after, limit + 1)
    .all();
  const more = results.length > limit;
  const trials = results.slice(0, limit).map((row) => ({
    id: row.id,
    session: row.session_id,
    profile: JSON.parse(row.profile),
    sentence: row.sentence_id,
    text: row.sentence_text,
    reading: row.reading,
    keys: JSON.parse(row.keys),
    accuracy: row.accuracy,
    duration_ms: row.duration_ms,
    created_at: row.created_at,
  }));
  return json({ trials, next: more ? trials[trials.length - 1].id : null });
}
