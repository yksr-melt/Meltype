// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 新月配列 (https://github.com/nagamine-git/shingetsu-layout、MIT License、Copyright (c) 2026 nagamine-git) の
// Google 日本語入力用ローマ字テーブル (shingetsu-romantable.txt) から、dictionaries/kana-layouts-shingetsu.txt を作る
// (形式は dictionaries/kana-layouts.txt と同じ)。
//   git clone https://github.com/nagamine-git/shingetsu-layout
//   node tools/make-shingetsu-layout.mjs shingetsu-layout/shingetsu-romantable.txt > dictionaries/kana-layouts-shingetsu.txt
//
// テーブルのキーは US 配列の刻印なので、キーの位置で日本語キーボードの刻印に読み替える (US の ; ' [ ] = は、JIS の ; : @ [ ^ の位置)。
import fs from 'node:fs';

const us = { "'": ':', '[': '@', ']': '[', '=': '^' };
const keys = text => [...text].map(c => us[c] ?? c).join(' ');
const rows = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/).filter(line => line.trim() !== '' && !line.startsWith('#'));
const out = [
  '# 新月配列の表。作成元: tools/make-shingetsu-layout.mjs (手で直さない)',
  '# 表は https://github.com/nagamine-git/shingetsu-layout (MIT License、Copyright (c) 2026 nagamine-git) の shingetsu-romantable.txt から取った',
  '# 形式は kana-layouts.txt と同じ: 配列<Tab>打つキー<Tab>かな[<Tab>次の入力]',
  ...rows.map(row => {
    const [input, kana, next] = row.split('\t');
    return ['Shingetsu', keys(input), kana, ...(next ? [keys(next)] : [])].join('\t');
  }),
];
process.stdout.write(out.join('\n') + '\n');
