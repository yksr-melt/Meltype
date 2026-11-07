// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// ソースファイルの先頭のライセンス・著作権の表記を確かめる bot (.github/workflows/notices.yml から呼ぶ)。
//   - Pull Request で新しく足したソースファイルに `SPDX-License-Identifier` と `Copyright (C) <年> <名前>` があるか
//   - 作者 (yksr-melt) 以外の人が、Yukishiro (雪代) の名義で著作権の行を書いていないか
//     (貢献の著作権は貢献した人に残る: CONTRIBUTING.md の CLA。自分の名前で書いてもらう)
// 見つけたら Pull Request にコメントする (直ったらコメントを「直りました」に書き換える)。
// Pull Request のコードは動かさない。GitHub の API で、変更 (patch) の足された行を文字列として読むだけ。
import * as v from 'valibot';
import { createGitHub, readEvent, type GitHub, type PullFile } from '../lib/github-api.ts';

type Finding = { file: string; problems: string[] };

const MARKER = '<!-- meltype-bot --><!-- notices -->';
/** Yukishiro 名義で書いてよい人 (作者)。 */
const OWNERS = ['yksr-melt'];
/** 先頭に表記を書くソースファイル。辞書・文書・設定ファイル・画像は見ない。 */
const SOURCE_FILE = /\.(cs|swift|ps1|psm1|sh|py|cpp|c|h|hpp|m|mm|mjs|js|ts|lisp|iss)$/i;
/** Yukishiro の名義 */
const OWNER_NAME = /yukishiro|雪代|ゆきしろ|yksr/i;
/** 先頭の何行の中に表記があればよいか (shebang やエンコーディングの行の後に書くこともあるので少し余裕を持たせる) */
const HEADER_LINES = 8;

/** patch の足された行 (先頭の + を外す)。 */
const addedLines = (patch: string | undefined) => (patch ?? '').split('\n').filter(l => l.startsWith('+') && !l.startsWith('+++')).map(l => l.slice(1));

/** コードスパン。ファイル名・著作権の行は PR の作者が書けるので、バッククォートで抜けられないようにする (meltype-bot と同じ)。 */
const code = (text: unknown) => '`' + String(text ?? '').replace(/`/g, 'ˋ') + '`';

const copyrightOf = (line: string) => /copyright\s*(\(c\)|©)?\s*\d{4}/i.test(line) ? line.replace(/^[\s#/;*!<>-]*(rem\s+)?/i, '').trim() : null;

function check(files: PullFile[], author: string) {
  const findings: Finding[] = [];
  const isOwner = OWNERS.some(o => o.toLowerCase() === author.toLowerCase());
  for (const file of files) {
    if (!SOURCE_FILE.test(file.filename)) continue;
    const added = addedLines(file.patch);
    if (file.status === 'added') {
      // patch が大きすぎると GitHub は patch を返さない。そのときは見ない
      if (!file.patch) continue;
      const head = added.slice(0, HEADER_LINES);
      const problems: string[] = [];
      if (!head.some(l => l.includes('SPDX-License-Identifier'))) problems.push('`SPDX-License-Identifier: GPL-3.0-or-later` がありません');
      const copyright = head.map(copyrightOf).find(c => c);
      if (!copyright) problems.push('`Copyright (C) <年> <あなたの名前>` がありません');
      else if (!isOwner && OWNER_NAME.test(copyright)) problems.push(`著作権の行が作者の名義になっています (${code(copyright)})。あなたの名前にしてください`);
      if (problems.length) findings.push({ file: file.filename, problems });
    }
    else if (!isOwner) {
      // 既存のファイルに、作者の名義の著作権の行を足していないか
      const copyright = added.map(copyrightOf).find(c => c && OWNER_NAME.test(c));
      if (copyright) findings.push({ file: file.filename, problems: [`作者の名義の著作権の行を足しています (${code(copyright)})。あなたが書いた部分なら、あなたの名前の行を足してください`] });
    }
  }
  return findings;
}

function message(findings: Finding[], author: string) {
  const list = findings.map(f => `- ${code(f.file)}\n${f.problems.map(p => `  - ${p}`).join('\n')}`).join('\n');
  return [
    `@${author} さん、ソースファイルの先頭のライセンス・著作権の表記を確認させてください。`,
    '',
    list,
    '',
    '新しく作ったソースファイルの先頭には、次の 2 行を入れてください (コメントの記号はファイルに合わせてください)。',
    '貢献の著作権は書いた人に残るので ([CLA](https://github.com/yksr-melt/Meltype/blob/main/CONTRIBUTING.md) の 3)、名前は **あなたの名前** (GitHub のユーザー名など) にしてください。',
    '',
    '```',
    '// SPDX-License-Identifier: GPL-3.0-or-later',
    '// Copyright (C) 2026 あなたの名前',
    '```',
    '',
    '直して push すると、このコメントは自動で書き換わります。',
  ].join('\n');
}

async function upsert(gh: GitHub, number: number, body: string | null) {
  const mine = await gh.findBotComment(number, MARKER);
  if (!body) {
    // 直った: 前に指摘していたら、直ったことを書く (指摘が無かったなら何もしない)
    if (mine && !mine.body?.includes('✅')) await gh.updateComment(mine.id, `${MARKER}\n✅ ライセンス・著作権の表記を確認しました。ありがとうございます。`);
    return;
  }
  if (mine) await gh.updateComment(mine.id, `${MARKER}\n${body}`);
  else await gh.postComment(number, `${MARKER}\n${body}`);
}

async function main() {
  const gh = createGitHub();
  const pr = readEvent(v.looseObject({ pull_request: v.looseObject({ number: v.number(), user: v.looseObject({ login: v.string() }) }) })).pull_request;
  const author = pr.user.login;
  const findings = check(await gh.listPullFiles(pr.number), author);
  for (const f of findings) console.log(`${f.file}: ${f.problems.join(' / ')}`);
  await upsert(gh, pr.number, findings.length ? message(findings, author) : null);
}

/** bun bots/notices/main.ts --self-test: 判定だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  const file = (filename: string, status: string, lines: string[]): PullFile => ({ filename, status, patch: lines.map(l => `+${l}`).join('\n') });
  const cases: [string, PullFile[], string, number][] = [
    ['他の人が Yukishiro 名義', [file('a.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 Yukishiro'])], 'someone', 1],
    ['他の人が自分の名前', [file('a.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 someone'])], 'someone', 0],
    ['作者が Yukishiro 名義', [file('a.cs', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 Yukishiro'])], 'yksr-melt', 0],
    ['表記が無い', [file('b.sh', 'added', ['#!/bin/bash', 'echo hi'])], 'yksr-melt', 1],
    ['shebang の後に表記', [file('b.sh', 'added', ['#!/bin/bash', '# SPDX-License-Identifier: GPL-3.0-or-later', '# Copyright (C) 2026 someone'])], 'someone', 0],
    ['ソースでないファイル', [file('docs/a.md', 'added', ['# title'])], 'someone', 0],
    ['既存のファイルに Yukishiro 名義を足した', [file('c.ps1', 'modified', ['# Copyright (C) 2026 Yukishiro'])], 'someone', 1],
    ['雪代 の名義', [file('d.swift', 'added', ['// SPDX-License-Identifier: GPL-3.0-or-later', '// Copyright (C) 2026 雪代'])], 'someone', 1],
  ];
  let failed = 0;
  for (const [name, files, author, expected] of cases) {
    const actual = check(files, author).length;
    const ok = actual === expected;
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name} (指摘 ${actual} 件、期待 ${expected} 件)`);
  }
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
