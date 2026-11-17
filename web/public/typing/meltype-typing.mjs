#!/usr/bin/env node
/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// Meltype のタイピングをターミナルで打つ (https://meltype.moe/typing/)。Node.js 18 以降で、ほかに要るものはない。
//   node meltype-typing.mjs                 番号を聞いてから始める (空なら送らずに遊ぶ)
//   node meltype-typing.mjs --token 番号     番号を渡して始める (環境変数 MELTYPE_TYPING_TOKEN でもよい)
//   node meltype-typing.mjs --no-send       送らずに遊ぶ
//   node meltype-typing.mjs --count 20      打つ文の数 (既定は 10)
//   node meltype-typing.mjs --kind chat     文の種類 (plain: ふつう・formal: 硬め・chat: ネットの会話。既定は plain)
//   node meltype-typing.mjs --site URL      別のサイトにつなぐ (開発用)
// 番号は、ブラウザーのページで調査への同意をしたあとに出る「ターミナルで打つ」の番号。同意していない人は送れない。
// 決まり: お手本とローマ字の読み替えはサイトから読み込む (ページと同じものを使い、このファイルを配り直さずに済むように)。
// ターミナルではローマ字打ち (QWERTY) だけ。押した文字しか分からないので、キーを離した時刻は null、Shift は記録しない (送るときに client: "cli" を付けて分ける)。
import { createInterface } from "node:readline/promises";
import { performance } from "node:perf_hooks";

const args = process.argv.slice(2);
const option = (name) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : undefined;
};
const site = (option("--site") || "https://meltype.moe").replace(/\/+$/, "");
const count = Math.max(1, Math.min(60, Number(option("--count")) || 10));
const noSend = args.includes("--no-send");
const kind = option("--kind") || "plain";

const out = process.stdout;
const dim = (text) => `\x1b[2m${text}\x1b[0m`;
const bold = (text) => `\x1b[1m${text}\x1b[0m`;
const sky = (text) => `\x1b[36m${text}\x1b[0m`;
const red = (text) => `\x1b[31m${text}\x1b[0m`;

function die(message) {
  out.write(`${red(message)}\n`);
  process.exit(1);
}

if (!process.stdin.isTTY) die("ターミナルで開いてください (キーを 1 つずつ読むため、パイプやリダイレクトでは動きません)");

// サイトのモジュールを読む。data: の URL には相対パスが使えないので、"./x.js" の import も読んで data: の URL に置き換える
const sources = new Map();
async function source(path) {
  if (!sources.has(path)) {
    sources.set(
      path,
      (async () => {
        const response = await fetch(`${site}${path}`);
        if (!response.ok) throw new Error(`${path} を読めません (${response.status})`);
        let text = await response.text();
        const dir = path.slice(0, path.lastIndexOf("/") + 1);
        for (const [, dep] of text.matchAll(/from "\.\/([\w.-]+\.js)"/g)) text = text.replaceAll(`"./${dep}"`, JSON.stringify(await source(dir + dep)));
        return `data:text/javascript;charset=utf-8,${encodeURIComponent(text)}`;
      })(),
    );
  }
  return sources.get(path);
}
const load = async (path) => import(await source(path));

let SENTENCES, KINDS, toKana, finalKana, align, suspicious;
try {
  ({ SENTENCES, KINDS } = await load("/assets/js/typing-sentences.js"));
  ({ toKana, finalKana, align } = await load("/assets/js/romaji.js"));
  ({ suspicious } = await load("/assets/js/typing-check.js"));
} catch (e) {
  die(`サイトにつながりません: ${e.message}`);
}

let token = noSend ? "" : option("--token") || process.env.MELTYPE_TYPING_TOKEN || "";
if (!noSend && !token) {
  out.write(`${dim("記録を調査に送るには、ブラウザーの " + site + "/typing/ で同意したあとに出る番号を貼ってください。")}\n`);
  const rl = createInterface({ input: process.stdin, output: out });
  token = (await rl.question("番号 (空なら送らずに遊ぶ): ")).trim();
  rl.close();
}
let sending = Boolean(token);

// ---- 送る ----

let queue = Promise.resolve();
let sent = 0;
let sendError = "";
let skipped = 0;

function send(trial) {
  if (!sending) return;
  queue = queue.then(async () => {
    if (!sending) return;
    try {
      const response = await fetch(`${site}/api/typing/trials`, {
        method: "POST",
        headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
        body: JSON.stringify({ ...trial, client: "cli", layout: "qwerty" }),
      });
      if (response.ok) {
        sent += 1;
        return;
      }
      const body = await response.json().catch(() => ({}));
      // 番号が違う・時間切れ・利用禁止は、送り直しても通らないので、この回は送るのをやめる
      if ([401, 403, 410, 429].includes(response.status)) sending = false;
      sendError = body.error || `送れませんでした (${response.status})`;
    } catch (e) {
      sendError = `送れませんでした: ${e.message}`;
    }
  });
}

// ---- 打つ ----

function codeOf(char) {
  if (/[a-z]/i.test(char)) return `Key${char.toUpperCase()}`;
  if (/[0-9]/.test(char)) return `Digit${char}`;
  return { " ": "Space", "-": "Minus", ",": "Comma", ".": "Period", "'": "Quote" }[char] || "";
}

function shuffle(items) {
  const array = [...items];
  for (let i = array.length - 1; i > 0; i -= 1) {
    const j = Math.floor(Math.random() * (i + 1));
    [array[i], array[j]] = [array[j], array[i]];
  }
  return array;
}

