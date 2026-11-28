// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 ninjin0802
//
// Unicode の emoji-test.txt から、アプリに入れる絵文字の一覧 (app/src/main/assets/emoji.txt) を作る。
//   curl -LO https://unicode.org/Public/18.0.0/emoji/emoji-test.txt
//   node tools/make-emoji-asset.ts emoji-test.txt app/src/main/assets/emoji.txt
// 形を変えるだけで、選別はしない。fully-qualified の行を、元の並びのまま、グループごとに書き出す。
// どのグループをどの種類に入れるか、どれを一覧から外すか (肌の色の違いなど) は、アプリの側
// (app/src/main/kotlin/…/emoji/EmojiData.kt) が決める。
// 書き出す形 (UTF-8、行末は LF):
//   # で始まる行  説明。読み飛ばす
//   @グループ名   ここから先の絵文字が入る、Unicode のグループ
//   そのほかの行  絵文字を空白で区切って並べたもの (元のサブグループごとに 1 行)

import fs from 'node:fs';

const [source, target, ...rest] = process.argv.slice(2);
if (!source || !target || rest.length > 0) {
  console.error('usage: node tools/make-emoji-asset.ts <emoji-test.txt> <emoji.txt>');
  process.exit(2);
}

let version: string | undefined;
let date: string | undefined;
const out: string[] = [];
let row: string[] = [];
const flush = (): void => {
  if (row.length > 0) out.push(row.join(' '));
  row = [];
};

for (const line of fs.readFileSync(source, 'utf8').split('\n')) {
  let m: RegExpMatchArray | null;
  if ((m = line.match(/^# Version: (\S+)/))) version = m[1];
  else if ((m = line.match(/^# Date: (.+)/))) date = m[1];
  else if ((m = line.match(/^# group: (.+)/))) {
    flush();
    out.push('@' + m[1]);
  } else if (line.startsWith('# subgroup:')) flush();
  else if ((m = line.match(/^([0-9A-F ]+?)\s*; fully-qualified\s/)))
    row.push(m[1]!.split(' ').filter(Boolean).map(cp => String.fromCodePoint(parseInt(cp, 16))).join(''));
}
flush();
if (!version || !date) {
  console.error('emoji-test.txt の Version または Date の行が見つからない');
  process.exit(1);
}

const header = [
  `# Unicode Emoji ${version} の emoji-test.txt から、tools/make-emoji-asset.ts で作った。手で直さない。`,
  `# 元のファイル: https://unicode.org/Public/${version}.0/emoji/emoji-test.txt (Date: ${date})`,
  '# © Unicode, Inc. Unicode License v3 (https://www.unicode.org/license.txt)。表記は THIRD-PARTY-NOTICES.md。',
  '# 形: 「@」で始まる行は Unicode のグループ名。そのほかの行は、fully-qualified の絵文字を空白で区切ったもの。',
];
fs.writeFileSync(target, [...header, ...out].join('\n') + '\n');
