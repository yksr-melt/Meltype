/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング調査 (/typing/) の受付。どのキーが抜けやすいかを調べるため、お手本の文を打ったときの打鍵を集める。
// ページは誰でも遊べ、打鍵は調査に同意した人のものだけを受け付ける。
// 決まり: 受け付けるのはお手本の id と、それを打った打鍵 (keys) だけ (打った文字は打鍵から組み立てる。自由に書いた文章を送る欄は作らない)。
// 決まり: IP はハッシュにして回数制限にだけ使い、調査のデータには入れない (rate_events は 1 日で消える)。
import { TYPING_VERSION, accuracyOf, replay, suspicious } from "../public/assets/js/typing-check.js";
import { CONSENT_VERSION } from "../public/assets/js/typing-consent.js";
import { KINDS, SENTENCES, sentenceById } from "../public/assets/js/typing-sentences.js";
import { error, json, now, randomToken, readJson, sha256Hex } from "./http.js";
import { claimRate, hashIp, verifyTurnstile } from "./reports.js";

// 参加 (同意) を始められる回数。参加に期限は無く、人が始め直すのはブラウザーを変えたときくらいなので、数回で足りる。
// 自動で何度も始め直して書き込み続けるのを止めるための上限
const TYPING_RATE = { kind: "typing", window: 10 * 60, windowLimit: 3, dayLimit: 6, unit: "回", verb: "参加を始められる" };
// 1 つの参加から 1 日に送れる文の数。続けて打っても足りる量にして、1 日の書き込みを抑える
const DAY_TRIALS = 1000;
// 1 文の打鍵の上限は、読み 1 字あたりローマ字 3 打 + Shift・打ち直しの余裕を見て 12 打。
// 文ごとの書き込みの大きさをお手本の長さで抑える (いちばん長い文で 1 文 10 KB ほど)
const KEYS_PER_CHAR = 12;
const MAX_BODY = 32 * 1024;
const MAX_MS = 10 * 60 * 1000;
// かなにしたときのお手本との一致率がこれより低い文を、ダッシュボードで「お手本とかけ離れた送信」として出す。
// 一致率は保存せず、見るたびに打鍵から計算する (読み替えの規則を直しても、古い行だけ古い規則の値で残らないように)。
// 全部を計算し直すと重いので、新しいものから FLAG_SCAN 件だけを見る
export const FLAG_ACCURACY = 0.5;
const FLAG_SCAN = 5000;

export const PROFILE_CHOICES = {
  touch: ["yes", "partly", "no", "unknown"],
  layout: ["jis", "us", "other", "unknown"],
  os: ["windows", "mac", "linux", "chromeos", "other"],
};

// タイピングのデータベースが無いときは受け付けない (wrangler.jsonc の TYPING_DB)
function noDatabase(env) {
  if (env.TYPING_DB) return null;
  console.error("TYPING_DB が設定されていないため、タイピング調査を受け付けていません");
  return error(503, "いまは受け付けられません。時間をおいてもう一度試してください");
}

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
  return env.TYPING_DB.prepare("SELECT id, consent_version FROM typing_sessions WHERE token_hash = ?").bind(await sha256Hex(token)).first();
}

export async function createSession(request, env) {
  const missing = noDatabase(env);
  if (missing) return missing;
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
  await env.TYPING_DB.prepare("INSERT INTO typing_sessions (id, token_hash, profile, consent_version, created_at) VALUES (?, ?, ?, ?, ?)")
    .bind(id, await sha256Hex(token), JSON.stringify(validProfile(body.profile)), CONSENT_VERSION, at)
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
  const missing = noDatabase(env);
  if (missing) return missing;
  const session = await sessionFor(request, env);
  if (!session) return error(401, "参加の番号が見つかりません。ページを読み込み直してください");
  if (session.consent_version !== CONSENT_VERSION) return error(410, "同意の内容が変わりました。もう一度同意してから送ってください");
  const { body, error: invalid } = await readJson(request, MAX_BODY);
  if (invalid) return invalid;
  const sentence = sentenceById(body && body.sentence);
  if (!sentence || sentence.retired) return error(400, "お手本の文が見つかりません");
  const keys = validKeys(body.keys, [...sentence.reading].length * KEYS_PER_CHAR);
  if (!keys) return error(400, "打鍵の記録の形式が正しくありません");
  if (!isInt(body.duration, 1, MAX_MS)) return error(400, "かかった時間の形式が正しくありません");
  // 前の版のページ (Backspace の消し方が違う) から送られた打鍵は、読み直してもらう
  if (body.version !== TYPING_VERSION) return error(400, "ページが新しくなりました。読み込み直してから打ってください");
  const typed = replay(keys, sentence.reading);
  // 名前など、お手本と関係ない入力が続く文は残さない (ページも送る前に同じ判定で止める)
  if (suspicious(sentence.reading, typed)) return error(422, "お手本と関係ない入力が続いたので、この文は受け付けません");

  const at = now();
  const inserted = await env.TYPING_DB.prepare(
    "INSERT INTO typing_trials (session_id, sentence_id, keys, duration_ms, version, created_at) " +
      "SELECT ?1, ?2, ?3, ?4, ?7, ?5 WHERE (SELECT COUNT(*) FROM typing_trials WHERE session_id = ?1 AND created_at > ?5 - 86400) < ?6 RETURNING id",
  )
    .bind(session.id, sentence.id, JSON.stringify(keys), body.duration, at, DAY_TRIALS, TYPING_VERSION)
    .first();
  if (!inserted) return error(429, `送れるのは 1 日に ${DAY_TRIALS} 文までです。ありがとうございました！また明日お願いします`);
  return json({ id: inserted.id }, { status: 201 });
}

