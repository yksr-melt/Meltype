// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
//
// JMdict (EDRDG、CC BY-SA 4.0) から、カタカナで書く語の読みの一覧 dictionaries/katakana.txt を作る。
// 日英の区切り (ReadingLattice.cs) で、readings.txt に無いカタカナ語 (ホスティング) を日本語の語として読むのに使う。
//   node tools/make-katakana.mjs JMdict_e.xml > dictionaries/katakana.txt
// よく使う語の印の有無によらず、カタカナだけの読みを入れる。
// 3 文字以下の語は入れない。英単語をローマ字で読むと、その一部に短いカタカナ語がたまたま現れて、英単語が日本語に割られる
// (gameover の ガメ、thin の ティン、massive の シヴェ)。よく使う短いカタカナ語 (ペン) は readings.txt にある。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
const toHiragana = s => s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60));

const words = new Set();
for (const [, reb] of xml.matchAll(/<reb>([ァ-ヶー]+)<\/reb>/g)) {
  const reading = toHiragana(reb);
  if (reading.length >= 4) words.add(reading);
}

const out = [
  '# Meltype カタカナで書く語の読みの一覧',
  '# 日英の区切りで、よく使う語の読み (readings.txt) に無いカタカナ語を日本語の語として読むのに使う。',
  '# JMdict (Electronic Dictionary Research and Development Group) のカタカナだけの読みから tools/make-katakana.mjs で作成。',
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  ...[...words].sort(),
];
process.stdout.write(out.join('\n') + '\n');
