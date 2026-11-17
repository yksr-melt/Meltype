/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

import { detectOs, escapeHtml, getJson, icon, initHeader } from "./common.js";
import { align, finalKana, toKana } from "./romaji.js";
import { SENTENCES } from "./typing-sentences.js";

const SESSION_KEY = "meltype-typing-session";
// サーバーが打鍵を受け付ける時間 (src/typing.js の SESSION_HOURS) より少し短くして、送る途中で切れないようにする
const SESSION_MS = 11 * 60 * 60 * 1000;
const ROUND = 10;

const root = document.querySelector("[data-typing]");
const $ = (name) => root.querySelector(`[data-${name}]`);
const intro = $("intro");
const play = $("play");
const result = $("result");
const board = $("board");

// localStorage は使えない環境 (プライベートモードなど) がある。そのときは開いているあいだだけ覚える
const storage = {
  get() {
    try {
      const saved = JSON.parse(localStorage.getItem(SESSION_KEY) || "null");
      return saved && typeof saved.token === "string" ? saved : null;
    } catch {
      return null;
    }
  },
  set(value) {
    try {
      if (value) localStorage.setItem(SESSION_KEY, JSON.stringify(value));
      else localStorage.removeItem(SESSION_KEY);
    } catch {
      /* 覚えられなくても参加はできる */
    }
  },
};

const state = {
  session: storage.get(),
  queue: [],
  index: 0,
  results: [],
  trial: null,
  pending: Promise.resolve(),
  failed: 0,
};

function setStatus(element, kind, message) {
  const name = kind === "danger" || kind === "warn" ? "alert" : kind === "ok" ? "check" : "info";
  // callout に is-ok は無いので、うまくいったときも is-info で出す
  element.innerHTML = message ? `<p class="callout is-${kind === "ok" ? "info" : kind}">${icon(name)}<span>${escapeHtml(message)}</span></p>` : "";
}

function show(section) {
  for (const element of [intro, play, result]) element.hidden = element !== section;
}

function shuffle(items) {
  const array = [...items];
  for (let i = array.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    [array[i], array[j]] = [array[j], array[i]];
  }
  return array;
}

// まだ打っていない文を優先して出す (同じ人が何度も同じ文を打つと、文ごとの比べ方が偏るため)
function nextRound() {
  const done = new Set(state.session.done || []);
  const fresh = shuffle(SENTENCES.filter((s) => !done.has(s.id)));
  const rest = shuffle(SENTENCES.filter((s) => done.has(s.id)));
  return [...fresh, ...rest].slice(0, ROUND);
}

function sessionAlive(session) {
  return session && Date.now() - session.startedAt < SESSION_MS;
}

// ---- 1. 同意 ----

function turnstileToken() {
  const input = intro.querySelector('[name="cf-turnstile-response"]');
  return input ? input.value : "";
}

$("consent").addEventListener("change", (event) => {
  $("start").disabled = !event.target.checked;
});

intro.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!$("consent").checked) return;
  // 続けて参加するときは同意を聞き直さず、同じ参加の番号で打つ
  if (sessionAlive(state.session)) {
    startRound();
    return;
  }
  const status = $("intro-status");
  const button = $("start");
  button.disabled = true;
  setStatus(status, "info", "準備しています");
  const os = detectOs();
  try {
    const created = await getJson("/api/typing/sessions", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        consent: true,
        turnstile: turnstileToken(),
        profile: {
          touch: intro.elements.namedItem("touch").value,
          layout: intro.elements.namedItem("layout").value,
          method: intro.elements.namedItem("method").value,
          os: ["windows", "mac", "linux"].includes(os) ? os : /CrOS/.test(navigator.userAgent) ? "chromeos" : "other",
        },
      }),
    });
    // 時間がたって参加し直すときも、打った文の記録は引き継ぐ
    state.session = { id: created.id, token: created.token, startedAt: Date.now(), done: (state.session && state.session.done) || [] };
    storage.set(state.session);
    setStatus(status, "", "");
    startRound();
  } catch (e) {
    button.disabled = false;
    if (window.turnstile) window.turnstile.reset();
    setStatus(status, "danger", e.message);
  }
});

