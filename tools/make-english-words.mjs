// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// SCOWL (Spell Checker Oriented Word Lists、Kevin Atkinson) から、よく使う英単語の一覧 dictionaries/english-words.txt を作る。
// 全 OS で、英単語 (meeting) を知るのに使う。
//   node tools/make-english-words.mjs <SCOWL の final フォルダー> > dictionaries/english-words.txt
// SCOWL は http://wordlist.aspell.net/ (scowl-2020.12.07.zip)。
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];
// 入力に使うローマ字の表から綴りを取り出し、小書き文字や外来音も判定する。
const romajiSource = fs.readFileSync(new URL('../src/Meltype.Core/Detection/RomajiDetector.cs', import.meta.url), 'utf8');
const syllables = [...romajiSource.matchAll(/\("[ぁ-ゖ]+", \[([^\]]+)\]\)/g)]
  .flatMap(([, spellings]) => [...spellings.matchAll(/"([a-z]+)"/g)].map(([, spelling]) => spelling));
if (syllables.length === 0) throw new Error('ローマ字の表を読み取れません');

const prefixes = new Set();
for (const syllable of syllables) {
  for (let length = 1; length < syllable.length; length++) prefixes.add(syllable.slice(0, length));
}
// n は「ん」、子音の重なりや tch の t は「っ」として読める。末尾の打ちかけも含める。
const kana = new RegExp(`^(?:${syllables.join('|')}|n|([bcdfghjklmpqrstvwxyz])(?=\\1)|t(?=ch))*(?:${[...prefixes].join('|')})?$`);

function readWords(file) {
  // SCOWL のファイルは Latin-1。小文字の英字だけで 2 文字以上の語を使う。
  return fs.readFileSync(file, 'latin1').split(/\r?\n/).map(line => line.trim()).filter(word => /^[a-z]{2,}$/.test(word));
}

function wordsAt(size) {
  return ['english-words', 'american-words'].flatMap(kind => readWords(path.join(dir, `${kind}.${size}`)));
}

const common = new Set([10, 20, 35].flatMap(wordsAt));
// 先頭を 1 文字取った低頻度語があると、かな入力で元の英単語を分けてしまう (function → は + unction)。
const commonTails = new Set([...common].map(word => word.slice(1)));
const words = new Set(common);
for (const word of [40, 50].flatMap(wordsAt)) {
  // 頻度の低い語が日本語の読みや打ちかけと重なると、入力の途中を英字に分けてしまう (anode → あので)。
  if (kana.test(word) || commonTails.has(word)) continue;
  // 短い語は余計な子音を 1 文字打ったローマ字とも重なる (shims → し + ms)。
  if (word.length <= 5 && [...word].some((letter, i) => !'aiueo'.includes(letter) && kana.test(word.slice(0, i) + word.slice(i + 1)))) continue;
  words.add(word);
}
for (const level of [1, 3]) {
  for (const word of readWords(path.join(dir, '..', 'misc', `profane.${level}`))) words.add(word);
}


const out = [
  '# Meltype よく使う英単語',
  '# SCOWL (Spell Checker Oriented Word Lists) の english-words・american-words (10, 20, 35, 40, 50) と misc/profane.{1,3} から tools/make-english-words.mjs で作成。',
  '# 大きさ 40・50 はローマ字の読みや打ちかけと重なる語を除く。',
  '# Copyright 2000-2018 by Kevin Atkinson',
  '#   Permission to use, copy, modify, distribute and sell these word lists, the associated scripts, the output created from the scripts,',
  '#   and its documentation for any purpose is hereby granted without fee, provided that the above copyright notice appears in all copies and',
  '#   that both that copyright notice and this permission notice appear in supporting documentation. Kevin Atkinson makes no representations',
  '#   about the suitability of this array for any purpose. It is provided "as is" without express or implied warranty.',
  '# 詳しくは THIRD-PARTY-NOTICES.md (SCOWL の Copyright ファイルの全文)。',
  ...[...words].sort(),
];
process.stdout.write(out.join('\n') + '\n');
console.error(`${words.size} 語`);