if (!KINDS.some((k) => k.id === kind)) die(`--kind は ${KINDS.map((k) => k.id).join("・")} のどれかにしてください`);
const queueOfSentences = shuffle(SENTENCES.filter((s) => s.kind === kind)).slice(0, count);
const results = [];
let index = 0;
let trial;
let note = "";
let drawnLines = 0;

function newTrial() {
  return { typed: "", keys: [], startedAt: 0, backspaces: 0 };
}

function draw() {
  const sentence = queueOfSentences[index];
  const { kana, pending } = toKana(trial.typed);
  const lines = [
    dim(`${index + 1} / ${queueOfSentences.length}   Enter で次へ・Esc で飛ばす・Ctrl+C でやめる${sending ? "" : "   (送らずに遊んでいます)"}`),
    "",
    `  ${bold(sentence.text)}`,
    `  ${dim(sentence.reading)}`,
    "",
    `  ${kana}${sky(pending)}${sky("▏")}`,
    `  ${dim(trial.typed.toLowerCase())}`,
    note ? `  ${red(note)}` : "",
  ];
  // 前に描いた行を消してから描き直す
  if (drawnLines) out.write(`\x1b[${drawnLines}F\x1b[J`);
  out.write(`${lines.join("\n")}\n`);
  drawnLines = lines.length;
}

function startSentence() {
  trial = newTrial();
  note = "";
  drawnLines = 0;
  out.write("\n");
  draw();
}

function finishSentence() {
  if (!trial.typed) return;
  const sentence = queueOfSentences[index];
  const duration = Math.max(1, Math.round(performance.now() - trial.startedAt));
  results.push({ sentence, typed: trial.typed, duration, backspaces: trial.backspaces });
  // お手本と関係ない入力が続いた文は送らない (サーバーも同じ判定で受け付けない)
  if (suspicious(sentence.reading, trial.typed, "qwerty")) skipped += 1;
  else send({ sentence: sentence.id, typed: trial.typed, keys: trial.keys, duration });
  nextSentence();
}

function nextSentence() {
  index += 1;
  if (index < queueOfSentences.length) startSentence();
  else finish();
}

function onData(data) {
  // Ctrl+C
  if (data === "\x03") return finish();
  // 矢印などのエスケープシーケンスは無視し、Esc だけなら飛ばす
  if (data.startsWith("\x1b")) {
    if (data === "\x1b") nextSentence();
    return;
  }
  // 貼り付け (一度に何文字も届く) と、IME がオンのときの日本語は記録しない
  const chars = [...data];
  if (chars.filter((c) => c >= " ").length > 3) {
    trial = newTrial();
    note = "貼り付けは使えません。この文を打ち直してください";
    return draw();
  }
  if (chars.some((c) => c > "~" && c !== "\x7f")) {
    trial = newTrial();
    note = "IME がオンになっています。半角英数に切り替えてから、この文を打ち直してください";
    return draw();
  }
  const at = performance.now();
  for (const char of chars) {
    // 速く打つと、Enter と前の文字が一度に届くことがある
    if (char === "\r" || char === "\n") {
      finishSentence();
      continue;
    }
    const backspace = char === "\x7f" || char === "\b";
    if (!backspace && (char < " " || char > "~")) continue;
    if (!trial.startedAt) trial.startedAt = at;
    const down = Math.max(0, Math.round(at - trial.startedAt));
    if (backspace) {
      trial.keys.push(["Backspace", "Backspace", down, null]);
      trial.typed = trial.typed.slice(0, -1);
      trial.backspaces += 1;
    } else {
      trial.keys.push([codeOf(char), char, down, null]);
      trial.typed += char;
    }
  }
  if (finished) return;
  note = "";
  draw();
}

// ---- 結果 ----

let finished = false;

async function finish() {
  if (finished) return;
  finished = true;
  process.stdin.setRawMode(false);
  process.stdin.pause();
  let chars = 0;
  let wrong = 0;
  let ms = 0;
  let backspaces = 0;
  const misses = new Map();
  for (const entry of results) {
    const ops = align(entry.sentence.reading, finalKana(entry.typed));
    chars += [...entry.sentence.reading].length;
    wrong += ops.filter((op) => op.op !== "same").length;
    ms += entry.duration;
    backspaces += entry.backspaces;
    for (const op of ops) if (op.op === "omit" || op.op === "sub") misses.set(op.want, (misses.get(op.want) || 0) + 1);
  }
  out.write(`\n${bold("おつかれさまでした！")}\n`);
  if (results.length) {
    out.write(`  速さ ${sky(String(Math.round((chars / ms) * 60000)))} かな/分   正確さ ${sky(String(Math.max(0, Math.round((1 - wrong / chars) * 1000) / 10)))} %   Backspace ${sky(String(backspaces))} 回\n`);
    const top = [...misses].sort((a, b) => b[1] - a[1]).slice(0, 8);
    if (top.length) out.write(`  抜けた・打ち間違えた字: ${top.map(([kana, n]) => `${kana} ${n} 回`).join("・")}\n`);
  }
  if (token) {
    out.write(dim("  送っています…\n"));
    await queue;
    if (sendError) out.write(`  ${red(`${sent} 文を送りました。${sendError}`)}\n`);
    else out.write(`  ${sent} 文の記録を送りました。ありがとうございます！\n`);
    if (skipped) out.write(`  ${dim(`お手本と関係ない入力が続いた ${skipped} 文は送りませんでした。`)}\n`);
  }
  process.exit(0);
}

process.stdin.setRawMode(true);
process.stdin.setEncoding("utf8");
process.stdin.on("data", onData);
startSentence();
