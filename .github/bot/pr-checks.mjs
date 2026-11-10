// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Pull Request のチェック (.github/workflows/core.yml・pr-expectations.yml) の補助。結果は GitHub の「Summary」に表で出す。
//   node pr-checks.mjs cases <出力ファイル>        … Pull Request の「確かめること」から、確かめる例を集める
//   node pr-checks.mjs expect <結果の JSON>        … 例の結果を表にし、外れがあれば失敗
//   node pr-checks.mjs compare <main の JSON> <Pull Request の JSON>  … 品質テストを比べ、新しく外れた例があれば失敗
//   node pr-checks.mjs impact <main の一覧> <Pull Request の一覧> [全件の出力]  … よく使う語の判定 (--judge-corpus) を比べ、変わった語を出す (失敗にはしない)
// Pull Request の本文は値として扱うだけ (コマンドとして実行しない)。
import fs from 'node:fs';
import { cell, codeCell as code } from './markdown.mjs';

const summary = text => fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY ?? '/dev/stdout', text + '\n');

function cases(output) {
  const event = JSON.parse(fs.readFileSync(process.env.GITHUB_EVENT_PATH, 'utf8'));
  const body = event.pull_request?.body ?? '';
  const list = [];

  // 「## 確かめること」の下の「入力 → 期待」の行
  const section = body.split(/^##\s*確かめること\s*$/m)[1]?.split(/^##\s/m)[0] ?? '';
  for (const line of section.split('\n')) {
    const match = line.replace(/^\s*[-*]\s*/, '').match(/^(.+?)\s*(?:→|->)\s*(.+)$/);
    if (!match || line.trim().startsWith('<!--')) continue;
    list.push({ typed: match[1].replace(/`/g, '').trim(), expected: match[2].replace(/`/g, '').trim() });
  }

  fs.writeFileSync(output, list.map(c => `${c.typed}\t${c.expected}`).join('\n') + '\n');
  fs.appendFileSync(process.env.GITHUB_OUTPUT ?? '/dev/null', `count=${list.length}\n`);
  console.log(`確かめる例: ${list.length} 個`);
}

function expect(resultFile) {
  const results = JSON.parse(fs.readFileSync(resultFile, 'utf8'));
  const failed = results.filter(r => !r.ok).length;
  summary(`## 確かめることの再現 ${failed === 0 ? '✅' : '❌'}\n`);
  summary('| | 入力 | 期待 | Pull Request の結果 | 比べ方 |\n|---|---|---|---|---|');
  results.forEach(r => summary(`| ${r.ok ? '✅' : '❌'} | ${code(r.typed)} | ${cell(r.expected)} | ${cell(r.actual)} | ${cell(r.kind)} |`));
  if (failed > 0) {
    console.error(`${failed} 個の例が期待どおりになりませんでした`);
    process.exitCode = 1;
  }
}

function compare(baseFile, prFile) {
  const pr = JSON.parse(fs.readFileSync(prFile, 'utf8'));
  let base = null;
  try { base = JSON.parse(fs.readFileSync(baseFile, 'utf8')); } catch { /* main に --eval-json がまだ無いか、main をビルドできない */ }
  const baseCases = new Map((base?.cases ?? []).map(c => [c.key, c]));
  const broken = pr.cases.filter(c => !c.ok && baseCases.get(c.key)?.ok === true);
  const fixed = pr.cases.filter(c => c.ok && baseCases.get(c.key)?.ok === false);
  const added = base ? pr.cases.filter(c => !baseCases.has(c.key)) : [];
  const addedFailing = added.filter(c => !c.ok);
  const rate = r => `${r.pass}/${r.total} (${(r.pass / r.total * 100).toFixed(1)}%)`;

  summary(`## 精度の比較 ${broken.length + addedFailing.length === 0 ? '✅' : '❌'}\n`);
  summary(`| | 品質テスト |\n|---|---|\n| main | ${base ? rate(base) : '(比べられません)'} |\n| Pull Request | ${rate(pr)} |\n`);
  const list = (title, items) => {
    if (items.length === 0) return;
    summary(`### ${title} (${items.length})\n`);
    for (const c of items.slice(0, 50)) summary(`- ${cell(c.detail)}`);
    if (items.length > 50) summary(`- …ほか ${items.length - 50} 個`);
    summary('');
  };
  list('❌ 新しく外れた例', broken);
  list('❌ 新しく足した例で外れたもの', addedFailing);
  list('✅ 直った例', fixed);
  list('➕ 新しく足した例', added.filter(c => c.ok));
  if (broken.length + addedFailing.length > 0) {
    console.error(`新しく外れた例が ${broken.length + addedFailing.length} 個あります`);
    process.exitCode = 1;
  }
}

/** 「語[Tab]見え方」の行を読む (無ければ null)。 */
function readJudged(file) {
  let text;
  try { text = fs.readFileSync(file, 'utf8'); } catch { return null; }
  const map = new Map();
  for (const line of text.split('\n')) {
    const tab = line.indexOf('\t');
    if (tab > 0) map.set(line.slice(0, tab), line.slice(tab + 1));
  }
  return map;
}

/** 変わった語の一覧。両方にある語だけを比べる (辞書に足した・消した語は数えない)。 */
function judgedChanges(base, pr) {
  const changes = [];
  for (const [word, now] of pr) {
    const before = base.get(word);
    if (before !== undefined && before !== now) changes.push({ word, before, now });
  }
  return changes.sort((x, y) => x.word.localeCompare(y.word));
}

function impact(baseFile, prFile, allFile) {
  const pr = readJudged(prFile), base = readJudged(baseFile);
  summary('## よく使う語の判定の変化 (参考)\n');
  if (!pr || !base) {
    summary('main にまだこの仕組みが無いか、main をビルドできないため、比べられません。');
    return;
  }
  const changes = judgedChanges(base, pr);
  summary(`よく使う語の読み (ローマ字) と英単語 ${pr.size} 語を 1 語ずつ打って確定し、main と比べました。`
    + '**変わった語が間違いとは限りません。** 直したかった語のほかに、意図しない語が変わっていないか確かめてください (このチェックは失敗にしません)。\n');
  if (changes.length === 0) {
    summary('判定が変わった語はありません。');
    return;
  }
  summary(`### 判定が変わった語 (${changes.length})\n`);
  summary('| 打った英字 | main | Pull Request |\n|---|---|---|');
  for (const c of changes.slice(0, 200)) summary(`| ${code(c.word)} | ${cell(c.before)} | ${cell(c.now)} |`);
  if (changes.length > 200) summary(`\n…ほか ${changes.length - 200} 語 (全件はログと、成果物の judged-words の changes.tsv にあります)`);
  // Summary に出しきれない分も見られるように、全件をログと TSV に出す
  const lines = changes.map(c => `${c.word}\t${c.before}\t${c.now}`);
  if (allFile) fs.writeFileSync(allFile, ['打った英字\tmain\tPull Request', ...lines].join('\n') + '\n');
  console.log(`判定が変わった語: ${changes.length} 語\n${lines.join('\n')}`);
}

const [mode, a, b] = process.argv.slice(2);
if (mode === 'cases') cases(a);
else if (mode === 'expect') expect(a);
else if (mode === 'compare') compare(a, b);
else if (mode === 'impact') impact(a, b, process.argv[5]);
