/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング (/typing/)。誰でも遊べ、打った記録は調査に同意した人だけが送る。
// 決まり: 同意するまでは打鍵をどこにも送らない (打った記録はこのページのメモリーにだけ置く)。
// 決まり: 送るのは打鍵だけ。画面に出す打った文字も、打鍵から組み立てる (typing-check.js の replay。Backspace は Meltype と同じく日本語なら 1 音消す)。
import { detectOs, escapeHtml, getJson, icon, initHeader } from "./common.js";
import { align, toRomaji } from "./romaji.js";
import { TYPING_VERSION, compare, previewKana, replay, suspicious } from "./typing-check.js";
import { CONSENT_VERSION } from "./typing-consent.js";
import { KINDS, SENTENCES } from "./typing-sentences.js";

const STORAGE_KEY = "meltype-typing";
const ROUND = 10;
// 全部の種類の文を混ぜて出す選択肢。種類の id (sentences/ のファイル名の [a-z]+) と重ならない値にしている
const ALL_KINDS = "*";

const root = document.querySelector("[data-typing]");
const $ = (name) => root.querySelector(`[data-${name}]`);
const intro = $("intro");
const play = $("play");
const result = $("result");
const board = $("board");
const sendForm = $("send");

// 覚えること: いま使える参加 (session)、打った文の id (done)、続けて送るか (auto)、文の種類 (kind)。
// localStorage が使えない環境 (プライベートモードなど) では、開いているあいだだけ覚える
function load() {
  try {
    const saved = JSON.parse(localStorage.getItem(STORAGE_KEY) || "null");
    if (saved && Array.isArray(saved.done)) return saved;
  } catch {
    /* 読めなければ初めから */
  }
  return { session: null, done: [], auto: true, kind: ALL_KINDS };
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
  const pool = SENTENCES.filter((s) => (saved.kind === ALL_KINDS || s.kind === saved.kind) && !s.retired);
  return [...shuffle(pool.filter((s) => !done.has(s.id))), ...shuffle(pool.filter((s) => done.has(s.id)))].slice(0, ROUND);
}

// 同意した参加は、同意文が変わるまで使い続ける (同じ人の打ち方をまとめて見られるように)
const sessionAlive = () => Boolean(saved.session && saved.session.consent === CONSENT_VERSION);
const hasEnglish = (sentence) => /[a-z]/.test(sentence.reading);

// ---- 1. はじめる ----

const kindOptions = [{ id: ALL_KINDS, label: "シャッフル (全部の種類)" }, ...KINDS];
$("kind").innerHTML = kindOptions.map((kind) => `<option value="${kind.id}">${escapeHtml(kind.label)}</option>`).join("");
$("kind").value = kindOptions.some((kind) => kind.id === saved.kind) ? saved.kind : ALL_KINDS;

intro.addEventListener("submit", (event) => {
  event.preventDefault();
  saved.kind = $("kind").value;
  save();
  startRound();
});

// ---- 2. 打つ ----

function newTrial() {
  return { keys: [], open: new Map(), startedAt: null, ime: false };
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
  $("reading-romaji").textContent = toRomaji(sentence.reading);
  $("ime-note").hidden = true;
  render();
}

function render() {
  const sentence = state.queue[state.index];
  const typed = replay(state.trial.keys, sentence.reading);
  const { kana, pending } = previewKana(typed, sentence.reading);
  $("kana").textContent = kana;
  $("pending").textContent = pending;
  $("romaji").textContent = typed.toLowerCase();
}

// 時刻は performance.now() で測る。event.timeStamp は、ブラウザーや設定 (指紋対策など) によって 0 や粗い値になり、打鍵の間隔が取れないため
function elapsed() {
  return Math.max(0, Math.round(performance.now() - state.trial.startedAt));
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
  // 押しっぱなしの繰り返しは、字を消す Backspace だけ打鍵として残す (どこをどう直したかを打鍵から再現できるように)。
  // 繰り返しの打鍵は離した時刻が無いので null のまま
  if (event.repeat && event.key !== "Backspace") return;
  trial.startedAt ??= performance.now();
  if (!event.repeat) trial.open.set(event.code, trial.keys.length);
  trial.keys.push([event.code, event.key, elapsed(), null]);
  render();
}

function onKeyUp(event) {
  if (play.hidden) return;
  const trial = state.trial;
  const index = trial.open.get(event.code);
  if (index === undefined) return;
  trial.open.delete(event.code);
  trial.keys[index][3] = Math.max(trial.keys[index][2], elapsed());
}

board.addEventListener("keydown", onKeyDown);
board.addEventListener("keyup", onKeyUp);
board.addEventListener("click", () => board.focus());
board.addEventListener("focus", () => ($("focus-hint").hidden = true));
board.addEventListener("blur", () => ($("focus-hint").hidden = false));