// ---- 2. 打つ ----

function newTrial() {
  return { typed: "", keys: [], open: new Map(), startedAt: 0, backspaces: 0, ime: false };
}

function startRound() {
  state.queue = nextRound();
  state.index = 0;
  state.results = [];
  $("total").textContent = String(state.queue.length);
  show(play);
  $("forget").hidden = false;
  loadSentence();
  board.focus();
}

function loadSentence() {
  const sentence = state.queue[state.index];
  state.trial = newTrial();
  $("count").textContent = String(state.index + 1);
  $("text").textContent = sentence.text;
  $("reading").textContent = sentence.reading;
  $("ime-note").hidden = true;
  render();
}

function render() {
  const { kana, pending } = toKana(state.trial.typed);
  $("kana").textContent = kana;
  $("pending").textContent = pending;
  $("romaji").textContent = state.trial.typed.toLowerCase();
}

function elapsed(event) {
  return Math.max(0, Math.round(event.timeStamp - state.trial.startedAt));
}

function onKeyDown(event) {
  if (play.hidden) return;
  const trial = state.trial;
  // IME がオンのときは打ったキーが分からない (Process・229 になる) ので、この文は記録しない
  if (event.isComposing || event.key === "Process" || event.keyCode === 229) {
    event.preventDefault();
    trial.ime = true;
    $("ime-note").hidden = false;
    return;
  }
  if (event.ctrlKey || event.metaKey || event.altKey) return;
  const printable = event.key.length === 1 && event.key >= " " && event.key <= "~";
  if (!printable && !["Backspace", "Enter", "Shift"].includes(event.key)) return;
  event.preventDefault();
  if (event.key === "Enter") {
    if (!event.repeat) finishSentence();
    return;
  }
  if (trial.ime) {
    // IME をオフにして打ち直したら、そこから記録し直す
    state.trial = newTrial();
    $("ime-note").hidden = true;
    return onKeyDown(event);
  }
  if (!trial.startedAt) trial.startedAt = event.timeStamp;
  // 押しっぱなしの繰り返しは打鍵として数えないが、Backspace の繰り返しは文字を消すので反映する
  if (!event.repeat) {
    trial.open.set(event.code, trial.keys.length);
    trial.keys.push([event.code, event.key, elapsed(event), null]);
  } else if (event.key !== "Backspace") {
    return;
  }
  if (event.key === "Backspace") {
    trial.typed = trial.typed.slice(0, -1);
    trial.backspaces += 1;
  } else if (printable) {
    trial.typed += event.key;
  }
  render();
}

function onKeyUp(event) {
  if (play.hidden) return;
  const trial = state.trial;
  const index = trial.open.get(event.code);
  if (index === undefined) return;
  trial.open.delete(event.code);
  trial.keys[index][3] = Math.max(trial.keys[index][2], elapsed(event));
}

board.addEventListener("keydown", onKeyDown);
board.addEventListener("keyup", onKeyUp);
board.addEventListener("click", () => board.focus());
board.addEventListener("focus", () => ($("focus-hint").hidden = true));
board.addEventListener("blur", () => ($("focus-hint").hidden = false));

function send(trial, sentence, duration) {
  const body = JSON.stringify({ sentence: sentence.id, typed: trial.typed, keys: trial.keys, duration });
  const token = state.session.token;
  // 前の文を送り終えてから次を送る (順番が入れ替わらないように)。失敗したら 1 回だけ送り直す
  state.pending = state.pending.then(async () => {
    for (let attempt = 0; attempt < 2; attempt += 1) {
      try {
        await getJson("/api/typing/trials", { method: "POST", headers: { "content-type": "application/json", authorization: `Bearer ${token}` }, body });
        return;
      } catch (e) {
        if (attempt === 1 || (e.status && e.status < 500)) {
          state.failed += 1;
          console.error("打鍵を送れませんでした", e);
          return;
        }
      }
    }
  });
}