// どれだけ集まったかを、お手本の文ごとに打たれた数で見る (ダッシュボード)
export async function sentenceCounts(env) {
  const missing = noDatabase(env);
  if (missing) return missing;
  const { results } = await env.TYPING_DB.prepare("SELECT sentence_id, COUNT(*) AS count FROM typing_trials GROUP BY sentence_id").all();
  const counts = new Map(results.map((row) => [row.sentence_id, row.count]));
  return json({
    kinds: KINDS,
    sentences: SENTENCES.map((sentence) => ({ ...sentence, count: counts.get(sentence.id) || 0 })),
  });
}

// 一致率が FLAG_ACCURACY 未満の文 (新しいものから FLAG_SCAN 件のうち)。sessionId を渡すと、その参加の文だけを見る
async function flaggedTrials(env, sessionId = null) {
  const { results } = await env.TYPING_DB.prepare(
    "SELECT id, session_id, sentence_id, keys, version, created_at FROM typing_trials WHERE ?1 IS NULL OR session_id = ?1 ORDER BY id DESC LIMIT ?2",
  )
    .bind(sessionId, FLAG_SCAN)
    .all();
  return results.flatMap((row) => {
    const sentence = sentenceById(row.sentence_id);
    if (!sentence) return [];
    const typed = replay(JSON.parse(row.keys), sentence.reading, row.version);
    return accuracyOf(sentence.reading, typed) < FLAG_ACCURACY ? [{ ...row, typed }] : [];
  });
}

// お手本とかけ離れた文を送った参加を、多い順に出す。消すかはダッシュボードで人が決める
export async function flaggedSessions(env) {
  const missing = noDatabase(env);
  if (missing) return missing;
  const bySession = new Map();
  for (const trial of await flaggedTrials(env)) {
    const entry = bySession.get(trial.session_id) || { id: trial.session_id, flagged: 0, samples: [], last: 0 };
    entry.flagged += 1;
    entry.last = Math.max(entry.last, trial.created_at);
    if (entry.samples.length < 5) entry.samples.push(trial.typed);
    bySession.set(trial.session_id, entry);
  }
  const sessions = [...bySession.values()].sort((a, b) => b.flagged - a.flagged || b.last - a.last).slice(0, 100);
  return json({ threshold: FLAG_ACCURACY, scanned: FLAG_SCAN, sessions });
}

// お手本とかけ離れた文 (一致率が FLAG_ACCURACY 未満) を、その参加から消す。
// ダッシュボードは Access の内側だが、別のサイトから送らせる書き込みを防ぐため同じオリジンからだけ受け付ける
export async function deleteFlagged(request, env, url, sessionId) {
  const missing = noDatabase(env);
  if (missing) return missing;
  if (request.headers.get("origin") !== url.origin) return error(403, "このページからは操作できません");
  const ids = (await flaggedTrials(env, sessionId)).map((trial) => trial.id);
  // D1 は 1 つの文に渡せる値が 100 個までなので、分けて消す
  const chunks = [];
  for (let i = 0; i < ids.length; i += 100) chunks.push(ids.slice(i, i + 100));
  if (chunks.length) await env.TYPING_DB.batch(chunks.map((chunk) => env.TYPING_DB.prepare(`DELETE FROM typing_trials WHERE id IN (${chunk.map(() => "?").join(",")})`).bind(...chunk)));
  return json({ deleted: ids.length });
}

// 研究用に書き出す。after より大きい id から limit 件ずつ返すので、next が null になるまで続けて読む
export async function exportTrials(env, url) {
  const missing = noDatabase(env);
  if (missing) return missing;
  const after = Math.max(0, Number(url.searchParams.get("after")) || 0);
  const limit = Math.min(1000, Math.max(1, Number(url.searchParams.get("limit")) || 500));
  const { results } = await env.TYPING_DB.prepare(
    "SELECT t.id, t.session_id, s.profile, t.sentence_id, t.keys, t.duration_ms, t.version, t.created_at " +
      "FROM typing_trials t JOIN typing_sessions s ON s.id = t.session_id WHERE t.id > ? ORDER BY t.id LIMIT ?",
  )
    .bind(after, limit + 1)
    .all();
  const more = results.length > limit;
  // お手本の文と読みは、書き出すときに id から引いて付ける (使いやすいように)
  const trials = results.slice(0, limit).map((row) => {
    const sentence = sentenceById(row.sentence_id);
    return {
      id: row.id,
      session: row.session_id,
      profile: JSON.parse(row.profile),
      sentence: row.sentence_id,
      text: sentence ? sentence.text : null,
      reading: sentence ? sentence.reading : null,
      keys: JSON.parse(row.keys),
      duration_ms: row.duration_ms,
      // 打った文字を組み立てるときの決まりの版 (public/assets/js/typing-check.js の TYPING_VERSION)
      version: row.version,
      created_at: row.created_at,
    };
  });
  return json({ trials, next: more ? trials[trials.length - 1].id : null });
}
