// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// benizara (紅皿、MIT License、Copyright (c) 2018 綾木健一郎) の AZIK の定義ファイル (AZIK.bnz) から、
// 組み込みのローマ字の表 dictionaries/romaji-azik.txt を作る。
//   git clone https://github.com/k-ayaki/benizara
//   node tools/make-romaji-tables.mjs benizara/AZIK.bnz > dictionaries/romaji-azik.txt
//
// .bnz の <k><*> の面は「k の次に押したキー」のかな。k + そのキーの英字を綴りにする。
// 綴りは英小文字だけ (Core の romaji.txt と同じ決まり)。; (っ) や : (ー) を使う綴りは入れない。
// 組み込みの綴りと同じかなになる綴り (ka = か など) は書かない (表を短くするため)。
// 同じ子音を 2 回打つ綴り (kk = きん、tt = たち など) も入れない。AZIK の っ の ; を romaji.txt の表では書けないので、
// っ は今までどおり子音を 2 回打って入れる。
import fs from 'node:fs';

const bytes = fs.readFileSync(process.argv[2]);
const text = bytes[0] === 0xff && bytes[1] === 0xfe ? new TextDecoder('utf-16le').decode(bytes.subarray(2)) : new TextDecoder('shift_jis').decode(bytes);
const rows = ['1234567890-^\\', 'qwertyuiop@[', 'asdfghjkl;:]', 'zxcvbnm,./_'];
const vowels = { a: 'あ', i: 'い', u: 'う', e: 'え', o: 'お' };
const toHiragana = s => s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60)).replace(/ヴ/g, 'ゔ');

const table = new Map();
let prefix = null, row = 0;
for (const raw of text.split(/\r?\n/)) {
  const line = raw.trim();
  if (line === '' || line.startsWith(';')) continue;
  if (/^\[/.test(line)) {
    prefix = /ローマ字シフト無し/.test(line) ? '' : null;
    row = 0;
    continue;
  }
  const face = /^<([a-z])>/.exec(line);
  if (face) {
    if (prefix !== null) prefix = face[1];
    row = 0;
    continue;
  }
  if (prefix === null || row >= rows.length) continue;
  const cells = line.split(',');
  const keys = [...rows[row++]];
  for (let i = 0; i < keys.length && i < cells.length; i++) {
    const key = keys[i];
    const kana = toHiragana(cells[i].trim());
    if (!/^[a-z]$/.test(key) || !/^[ぁ-ゖー]+$/.test(kana)) continue;
    const spelling = prefix + key;
    // 母音だけの綴りと、子音 + 母音の組み込みの綴りは書かない
    if (spelling.length === 1 && vowels[spelling] === kana) continue;
    if (spelling.length === 2 && spelling[1] in vowels && !(spelling[0] in vowels)) continue;
    if (spelling.length === 2 && spelling[0] === spelling[1] && spelling !== 'nn') continue;
    if (!table.has(spelling)) table.set(spelling, kana);
  }
}

const out = [
  '# 組み込みのローマ字の表: AZIK (拡張ローマ字)。設定「ローマ字の表」で AZIK を選ぶと使う',
  '# 作成元: tools/make-romaji-tables.mjs (手で直さない)。表は benizara (https://github.com/k-ayaki/benizara、MIT License) の AZIK.bnz から取った',
  '# 形式: 綴り<Tab>かな',
  ...[...table].map(([spelling, kana]) => `${spelling}\t${kana}`),
];
process.stdout.write(out.join('\n') + '\n');
