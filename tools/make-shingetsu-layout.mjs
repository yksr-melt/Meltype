// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 新月配列 (https://github.com/nagamine-git/shingetsu-layout、MIT License、Copyright (c) 2026 nagamine-git) の
// Google 日本語入力用ローマ字テーブル (shingetsu-romantable.txt) から、src/Meltype.Core/Input/KanaSequenceTables.cs を作る。
//   git clone https://github.com/nagamine-git/shingetsu-layout
//   node tools/make-shingetsu-layout.mjs shingetsu-layout/shingetsu-romantable.txt > src/Meltype.Core/Input/KanaSequenceTables.cs
//
// 表の 1 行は「打つキー<Tab>かな<Tab>次の入力 (省略可)」。キーは US 配列の刻印の文字で、Meltype はキーの位置で読む。
import fs from 'node:fs';

const rows = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/).filter(line => line.trim() !== '' && !line.startsWith('#'));
const out = [
  '// SPDX-License-Identifier: GPL-3.0-or-later',
  '// 作成元: tools/make-shingetsu-layout.mjs (手で直さない)',
  '// 新月配列の表は https://github.com/nagamine-git/shingetsu-layout (MIT License、Copyright (c) 2026 nagamine-git) の',
  '// shingetsu-romantable.txt から取った。',
  '',
  'namespace Meltype.Input;',
  '',
  'internal static class KanaSequenceTables',
  '{',
  '    /// <summary>配列ごとの表 (「打つキー<Tab>かな<Tab>次の入力」の行。キーは US 配列の刻印の文字)。</summary>',
  '    public static readonly Dictionary<KanaLayout, string[]> Tables = new()',
  '    {',
  '        [KanaLayout.Shingetsu] =',
  '        [',
  ...rows.map(row => `            ${JSON.stringify(row)},`),
  '        ],',
  '    };',
  '}',
];
process.stdout.write(out.join('\n') + '\n');
