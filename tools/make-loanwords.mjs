// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// JMdict (EDRDG、CC BY-SA 4.0) から、英字で書く語 (Ｌｉｎｕｘ、ＧＨＱ) とそのカタカナの読みの表
// dictionaries/loanwords.txt を作る。リナックス を変換したとき、候補に Linux を出すのに使う。
//   node tools/make-loanwords.mjs JMdict_e.xml > dictionaries/loanwords.txt
// 形式は candidates.txt と同じ「読み (ひらがな) 候補 候補 …」。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
const toHiragana = s => s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60));
// 全角の英数字・記号を半角に
const toHalfWidth = s => s.replace(/[！-～]/g, c => String.fromCharCode(c.charCodeAt(0) - 0xFEE0));

const lines = new Map();
for (const [, entry] of xml.matchAll(/<entry>([\s\S]*?)<\/entry>/g)) {
  // 英字 (と数字・. + & -) だけで書く書き方。空白や ・ の入るもの、まれな書き方 (&rK; など) は除く。
  const spellings = [...entry.matchAll(/<k_ele>([\s\S]*?)<\/k_ele>/g)]
    .filter(m => !/<ke_inf>/.test(m[1]))
    .map(m => toHalfWidth(m[1].match(/<keb>(.*?)<\/keb>/)[1]))
    .filter(k => /^[A-Za-z0-9.+&-]+$/.test(k) && /[A-Za-z]/.test(k));
  if (spellings.length === 0) continue;
  // 読みはカタカナのものだけ (ベースろくじゅうよん のような数字の読みは除く)。まれな読み (&rk;) も除く。
  const readings = [...entry.matchAll(/<r_ele>([\s\S]*?)<\/r_ele>/g)]
    .filter(m => !/<re_inf>/.test(m[1]))
    .map(m => m[1].match(/<reb>(.*?)<\/reb>/)[1].replace(/・/g, ''))
    .filter(r => /^[ァ-ヶー]+$/.test(r))
    .map(toHiragana);
  for (const reading of readings) {
    const list = lines.get(reading) ?? [];
    for (const s of spellings) if (!list.includes(s)) list.push(s);
    lines.set(reading, list);
  }
}

const out = [
  '# Meltype 英字で書く語の候補 (読み 英字 英字 …)。リナックス → Linux、じーえいちきゅー → GHQ',
  '# JMdict (Electronic Dictionary Research and Development Group) の英字で書く語から tools/make-loanwords.mjs で作成。',
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  ...[...lines].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([r, list]) => `${r} ${list.join(' ')}`),
];
process.stdout.write(out.join('\n') + '\n');
