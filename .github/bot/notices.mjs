// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// ソースファイルの先頭のライセンス・著作権の表記を確かめる bot (.github/workflows/notices.yml から呼ぶ)。
//   - Pull Request で新しく足したソースファイルに `SPDX-License-Identifier` と `Copyright (C) <年> <名前>` があるか
//   - 作者 (yksr-melt) 以外の人が、Yukishiro (雪代) の名義で著作権の行を書いていないか
//     (貢献の著作権は貢献した人に残る: CONTRIBUTING.md の CLA。自分の名前で書いてもらう)
// 見つけたら Pull Request にコメントする (直ったらコメントを「直りました」に書き換える)。
// Pull Request のコードは動かさない。GitHub の API で、変更 (patch) の足された行を文字列として読むだけ。
import fs from 'node:fs';

const api = process.env.GITHUB_API_URL ?? 'https://api.github.com';
const repo = process.env.GITHUB_REPOSITORY;
const token = process.env.GITHUB_TOKEN;

const Marker = '<!-- meltype-bot --><!-- notices -->';
/** Yukishiro 名義で書いてよい人 (作者)。 */
const Owners = ['yksr-melt'];
/** 先頭に表記を書くソースファイル。辞書・文書・設定ファイル・画像は見ない。 */
const SourceFile = /\.(cs|swift|ps1|psm1|sh|py|cpp|c|h|hpp|m|mm|mjs|js|ts|lisp|iss)$/i;
/** Yukishiro の名義 */
const OwnerName = /yukishiro|雪代|ゆきしろ|yksr/i;
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

const copyrightOf = line => /copyright\s*(\(c\)|©)?\s*\d{4}/i.test(line) ? line.replace(/^[\s#/;*!<>-]*(rem\s+)?/i, '').trim() : null;

function check(files, author) {
  const findings = [];
  const isOwner = Owners.some(o => o.toLowerCase() === author.toLowerCase());
  for (const file of files) {
    if (!SourceFile.test(file.filename)) continue;
    const added = addedLines(file.patch);
    if (file.status === 'added') {
      // patch が大きすぎると GitHub は patch を返さない。そのときは見ない
      if (!file.patch) continue;
      const head = added.slice(0, HeaderLines);
      const problems = [];
      if (!head.some(l => l.includes('SPDX-License-Identifier'))) problems.push('`SPDX-License-Identifier: GPL-3.0-or-later` がありません');
      const copyright = head.map(copyrightOf).find(c => c);
      if (!copyright) problems.push('`Copyright (C) <年> <あなたの名前>` がありません');
      else if (!isOwner && OwnerName.test(copyright)) problems.push(`著作権の行が作者の名義になっています (${code(copyright)})。あなたの名前にしてください`);
      if (problems.length) findings.push({ file: file.filename, problems });
    }
    else if (!isOwner) {
      // 既存のファイルに、作者の名義の著作権の行を足していないか
      const copyright = added.map(copyrightOf).find(c => c && OwnerName.test(c));
      if (copyright) findings.push({ file: file.filename, problems: [`作者の名義の著作権の行を足しています (${code(copyright)})。あなたが書いた部分なら、あなたの名前の行を足してください`] });
    }
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

async function upsert(number, body) {
  const comments = await gh('GET', `/issues/${number}/comments?per_page=100`) ?? [];
  const mine = comments.find(c => c.user?.type === 'Bot' && c.body?.includes(Marker));
  if (!body) {
    // 直った: 前に指摘していたら、直ったことを書く (指摘が無かったなら何もしない)
    if (mine && !mine.body.includes('✅')) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n✅ ライセンス・著作権の表記を確認しました。ありがとうございます。` });
    return;
  }
  if (mine) await gh('PATCH', `/issues/comments/${mine.id}`, { body: `${Marker}\n${body}` });
  else await gh('POST', `/issues/${number}/comments`, { body: `${Marker}\n${body}` });
}

async function main() {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const pr = event.pull_request;
  const author = pr.user.login;
  const findings = check(await changedFiles(pr.number), author);
  for (const f of findings) console.log(`${f.file}: ${f.problems.join(' / ')}`);
  await upsert(pr.number, findings.length ? message(findings, author) : null);
}

/** node .github/bot/notices.mjs --self-test: 判定だけを確かめる (GitHub には何も書かない)。 */
function selfTest() {
  const file = (filename, status, lines) => ({ filename, status, patch: lines.map(l => `+${l}`).join('\n') });
  const cases = [
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
