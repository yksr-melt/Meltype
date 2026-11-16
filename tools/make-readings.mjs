// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// JMdict (EDRDG、CC BY-SA 4.0) から、よく使う語の読みの一覧 dictionaries/readings.txt を作る。
// ローマ字の打ち間違い (shimsu → shimasu) を直すとき、直した読みが日本語らしいかを調べるのに使う。
//   node tools/make-readings.mjs JMdict_e.xml > dictionaries/readings.txt
// 1 行に「読み」か「読み[TAB]活用の種類」(v1 = 一段、v5k など = 五段、vk = 来る、vs = する、i = い形容詞)。
// 同じ読みで活用の違う語 (繰る v5r・来る vk) は、活用の種類を , でつなぐ (JMdict で先に出る語が先)。
// 活用した形 (たべ、かきま、はやく) は Meltype が読み込むときに作る。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
const common = /<re_pri>(?:ichi1|news1|spec1|spec2|gai1)<\/re_pri>/;
const toHiragana = s => s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60));

function conjugation(pos) {
  for (const p of pos) {
    if (p === '&v1;' || p === '&v1-s;') return 'v1';
    const five = p.match(/^&(v5(?:k-s|[kgsbmnrtuw]|aru|r-i|u-s));$/);
    if (five) return five[1];
    if (p === '&vk;') return 'vk';
    if (p === '&vs-i;' || p === '&vs-s;') return 'vs';
    if (p === '&adj-i;' || p === '&adj-ix;') return 'i';
  }
  return null;
}

const lines = new Map();
for (const [, entry] of xml.matchAll(/<entry>([\s\S]*?)<\/entry>/g)) {
  const readings = [...entry.matchAll(/<r_ele>([\s\S]*?)<\/r_ele>/g)]
    .filter(m => common.test(m[1]))
    .map(m => toHiragana(m[1].match(/<reb>(.*?)<\/reb>/)[1]))
    .filter(r => /^[ぁ-ゖー]+$/.test(r));
  if (readings.length === 0) continue;
  const pos = [...entry.matchAll(/<pos>(.*?)<\/pos>/g)].map(m => m[1]);
  const kind = conjugation(pos);
  for (const reading of readings) {
    if (!lines.has(reading)) lines.set(reading, new Set());
    if (kind) lines.get(reading).add(kind);
  }
}

const out = [
  '# Meltype 読みの一覧 (よく使う語の読み [TAB] 活用の種類)',
  '# ローマ字の打ち間違いを直すとき、直した読みが日本語らしいかを調べるのに使う。',
  '# JMdict (Electronic Dictionary Research and Development Group) のよく使う語から tools/make-readings.mjs で作成。',
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  ...[...lines].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([r, k]) => k.size > 0 ? `${r}\t${[...k].join(',')}` : r),
];
process.stdout.write(out.join('\n') + '\n');
