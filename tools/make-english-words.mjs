// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// SCOWL (Spell Checker Oriented Word Lists、Kevin Atkinson) から、よく使う英単語の一覧 dictionaries/english-words.txt を作る。
// 全 OS で、英単語 (meeting) を知るのに使う。
//   node tools/make-english-words.mjs <SCOWL の final フォルダー> > dictionaries/english-words.txt
// SCOWL は http://wordlist.aspell.net/ (scowl-2020.12.07.zip)。大きさ 35 まで (よく使う語) の english / american を使う。
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];
const words = new Set();
for (const size of [10, 20, 35]) {
  for (const kind of ['english-words', 'american-words']) {
    const file = path.join(dir, `${kind}.${size}`);
    if (!fs.existsSync(file)) continue;
    // SCOWL のファイルは Latin-1
    for (const line of fs.readFileSync(file, 'latin1').split(/\r?\n/)) {
      const word = line.trim();
      // 小文字の英字だけ (固有名詞・所有格の 's・アクセント付きは除く)。1 文字は除く。
      if (/^[a-z]{2,}$/.test(word)) words.add(word);
    }
  }
}

const out = [
  '# Meltype よく使う英単語',
  '# SCOWL (Spell Checker Oriented Word Lists) の大きさ 35 までの english / american から tools/make-english-words.mjs で作成。',
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
