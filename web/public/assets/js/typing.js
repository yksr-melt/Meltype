/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング (/typing/)。誰でも遊べ、打った記録は調査に同意した人だけが送る。
// 決まり: 同意するまでは打鍵をどこにも送らない (打った記録はこのページのメモリーにだけ置く)。
import { detectOs, escapeHtml, getJson, icon, initHeader } from "./common.js";
import { align, finalKana, toKana } from "./romaji.js";
import { SENTENCES } from "./typing-sentences.js";

const STORAGE_KEY = "meltype-typing";
// サーバーが打鍵を受け付ける時間 (src/typing.js の SESSION_HOURS) より少し短くして、送る途中で切れないようにする
const SESSION_MS = 11 * 60 * 60 * 1000;
const ROUND = 10;

const root = document.querySelector("[data-typing]");
const $ = (name) => root.querySelector(`[data-${name}]`);
const intro = $("intro");
const play = $("play");
const result = $("result");
const board = $("board");
const sendForm = $("send");

// 覚えること: いま使える参加 (session)、このブラウザーで同意したすべての参加の番号 (tokens: データを消すため)、
// 打った文の id (done)、続けて送るか (auto)。
// localStorage が使えない環境 (プライベートモードなど) では、開いているあいだだけ覚える
function load() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || "null");
    if (saved && Array.isArray(saved.tokens) && Array.isArray(saved.done)) return saved;
  } catch {
    /* 読めなければ初めから */
  }
  return { session: null, tokens: [], done: [], auto: true };
}

const saved = load();

function save() {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
  } catch {
    /* 覚えられなくても遊べる */
  }
}

const state = { queue: [], index: 0, round: [], trial: null };

function setStatus(kind, message) {
  const name = kind === "danger" || kind === "warn" ? "alert" : kind === "ok" ? "check" : "info";
  // callout に is-ok は無いので、うまくいったときも is-info で出す
  $("result-status").innerHTML = message ? `<p class="callout is-${kind === "ok" ? "info" : kind}">${icon(name)}<span>${escapeHtml(message)}</span></p>` : "";
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
  const done = new Set(saved.done);
  return [...shuffle(SENTENCES.filter((s) => !done.has(s.id))), ...shuffle(SENTENCES.filter((s) => done.has(s.id)))].slice(0, ROUND);
}

const sessionAlive = () => Boolean(saved.session && Date.now() - saved.session.startedAt < SESSION_MS);

// ---- 1. はじめる ----

intro.addEventListener("submit", (event) => {
  event.preventDefault();
  startRound();
});

// ---- 2. 打つ ----

function newTrial() {
  return { typed: "", keys: [], open: new Map(), startedAt: 0, backspaces: 0, ime: false };
}