function finishSentence() {
  const trial = state.trial;
  const sentence = state.queue[state.index];
  if (trial.ime || !trial.typed) return;
  const duration = Math.max(1, Math.round(performance.now() - (trial.startedAt || performance.now())));
  for (const index of trial.open.values()) trial.keys[index][3] = null;
  send(trial, sentence, duration);
  state.results.push({ sentence, typed: finalKana(trial.typed), duration, backspaces: trial.backspaces });
  state.session.done = [...new Set([...(state.session.done || []), sentence.id])];
  storage.set(state.session);
  next();
}

function next() {
  state.index += 1;
  if (state.index < state.queue.length) loadSentence();
  else showResult();
}

$("skip").addEventListener("click", () => {
  next();
  board.focus();
});

// ---- 3. 結果 ----

async function showResult() {
  show(result);
  result.focus();
  let chars = 0;
  let wrong = 0;
  let ms = 0;
  let backspaces = 0;
  const misses = new Map();
  for (const entry of state.results) {
    const ops = align(entry.sentence.reading, entry.typed);
    chars += [...entry.sentence.reading].length;
    wrong += ops.filter((op) => op.op !== "same").length;
    ms += entry.duration;
    backspaces += entry.backspaces;
    for (const op of ops) if (op.op === "omit" || op.op === "sub") misses.set(op.want, (misses.get(op.want) || 0) + 1);
  }
  $("speed").textContent = ms ? String(Math.round((chars / ms) * 60000)) : "-";
  $("accuracy").textContent = chars ? String(Math.max(0, Math.round((1 - wrong / chars) * 1000) / 10)) : "-";
  $("backspaces").textContent = String(backspaces);
  const top = [...misses].sort((a, b) => b[1] - a[1]).slice(0, 8);
  $("misses").innerHTML = top.length
    ? `<h3>抜けた・打ち間違えた字</h3><ul>${top.map(([kana, n]) => `<li><b>${escapeHtml(kana)}</b><span>${n} 回</span></li>`).join("")}</ul>`
    : state.results.length
      ? "<p>打ち終わった文には間違いがありませんでした！</p>"
      : "";
  const status = $("result-status");
  setStatus(status, "info", "送っています");
  await state.pending;
  if (state.failed) setStatus(status, "warn", `${state.failed} 文を送れませんでした。通信を確かめてから、もう一度打ってもらえると助かります`);
  else setStatus(status, "ok", "打った記録を送りました。");
  state.failed = 0;
}

$("again").addEventListener("click", () => {
  if (sessionAlive(state.session)) startRound();
  else {
    // 時間がたった参加の番号は使えないので、同意からやり直す (打った文の記録は引き継ぐ)
    show(intro);
    $("start").lastChild.textContent = "はじめる";
    if (window.turnstile) window.turnstile.reset();
  }
});

// ---- データを消す ----

$("forget").addEventListener("click", async () => {
  if (!state.session) return;
  if (!confirm("このブラウザーから送ったタイピングのデータを全部消します。よろしいですか？")) return;
  try {
    await getJson("/api/typing/sessions/me", { method: "DELETE", headers: { authorization: `Bearer ${state.session.token}` } });
  } catch (e) {
    if (e.status !== 404) {
      alert(e.message);
      return;
    }
  }
  state.session = null;
  storage.set(null);
  $("forget").hidden = true;
  show(intro);
  alert("消しました。");
});

// ---- はじめ ----

initHeader();
if (detectOs() === "mobile") $("mobile-note").hidden = false;
if (state.session) $("forget").hidden = false;
if (sessionAlive(state.session)) {
  $("consent").checked = true;
  $("start").disabled = false;
  $("start").lastChild.textContent = "続きから打つ";
}
