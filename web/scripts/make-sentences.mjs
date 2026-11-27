#!/usr/bin/env node
/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Meltype Contributors */

// sentences/*.txt (タイピングのお手本) から public/assets/js/typing-sentences.js を作る。
// お手本は txt だけを直す (typing-sentences.js は作るたびに書き直すので、コミットしない)。
// ファイル名は「並び順-種類の id.txt」。中身は「種類: 表示名」の行と、1 行に 1 文の「表示する文|読み」。# で始まる行と空行は読まない。
// 文の id は文と読みから作る。文や読みを直すと別の文になり、直す前に集めたデータが直した後の文を指すことはない。
// 決まり: 一度出したお手本の行は消さない (集めたデータは id でお手本を指すため)。出すのをやめるときは、行の頭に「出さない:」を付ける。
import { createHash } from "node:crypto";
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

import { finalKana, toRomaji } from "../public/assets/js/romaji.js";

const dir = fileURLToPath(new URL("../sentences/", import.meta.url));
const out = fileURLToPath(new URL("../public/assets/js/typing-sentences.js", import.meta.url));
// 読みはひらがなと「ー、。！？〜…「」（）」(打つときは - , . ! ? ~ [ ] ( ) と、「…」は . を 3 回)。日本語と英語が混ざった文は、英語の部分を英小文字・数字・「-」・空白で書く
const READING = /^[ぁ-ゖー、。！？〜…「」（）a-z0-9 -]+$/;
// 長い文は打つのに疲れ、1 文の打鍵の上限 (src/typing.js) も大きくなる
const MAX_KANA = 40;

export function sentenceId(text, reading) {
  return createHash("sha256").update(`${text}\n${reading}`).digest("hex").slice(0, 12);
}

// txt を読み、文の並びにする。間違いはファイル名と行番号を付けて集め、まとめて投げる
export function parse(files) {
  const kinds = [];
  const sentences = [];
  const errors = [];
  const seen = new Map();
  for (const [name, content] of files) {
    const match = /^\d+-([a-z]+)\.txt$/.exec(name);
    if (!match) {
      errors.push(`${name}: ファイル名は「並び順-種類の id.txt」にしてください (例: 1-plain.txt)`);
      continue;
    }
    const kind = { id: match[1], label: "" };
    content.split(/\r?\n/).forEach((raw, index) => {
      const line = raw.trim();
      const where = `${name}:${index + 1}`;
      if (!line || line.startsWith("#")) return;
      const label = /^種類:\s*(.+)$/.exec(line);
      if (label) {
        kind.label = label[1];
        return;
      }
      const retired = line.startsWith("出さない:");
      const parts = (retired ? line.slice("出さない:".length) : line).split("|");
      if (parts.length !== 2) return errors.push(`${where}: 「表示する文|読み」の形にしてください`);
      const [text, reading] = parts.map((part) => part.trim());
      if (!text || !reading) return errors.push(`${where}: 文と読みの両方を書いてください`);
      if (!READING.test(reading)) return errors.push(`${where}: 読みは、ひらがなと「ー、。！？〜…「」（）」(英語の部分は英小文字・数字・「-」・空白) だけで書いてください`);
      if ([...reading].length > MAX_KANA) return errors.push(`${where}: 読みは ${MAX_KANA} 字までにしてください`);
      for (const mark of ["、", "。"]) {
        if (text.split(mark).length !== reading.split(mark).length) return errors.push(`${where}: 文と読みで「${mark}」の数が違います`);
      }
      // 日本語の部分が、ローマ字で打ってそのかなになるか (「…」は . を 3 回打つので「。。。」になる)
      const kana = reading.split(/[a-z0-9 -]+/).filter(Boolean);
      if (kana.some((part) => finalKana(toRomaji(part)) !== part.replaceAll("…", "。。。"))) return errors.push(`${where}: ローマ字で打てない読みです`);
      if (seen.has(reading)) return errors.push(`${where}: 同じ読みの文が ${seen.get(reading)} にあります`);
      seen.set(reading, where);
      sentences.push({ kind: kind.id, id: sentenceId(text, reading), text, reading, ...(retired ? { retired: true } : {}) });
    });
    if (!kind.label) errors.push(`${name}: 「種類: 表示名」の行を書いてください`);
    if (!sentences.some((s) => s.kind === kind.id && !s.retired)) errors.push(`${name}: 出す文が 1 つもありません`);
    kinds.push(kind);
  }
  if (errors.length) throw new Error(`お手本 (sentences/*.txt) に間違いがあります:\n${errors.join("\n")}`);
  return { kinds, sentences };
}

export function readSentences() {
  const names = readdirSync(dir)
    .filter((name) => name.endsWith(".txt"))
    .sort((a, b) => parseInt(a, 10) - parseInt(b, 10));
  return parse(names.map((name) => [name, readFileSync(dir + name, "utf8")]));
}

function render({ kinds, sentences }) {
  return `/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Meltype Contributors */

// scripts/make-sentences.mjs が sentences/*.txt から作る。手で直さない。
export const KINDS = ${JSON.stringify(kinds, null, 2)};

export const SENTENCES = [
${sentences.map((s) => `  ${JSON.stringify(s)},`).join("\n")}
];

const byId = new Map(SENTENCES.map((sentence) => [sentence.id, sentence]));

export function sentenceById(id) {
  return byId.get(id) || null;
}
`;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  try {
    const result = readSentences();
    writeFileSync(out, render(result));
    console.log(`お手本 ${result.sentences.length} 文 (${result.kinds.map((k) => k.label).join("・")}) を書き出しました`);
  } catch (e) {
    console.error(e.message);
    process.exit(1);
  }
}
