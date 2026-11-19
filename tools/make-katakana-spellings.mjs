// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// JMdict (EDRDG、CC BY-SA 4.0) から、英単語とそのカタカナ表記の表 dictionaries/katakana-spellings.txt を作る。
// 英語と判定した語 (export) を変換したとき、候補にカタカナ表記 (エクスポート) を出すのに使う。
//   node tools/make-katakana-spellings.mjs JMdict_e.xml dictionaries/english-words.txt > dictionaries/katakana-spellings.txt
// 形式は「英単語 カタカナ カタカナ …」。
// 入れるのは、漢字の書き方の無いカタカナ語で、最初の意味の最初の英訳が英単語 1 語のもの (エクスポート = export)。
// 英語以外から来た語 (アルバイト)・和製英語 (ナイター)・擬音語や俗語 (ブツブツ = grunt) は、英語の綴りの読みでないので入れない。
// 英単語は、よく使う英単語の一覧 (english-words.txt) にある語だけ。まれな英訳まで入れると、ふつうの英語の文で出る候補が増える。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
const common = new Set(fs.readFileSync(process.argv[3], 'utf8').split('\n').filter(l => /^[a-z]+$/.test(l)));

const lines = new Map();
for (const [, entry] of xml.matchAll(/<entry>([\s\S]*?)<\/entry>/g)) {
  // 漢字やひらがなでも書く語 (ウン・うむ = yes) は、もとから日本語の語
  if (entry.includes('<k_ele>') || /<reb>[^<]*[ぁ-ゖ]/.test(entry)) continue;
  const sense = entry.match(/<sense>([\s\S]*?)<\/sense>/)?.[1];
  if (!sense || /<lsource(?![^>]*xml:lang="eng")|ls_wasei="y"/.test(sense)) continue;
  // 擬音語 (ブツブツ = grunt)・俗語 (ヌコ = cat)・略語 (レス = reply)・方言 (ハイサイ = hello) などの印の付いた意味は、英語の綴りの読みでない
  if (/<misc>|<dial>/.test(sense)) continue;
  const word = sense.match(/<gloss>(.*?)<\/gloss>/)?.[1];
  if (!word || !common.has(word)) continue;
  // カタカナだけの読み。まれな読み (&sk; など) は除く。
  const spellings = [...entry.matchAll(/<r_ele>([\s\S]*?)<\/r_ele>/g)]
    .filter(m => !/<re_inf>/.test(m[1]))
    .map(m => m[1].match(/<reb>(.*?)<\/reb>/)[1])
    .filter(r => /^[ァ-ヺー]+$/.test(r));
  // よく使う語の印 (gai1 など) の付いた語を前に出す
  const rank = /<re_pri>/.test(entry) ? 0 : 1;
  const list = lines.get(word) ?? [];
  for (const s of spellings) if (!list.some(e => e.s === s)) list.push({ s, rank });
  lines.set(word, list);
}

const out = [
  '# Meltype 英単語のカタカナ表記 (英単語 カタカナ カタカナ …)。export → エクスポート',
  '# JMdict (Electronic Dictionary Research and Development Group) のカタカナ語の英訳から tools/make-katakana-spellings.mjs で作成。',
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  ...[...lines].filter(([, list]) => list.length > 0).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
    .map(([w, list]) => `${w} ${list.sort((a, b) => a.rank - b.rank).map(e => e.s).join(' ')}`),
];
process.stdout.write(out.join('\n') + '\n');
