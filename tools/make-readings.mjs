// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// JMdict (EDRDG、CC BY-SA 4.0) から、よく使う語の読みの一覧 dictionaries/readings.txt を作る。
// ローマ字の打ち間違い (shimsu → shimasu) を直すとき、直した読みが日本語らしいかを調べるのに使う。
//   node tools/make-readings.mjs JMdict_e.xml > dictionaries/readings.txt
//   node tools/make-readings.mjs JMdict_e.xml rare > dictionaries/readings-rare.txt
// rare では、よく使う語の印が無い語のうち、ひらがなで 3 文字以上の読み (ろんぱ・うちどめ・かんれんづける) を出す (よく使う語と同じ読みは除く)。
// 2 文字以下の読みは英単語の綴りとぶつかり (pa = ぱ)、カタカナの読み (ヴィタミン) は外来語で英単語と同じ綴りになるので入れない。
// 古語・廃語・まれな語と、検索用・古い・不規則なかなの書き方は、3〜4 文字の読みだけ除く。
// 4 文字も残すと、これ|sha|する を これしゃ (此れ者)|する と割る。
// 5 文字以上も除くと、使わない語でも日本語の途中が英字になる (あきぐるま → あきguruま、あっかんべえ → あっかんbee)。
// 1 行に「読み」か「読み[TAB]活用の種類」(v1 = 一段、v5k など = 五段、vk = 来る、vs = する、i = い形容詞)。
// 同じ読みで活用の違う語 (繰る v5r・来る vk) は、活用の種類を , でつなぐ (JMdict で先に出る語が先)。
// 活用した形 (たべ、かきま、はやく) は Meltype が読み込むときに作る。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
const rare = process.argv[3] === 'rare';
const common = /<re_pri>(?:ichi1|news1|spec1|spec2|gai1)<\/re_pri>/;
const longHiragana = /<reb>[ぁ-ゖー]{3,}<\/reb>/;
const unusualKana = /<re_inf>&(?:sk|ok|ik|rk);<\/re_inf>/;
const unusedSense = /<misc>&(?:arch|obs|rare);<\/misc>/;
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
const commonReadings = new Set();
for (const [, entry] of xml.matchAll(/<entry>([\s\S]*?)<\/entry>/g)) {
  const elements = [...entry.matchAll(/<r_ele>([\s\S]*?)<\/r_ele>/g)];
  const used = [...entry.matchAll(/<sense>([\s\S]*?)<\/sense>/g)].some(m => !unusedSense.test(m[1]));
  for (const m of elements) if (common.test(m[1])) commonReadings.add(toHiragana(m[1].match(/<reb>(.*?)<\/reb>/)[1]));
  const readings = elements
    .filter(m => rare ? !common.test(m[1]) && longHiragana.test(m[1]) &&
      (m[1].match(/<reb>(.*?)<\/reb>/)[1].length >= 5 || used && !unusualKana.test(m[1])) : common.test(m[1]))
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

if (rare) for (const reading of commonReadings) lines.delete(reading);

const out = [
  ...(rare ? [
    '# Meltype よく使う語でない語の読みの一覧 (読み [TAB] 活用の種類)',
    '# 日英の区切りで、readings.txt の語より重い日本語の語として読む (かんれんづけ・くるまどめ)。',
    '# JMdict (Electronic Dictionary Research and Development Group) のよく使う語の印が無い語の、ひらがなで 3 文字以上の読みから tools/make-readings.mjs で作成 (3〜4 文字は古語・廃語などを除く)。',
  ] : [
    '# Meltype 読みの一覧 (よく使う語の読み [TAB] 活用の種類)',
    '# ローマ字の打ち間違いを直すとき、直した読みが日本語らしいかを調べるのに使う。',
    '# JMdict (Electronic Dictionary Research and Development Group) のよく使う語から tools/make-readings.mjs で作成。',
  ]),
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  ...[...lines].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([r, k]) => k.size > 0 ? `${r}\t${[...k].join(',')}` : r),
];
process.stdout.write(out.join('\n') + '\n');
