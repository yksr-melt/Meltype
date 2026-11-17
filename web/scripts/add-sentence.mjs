#!/usr/bin/env node
/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピングのお手本を足す。
//   npm run add-sentence -- "表示する文。" "ひょうじするぶん。"
//   npm run add-sentence -- --check "表示する文。" "ひょうじするぶん。"   (書き込まずに確かめるだけ)
// 次の番号の id を付けて public/assets/js/typing-sentences.js の末尾に足す。
// 決まり: 足すだけで、今ある文は書き換えない (一度使った id の文と読みを変えると、集めたデータが別の文を指すため)。
import { readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { SENTENCES } from "../public/assets/js/typing-sentences.js";

const FILE = fileURLToPath(new URL("../public/assets/js/typing-sentences.js", import.meta.url));
const READING = /^[ぁ-ゖー、。]+$/;
const MAX_KANA = 40;

function fail(message) {
  console.error(`お手本を足せません: ${message}`);
  process.exit(1);
}

const args = process.argv.slice(2);
const checkOnly = args[0] === "--check";
const [text, reading] = (checkOnly ? args.slice(1) : args).map((value) => (value || "").trim());

if (!text || !reading) fail('文と読みを渡してください。例: npm run add-sentence -- "表示する文。" "ひょうじするぶん。"');
if (!READING.test(reading)) fail("読みは、ひらがなと「ー、。」だけで書いてください");
if ([...reading].length > MAX_KANA) fail(`読みは ${MAX_KANA} 字までにしてください (長い文は打つのに疲れ、1 文の打鍵の上限も大きくなるため)`);
if (/["\\\n]/.test(text)) fail("文に「\"」「\\」・改行は入れられません");
// 読みと文で、句読点の数がそろっているかを見る (打つ人は読みを見て「、。」も打つため)
for (const mark of ["、", "。"]) {
  if (text.split(mark).length !== reading.split(mark).length) fail(`文と読みで「${mark}」の数が違います`);
}
const same = SENTENCES.find((s) => s.text === text || s.reading === reading);
if (same) fail(`同じ文か読みがもうあります (${same.id}: ${same.text})`);

const last = Math.max(...SENTENCES.map((s) => Number(s.id.slice(1))));
const id = `s${String(last + 1).padStart(3, "0")}`;
const line = `  { id: "${id}", text: "${text}", reading: "${reading}" },\n`;

if (checkOnly) {
  console.log(`足せます: ${line.trim()}`);
  process.exit(0);
}

const source = readFileSync(FILE, "utf8");
const end = source.indexOf("];\n");
if (end < 0) fail("typing-sentences.js の SENTENCES の終わりが見つかりません");
writeFileSync(FILE, source.slice(0, end) + line + source.slice(end));
console.log(`足しました: ${id} ${text} (${reading})`);
