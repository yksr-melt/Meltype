// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// C# のフォーマットの検査 (.github/workflows/quality.yml から呼ぶ。issue #1083)。
//   node tools/check-format.mjs <比べる元の commit>   (例: origin/main)
// 元の commit から変わった .cs だけを dotnet format whitespace で調べ、Pull Request で変えた行の違反だけを失敗にする。
// 変えていない行の違反 (前からあるもの) は数だけ出す。自動では直さない (直すときは dotnet format whitespace --folder --include <ファイル>)。
import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const base = process.argv[2];
if (!base) {
  console.error('使い方: node tools/check-format.mjs <比べる元の commit>');
  process.exit(2);
}
const git = (...args) => execFileSync('git', args, { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });

const files = git('diff', '--name-only', '--diff-filter=AMR', `${base}...HEAD`, '--', '*.cs').split('\n').filter(Boolean);
if (files.length === 0) {
  console.log('変わった .cs はありません');
  process.exit(0);
}

// Pull Request で足した・変えた行 (新しい側の行番号)
const changed = new Map(files.map(f => [f, new Set()]));
let current = null;
for (const line of git('diff', '-U0', '--no-color', `${base}...HEAD`, '--', ...files).split('\n')) {
  if (line.startsWith('+++ ')) current = line === '+++ /dev/null' ? null : line.slice(6);
  const hunk = line.match(/^@@ -\S+ \+(\d+)(?:,(\d+))? @@/);
  if (hunk && changed.has(current)) {
    const start = Number(hunk[1]);
    const count = hunk[2] === undefined ? 1 : Number(hunk[2]);
    for (let i = 0; i < count; i++) changed.get(current).add(start + i);
  }
}

const result = spawnSync('dotnet', ['format', 'whitespace', '--folder', '--verify-no-changes', '--include', ...files], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
if (result.error) throw result.error;
const root = process.cwd();
const added = [];
let existing = 0;
for (const line of `${result.stdout}\n${result.stderr}`.split('\n')) {
  const m = line.match(/^(.+?)\((\d+),(\d+)\): (?:error|warning) WHITESPACE: (.*?)(?: \[.*\])?$/);
  if (!m) continue;
  const file = path.relative(root, m[1]).split(path.sep).join('/');
  const row = Number(m[2]);
  if (changed.get(file)?.has(row)) added.push({ file, row, col: Number(m[3]), message: m[4] });
  else existing++;
}
if (result.status !== 0 && added.length === 0 && existing === 0) {
  console.error(result.stdout, result.stderr);
  throw new Error(`dotnet format が失敗しました (終了コード ${result.status})`);
}

for (const a of added) console.log(`::error file=${a.file},line=${a.row},col=${a.col},title=フォーマット::${a.message}`);
const summary = [
  '## C# のフォーマット',
  '',
  `調べたファイル: ${files.length} / 変えた行の違反: **${added.length}** / 前からある違反 (失敗にしない): ${existing}`,
  '',
];
if (added.length > 0) {
  summary.push('| ファイル | 行 | 内容 |', '|---|---|---|', ...added.map(a => `| \`${a.file}\` | ${a.row} | ${a.message.replace(/[\\|]/g, '\\$&')} |`), '');
  summary.push('直すには `dotnet format whitespace --folder --include <ファイル>` を流し、変えた行の分だけ残してください。');
}
console.log(summary.join('\n'));
if (process.env.GITHUB_STEP_SUMMARY) fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary.join('\n') + '\n');
if (added.length > 0) process.exitCode = 1;
