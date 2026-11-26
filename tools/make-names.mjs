// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
//
// SKK 辞書 SKK-JISYO.jinmei (GPL-2.0-or-later) から、日本人の姓・名の読みの一覧 dictionaries/names.txt を作る。
// 日英の区切り (ReadingLattice.cs) で、大文字で始めてローマ字で打った日本人名 (Takahashi) を 1 つの英語の語にするのに使う。
//   node tools/make-names.mjs SKK-JISYO.jinmei > dictionaries/names.txt
// 入手先: https://github.com/skk-dev/dict
import fs from 'node:fs';

const text = new TextDecoder('euc-jp').decode(fs.readFileSync(process.argv[2]));
const lines = text.split('\n');
const notices = lines.slice(0, 20)
  .filter(line => line.startsWith(';;') && line.includes('Copyright'))
  .map(line => line.slice(2).trim());

const readings = new Set();
for (const line of lines) {
  if (line.startsWith(';')) continue;
  const [reading] = line.split(' /');
  if (/^[ぁ-ゖー]+$/.test(reading)) readings.add(reading);
}

const out = [
  '# Meltype 日本人の姓・名の読みの一覧',
  '# 日英の区切りで、大文字で始めてローマ字で打った日本人名 (Takahashi) を、頭で切って日本語を続けず (T|赤橋) 1 つの英語の語にするのに使う。',
  '# SKK 辞書 SKK-JISYO.jinmei の送り仮名の無い語の読みから tools/make-names.mjs で作成。元データ: https://github.com/skk-dev/dict',
  ...notices.map(n => `# ${n}`),
  '# ライセンス: GPL-2.0-or-later (ファイルの先頭の「version 2, or (at your option) any later version」)。Meltype では GPL-3.0-or-later で使う。',
  ...[...readings].sort(),
];
process.stdout.write(out.join('\n') + '\n');
