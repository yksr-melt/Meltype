// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt
//
// Wikipedia の List of minerals の A〜Z の箇条書き (説明を含めて1行1項目) をテキストに保存して渡す。
//   node tools/make-mineral-words.mjs minerals.txt > dictionaries/english-minerals.txt
// 入力の版: https://en.wikipedia.org/w/index.php?title=List_of_minerals&oldid=1383057146
import fs from 'node:fs';

const words = new Set();
for (const line of fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/)) {
  // Heliotrope (bloodstone) のように括弧内が単独の別名なら、その名前も残す。
  for (const [, alias] of line.matchAll(/\(([a-z]{3,})\)/g)) words.add(alias);
  // 元素による亜種の括弧と説明を外す。ASCII のキーで打てる綴りにし、複合名は構成する語も登録する。
  const name = line.replace(/\([^)]*\)/g, '').replace(/\s+group\s*$/i, '').trim()
    .normalize('NFKD').replace(/\p{M}/gu, '').replace(/^[*\s]+|[-\s]+$/g, '').toLowerCase();
  for (const word of name.split(/\s+/)) if (/^[a-z][a-z-]+$/.test(word)) words.add(word);
}
if (words.size < 1000) throw new Error('鉱物名の一覧が欠けています');
process.stdout.write([
  '# Meltype 鉱物・天然石の英語名',
  '# Wikipedia contributors, List of minerals (2026-10-31 の版) の名前から tools/make-mineral-words.mjs で作成。',
  '# https://en.wikipedia.org/w/index.php?title=List_of_minerals&oldid=1383057146',
  '# CC BY-SA 4.0: https://creativecommons.org/licenses/by-sa/4.0/',
  '# 括弧内の亜種・説明は除き、アクセントを ASCII にし、複合名は空白で分けた。',
  ...[...words].sort(),
].join('\n') + '\n');
console.error(`${words.size} 語`);