function finishSentence() {
  const trial = state.trial;
  const sentence = state.queue[state.index];
  if (trial.ime || !replay(trial.keys, sentence.reading)) return;
  const duration = Math.max(1, Math.round(performance.now() - trial.startedAt));
  state.round.push({ sentence, keys: trial.keys, duration });
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
    const typed = replay(entry.keys, entry.sentence.reading);
    const ops = compare(entry.sentence.reading, typed);
    chars += ops.filter((op) => op.want).length;
    wrong += ops.filter((op) => op.op !== "same").length;
    ms += entry.duration;
    backspaces += entry.keys.filter(([, key]) => key === "Backspace").length;
    // 抜けた・打ち間違えた字は、英語が混ざった文ならお手本のローマ字の字で数える (英語の部分をかなにすると読めないため)
    const shown = hasEnglish(entry.sentence) ? align(toRomaji(entry.sentence.reading), typed.toLowerCase()) : ops;
    for (const op of shown) if (op.op === "omit" || op.op === "sub") misses.set(op.want, (misses.get(op.want) || 0) + 1);
  }
  $("speed").textContent = ms ? String(Math.round((chars / ms) * 60000)) : "-";
  $("accuracy").textContent = chars ? String(Math.max(0, Math.round((1 - wrong / chars) * 1000) / 10)) : "-";
  $("backspaces").textContent = String(backspaces);
  const top = [...misses].sort((a, b) => b[1] - a[1]).slice(0, 8);
  $("misses").innerHTML = top.length
    ? `<h3>抜けた・打ち間違えた字</h3><ul>${top.map(([char, n]) => `<li><b>${escapeHtml(char)}</b><span>${n} 回</span></li>`).join("")}</ul>`
    : state.round.length
      ? "<p>打ち終わった文には間違いがありませんでした！</p>"
      : "";
}

// Turnstile は同意の欄を出すときに読み込む。ページと一緒に読むと、読み終わる前に欄を出したとき確認の枠が出ないため。
// 読み込めないとき (広告ブロッカーなど) は、送れない理由を出す
const TURNSTILE_ERROR = "人かどうかの確認 (Turnstile) を読み込めませんでした。広告ブロッカーなどを止めてから、ページを読み込み直してください";
let turnstileLoading = null;
let turnstileWidget = null;

function loadTurnstile() {
  turnstileLoading ??= new Promise((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";
    script.async = true;
    script.onload = () => resolve(window.turnstile);
    script.onerror = () => {
      turnstileLoading = null;
      script.remove();
      reject(new Error(TURNSTILE_ERROR));
    };
    document.head.append(script);
  });
  return turnstileLoading;
}

async function showSendForm() {
  sendForm.hidden = false;
  $("auto-wrap").hidden = true;
  $("consent").checked = false;
  $("send-button").disabled = true;
  const element = $("turnstile");
  try {
    const turnstile = await loadTurnstile();
    if (turnstileWidget === null) turnstileWidget = turnstile.render(element, { sitekey: element.dataset.sitekey, action: "typing" });
    else turnstile.reset(turnstileWidget);
  } catch (e) {
    setStatus("danger", e.message);
  }
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
    // 同意したあとは、もう一度聞かない。続けて送るのをやめたら、その回は送らない
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
  if (turnstileWidget === null) {
    setStatus("danger", TURNSTILE_ERROR);
    return;
  }
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
        turnstile: window.turnstile.getResponse(turnstileWidget),
        profile: {
          touch: field("touch"),
          layout: field("layout"),
          os: ["windows", "mac", "linux"].includes(os) ? os : /CrOS/.test(navigator.userAgent) ? "chromeos" : "other",
        },
      }),
    });
    saved.session = { id: created.id, token: created.token, consent: CONSENT_VERSION };
    saved.auto = true;
    save();
    sendForm.hidden = true;
    $("auto-wrap").hidden = false;
    $("auto").checked = true;
    await sendRound();
  } catch (e) {
    $("send-button").disabled = false;
    window.turnstile.reset(turnstileWidget);
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
  // お手本と関係ない入力が続いた文は送らない (サーバーも同じ判定で受け付けない)
  const sendable = round.filter((entry) => !suspicious(entry.sentence.reading, replay(entry.keys, entry.sentence.reading)));
  const skipped = round.length - sendable.length;
  for (const entry of sendable) {
    const body = JSON.stringify({ sentence: entry.sentence.id, keys: entry.keys, duration: entry.duration, version: TYPING_VERSION });
    try {
      await getJson("/api/typing/trials", { method: "POST", headers: { "content-type": "application/json", authorization: `Bearer ${token}` }, body });
      sent += 1;
    } catch (e) {
      round.sent = false;
      if (e.status === 410) {
        // 同意文が変わった。同意からやり直してもらう
        saved.session = null;
        save();
        showSendForm();
      }
      setStatus(sent ? "warn" : "danger", sent ? `${sent} 文を送ったところで止まりました: ${e.message}` : e.message);
      return;
    }
  }
  const note = skipped ? ` お手本と関係ない入力が続いた ${skipped} 文は送りませんでした。` : "";
  setStatus("ok", `${sent} 文の記録を送りました。ありがとうございます！${note}`);
}

$("again").addEventListener("click", startRound);
$("choose").addEventListener("click", () => {
  show(intro);
  $("kind").focus();
});

// ---- はじめ ----

initHeader();
if (detectOs() === "mobile") $("mobile-note").hidden = false;