function startRound() {
  state.queue = nextRound();
  state.index = 0;
  state.round = [];
  $("total").textContent = String(state.queue.length);
  show(play);
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
    onKeyDown(event);
    return;
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

function finishSentence() {
  const trial = state.trial;
  const sentence = state.queue[state.index];
  if (trial.ime || !trial.typed) return;
  const duration = Math.max(1, Math.round(performance.now() - trial.startedAt));
  state.round.push({ sentence, typed: trial.typed, keys: trial.keys, duration, backspaces: trial.backspaces });
  saved.done = [...new Set([...saved.done, sentence.id])];
  save();
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

function showScores() {
  let chars = 0;
  let wrong = 0;
  let ms = 0;
  let backspaces = 0;
  const misses = new Map();
  for (const entry of state.round) {
    const ops = align(entry.sentence.reading, finalKana(entry.typed));
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
    : state.round.length
      ? "<p>打ち終わった文には間違いがありませんでした！</p>"
      : "";
}

let turnstileWidget = null;

function showSendForm() {
  sendForm.hidden = false;
  $("auto-wrap").hidden = true;
  $("consent").checked = false;
  $("send-button").disabled = true;
  const element = $("turnstile");
  if (!window.turnstile) return;
  if (turnstileWidget === null) turnstileWidget = window.turnstile.render(element, { sitekey: element.dataset.sitekey, action: "typing" });
  else window.turnstile.reset(turnstileWidget);
}

function showResult() {
  show(result);
  result.focus();
  showScores();
  setStatus("", "");
  if (!state.round.length) {
    sendForm.hidden = true;
    $("auto-wrap").hidden = true;
    return;
  }
  if (sessionAlive()) {
    // 同意した参加の時間のうちは、もう一度聞かない。続けて送るのをやめたら、その回は送らない
    sendForm.hidden = true;
    $("auto-wrap").hidden = false;
    $("auto").checked = saved.auto;
    if (saved.auto) sendRound();
  } else {
    showSendForm();
  }
}

$("auto").addEventListener("change", (event) => {
  saved.auto = event.target.checked;
  save();
  if (saved.auto && state.round.length && !state.round.sent) sendRound();
});

$("consent").addEventListener("change", (event) => {
  $("send-button").disabled = !event.target.checked;
});

sendForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!$("consent").checked) return;
  $("send-button").disabled = true;
  setStatus("info", "準備しています");
  const os = detectOs();
  const field = (name) => intro.elements.namedItem(name).value;
  try {
    const created = await getJson("/api/typing/sessions", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        consent: true,
        turnstile: window.turnstile && turnstileWidget !== null ? window.turnstile.getResponse(turnstileWidget) : "",
        profile: {
          touch: field("touch"),
          layout: field("layout"),
          method: field("method"),
          os: ["windows", "mac", "linux"].includes(os) ? os : /CrOS/.test(navigator.userAgent) ? "chromeos" : "other",
        },
      }),
    });
    saved.session = { id: created.id, token: created.token, startedAt: Date.now() };
    saved.tokens = [...saved.tokens, created.token];
    saved.auto = true;
    save();
    $("forget").hidden = false;
    sendForm.hidden = true;
    $("auto-wrap").hidden = false;
    $("auto").checked = true;
    await sendRound();
  } catch (e) {
    $("send-button").disabled = false;
    if (window.turnstile && turnstileWidget !== null) window.turnstile.reset(turnstileWidget);
    setStatus("danger", e.message);
  }
});

// この回の文を順に送る。途中で失敗したら、そこで止めて知らせる
async function sendRound() {
  const round = state.round;
  const token = saved.session.token;
  round.sent = true;
  setStatus("info", "送っています");
  let sent = 0;
  for (const entry of round) {
    const body = JSON.stringify({ sentence: entry.sentence.id, typed: entry.typed, keys: entry.keys, duration: entry.duration });
    try {
      await getJson("/api/typing/trials", { method: "POST", headers: { "content-type": "application/json", authorization: `Bearer ${token}` }, body });
      sent += 1;
    } catch (e) {
      round.sent = false;
      if (e.status === 410) {
        // 参加の時間が切れた。同意からやり直してもらう
        saved.session = null;
        save();
        showSendForm();
        $("auto-wrap").hidden = true;
      }
      setStatus(sent ? "warn" : "danger", sent ? `${sent} 文を送ったところで止まりました: ${e.message}` : e.message);
      return;
    }
  }
  setStatus("ok", `${sent} 文の記録を送りました。ありがとうございます！`);
}

$("again").addEventListener("click", startRound);

// ---- データを消す ----

$("forget").addEventListener("click", async () => {
  if (!saved.tokens.length) return;
  if (!confirm("このブラウザーから送ったタイピングの記録を全部消します。よろしいですか？")) return;
  const left = [];
  for (const token of saved.tokens) {
    try {
      await getJson("/api/typing/sessions/me", { method: "DELETE", headers: { authorization: `Bearer ${token}` } });
    } catch (e) {
      if (e.status !== 404) left.push(token);
    }
  }
  saved.tokens = left;
  if (!left.includes(saved.session && saved.session.token)) saved.session = null;
  save();
  $("forget").hidden = left.length === 0;
  alert(left.length ? "消せなかった記録があります。時間をおいてもう一度試してください。" : "消しました。");
});

// ---- はじめ ----

initHeader();
if (detectOs() === "mobile") $("mobile-note").hidden = false;
$("forget").hidden = saved.tokens.length === 0;
