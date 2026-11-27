// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// ソースファイルの先頭のライセンス・著作権の表記を確かめるチェック (.github/workflows/lint.yml の license-header から呼ぶ)。
// Pull Request で新しく足したソースファイルの先頭に、ライセンスと Meltype Contributors の表示があるか確かめる。
// 見つけたらファイルに注釈 (annotation) を付け、Summary に直し方を出して、チェックを失敗にする。
// GitHub の API で、変更 (patch) の足された行を文字列として読むだけ (書き込みの権限は要らない)。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

/** 先頭に表記を書くソースファイル。辞書・文書・設定ファイル・画像は見ない。 */
const SourceFile = /\.(cs|swift|ps1|psm1|sh|py|cpp|c|h|hpp|m|mm|mjs|js|ts|lisp|iss)$/i;
/** 年は追加した年。名前はプロジェクト内で共通。 */
const ProjectCopyright = /^Copyright \(C\) \d{4} Meltype Contributors$/;
/** 先頭の何行の中に表記があればよいか (shebang やエンコーディングの行の後に書くこともあるので少し余裕を持たせる) */
const HeaderLines = 8;

async function gh(method, url, body) {
  const response = await fetch(url.startsWith('http') ? url : `${api}/repos/${repo}${url}`, {
    method,
    headers: { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-bot' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error(`${method} ${url}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

/** Pull Request で変わったファイル (patch つき)。 */
async function changedFiles(number) {
  const files = [];
  for (let page = 1; page <= 30; page++) {
    const batch = await gh('GET', `/pulls/${number}/files?per_page=100&page=${page}`) ?? [];
    files.push(...batch);
    if (batch.length < 100) break;
  }
  return files;
}

/** patch の足された行 (先頭の + を外す)。 */
const addedLines = patch => (patch ?? '').split('\n').filter(l => l.startsWith('+') && !l.startsWith('+++')).map(l => l.slice(1));

/** コードスパン。ファイル名・著作権の行は PR の作者が書けるので、バッククォートで抜けられないようにする (meltype-bot.mjs と同じ)。 */
const code = text => '`' + String(text ?? '').replace(/`/g, 'ˋ') + '`';

const commentText = line => line.replace(/^[\s#/;*!<>-]*(rem\s+)?/i, '').replace(/\s*(?:\*\/|-->)$/, '').trim();

function check(files) {
  const findings = [];
  for (const file of files) {
    // patch が大きすぎると GitHub は patch を返さない。そのときは見ない
    if (file.status !== 'added' || !file.patch || !SourceFile.test(file.filename)) continue;
    const head = addedLines(file.patch).slice(0, HeaderLines).map(commentText);
    const problems = [];
    if (!head.includes('SPDX-License-Identifier: GPL-3.0-or-later')) problems.push('`SPDX-License-Identifier: GPL-3.0-or-later` がありません');
    if (!head.some(line => ProjectCopyright.test(line))) problems.push('`Copyright (C) <年> Meltype Contributors` がありません');
    if (problems.length) findings.push({ file: file.filename, problems });
  }
  return findings;
}

function message(findings, author) {
  const list = findings.map(f => `- ${code(f.file)}\n${f.problems.map(p => `  - ${p}`).join('\n')}`).join('\n');
  return [
    `@${author} さん、ソースファイルの先頭のライセンス・著作権の表記を確認させてください。`,
    '',
    list,
    '',
    '新しく作ったソースファイルの先頭には、次の 2 行を入れてください (コメントの記号はファイルに合わせてください)。',
    'ソースファイルの表示は Meltype Contributors にそろえます。',
    '',
    '```',
    '// SPDX-License-Identifier: GPL-3.0-or-later',
    '// Copyright (C) 2026 Meltype Contributors',
    '```',
    '',
    '直して push すると、このチェックが通ります。',
  ].join('\n');
}

/** ワークフローのコマンドの値・プロパティに入れられるようにする (https://github.com/actions/toolkit/blob/main/packages/core/src/command.ts)。 */
const escapeData = text => text.replace(/%/g, '%25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
const escapeProperty = text => escapeData(text).replace(/:/g, '%3A').replace(/,/g, '%2C');

async function main() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const pr = event.pull_request;
  const author = pr.user.login;
  const findings = check(await changedFiles(pr.number));
  if (!findings.length) return;
  for (const f of findings) {
    for (const problem of f.problems) console.log(`::error file=${escapeProperty(f.file)},line=1::${escapeData(problem)}`);
  }
  fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY ?? '/dev/stdout', message(findings, author) + '\n');
  process.exitCode = 1;
}

/** node .github/bot/license-header.mjs --self-test: 判定だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  const file = (filename, status, lines) => ({ filename, status, patch: lines.map(l => `+${l}`).join('\n') });
  const license = '// SPDX-License-Identifier: GPL-3.0-or-later';
  const copyright = '// Copyright (C) 2026 Meltype Contributors';
  const cases = [
    ['共通の表記', file('a.cs', 'added', [license, copyright]), 0],
    ['個人名の表記', file('a.cs', 'added', [license, '// Copyright (C) 2026 someone']), 1],
    ['表記が無い', file('b.sh', 'added', ['#!/bin/bash', 'echo hi']), 1],
    ['shebang の後に表記', file('b.sh', 'added', ['#!/bin/bash', '# SPDX-License-Identifier: GPL-3.0-or-later', '# Copyright (C) 2026 Meltype Contributors']), 0],
    ['ブロックコメントの表記', file('a.css.js', 'added', ['/* SPDX-License-Identifier: GPL-3.0-or-later */', '/* Copyright (C) 2027 Meltype Contributors */']), 0],
    ['ソースでないファイル', file('docs/a.md', 'added', ['# title']), 0],
    ['既存のファイルを編集', file('a.cs', 'modified', ['// 別の行']), 0],
  ];
  let failed = 0;
  for (const [name, input, expected] of cases) {
    const actual = check([input]).length;
    const ok = actual === expected;
    if (!ok) failed++;
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name} (指摘 ${actual} 件、期待 ${expected} 件)`);
  }
  if (failed) process.exit(1);
}

if (process.argv[2] === '--self-test') selfTest();
else await main();
