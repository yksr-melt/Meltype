// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: テスト失敗を識別できる短い表示と、詳細を追える証拠保存の契約を検査する。
 * 検証範囲: 収集結果の検査、reporterの出力、終了状態、保存された成果物。
 * 制約: 意図的に失敗するテストは一時ディレクトリの子プロセスで実行し、本体のテスト収集から隔離する。
 */

import { test, expect } from '@playwright/test';
import { mkdtemp, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { executeProcess } from '../../src/process';
import { failureLines, validateCollectedCases } from '../../src/reporting/compact-reporter';
import { jsonEvidence, withEvidence } from '../../src/reporting/evidence';

test('[reporter.identifiers] 収集されたIDの重複・空・未実行を拒否する', () => {
  expect(() => validateCollectedCases([{ project: 'dom', title: '[D03/textarea] Enter' }, { project: 'dom', title: '[D03/contenteditable] Enter' }])).not.toThrow();
  expect(() => validateCollectedCases([{ project: 'dom', title: '[D03] A' }, { project: 'dom', title: '[D03] B' }])).toThrow('重複');
  expect(() => validateCollectedCases([{ project: 'dom', title: 'missing' }])).toThrow();
  expect(() => validateCollectedCases([])).toThrow();
});
test('[reporter.two-lines] ケースと差分だけを2行に表示する', () => {
  expect(failureLines('regression/bot-report', '[B01] 英字が同じでもかなが異なる',
    { label: '判定', expected: '"different"', actual: '"match"' })).toEqual([
    'FAIL regression/bot-report [B01] 英字が同じでもかなが異なる',
    '判定: 期待 "different" / 実際 "match"',
  ]);
  expect(failureLines('unit', '[fixture] value', { label: '値', expected: '1\n2', actual: '3\n4' }).join('\n').split('\n')).toHaveLength(2);
});

// 意図的に落とすfixtureは一時フォルダだけに作り、通常のケース収集へ混ぜない。
test('[reporter.integration] 複数失敗・変種・timeoutを2行で表示し詳細とソースを保存する', async ({}, info) => {
  const directory = await mkdtemp(join(tmpdir(), 'meltype-reporter-'));
  const packageUrl = new URL('../../node_modules/@playwright/test/index.mjs', import.meta.url).href;
  const checkPath = fileURLToPath(new URL('../../src/reporting/check.ts', import.meta.url));
  const evidencePath = fileURLToPath(new URL('../../src/reporting/evidence.ts', import.meta.url));
  const reporterPath = fileURLToPath(new URL('../../src/reporting/compact-reporter.ts', import.meta.url));
  let stdout = '';
  try {
    await withEvidence(info, async () => {
      await writeFile(join(directory, 'package.json'), '{"type":"module"}');
      await writeFile(join(directory, 'failure.fixture.ts'), `
import { test } from ${JSON.stringify(packageUrl)};
import { checkEqual } from ${JSON.stringify(checkPath)};
import { withEvidence, jsonEvidence } from ${JSON.stringify(evidencePath)};
for (const field of ['textarea', 'contenteditable'])
  test('[fixture.compare/' + field + '] 値の比較', async ({}, info) => {
    await withEvidence(info, async () => { checkEqual('値', 1, 2); }, () => [jsonEvidence('観測', {value: 2})]);
  });
test('[fixture.timeout] 待機', async () => { await new Promise(() => {}); });
`);
      await writeFile(join(directory, 'playwright.config.ts'), `export default {
        testDir: '.', testMatch: '*.fixture.ts', workers: 1, retries: 0, timeout: 400,
        reporter: [[${JSON.stringify(reporterPath)}], ['json', {outputFile: 'artifacts/results.json'}]],
        outputDir: 'artifacts/results',
      };`);
      // Reporterの相対成果物はconfig基準、標準JSONはcwd基準なのでcwdを固定した子プロセスで起動する。
      const cli = fileURLToPath(new URL('../../node_modules/playwright/cli.js', import.meta.url));
      const script = `process.chdir(${JSON.stringify(directory)}); process.argv = ['node', ${JSON.stringify(cli)}, 'test', '--config', ${JSON.stringify(join(directory, 'playwright.config.ts'))}]; require(${JSON.stringify(cli)});`;
      const result = await executeProcess(process.execPath, ['-e', script]); stdout = result.stdout;
      // 子テストの失敗はこのテストの入力。終了コードだけでなく、表示と成果物も検査する。
      expect(result.exitCode).toBe(1);
      const lines = stdout.split('\n');
      const failures = lines.flatMap((line, index) => line.startsWith('FAIL ') ? [lines.slice(index, index + 2)] : []);
      expect(failures).toHaveLength(3);
      expect(failures[0]![0]).toContain('[fixture.compare/textarea]');
      expect(failures[1]![0]).toContain('[fixture.compare/contenteditable]');
      expect(failures[0]![1]).toBe('値: 期待 1 / 実際 2');
      expect(failures[2]![1]).toBe('エラー: timeout');
      expect(stdout).not.toContain(' at ');
      const json: unknown = JSON.parse(await readFile(join(directory, 'artifacts/results.json'), 'utf8'));
      expect(json).toHaveProperty('stats.unexpected', 3);
      const snapshot = await readFile(join(directory, 'artifacts/source/tests/failure.fixture.ts'), 'utf8');
      expect(snapshot).toContain('[fixture.compare/');
      const serialized = JSON.stringify(json);
      expect(serialized).toContain('failure-summary'); expect(serialized).toContain('観測');
    }, () => [jsonEvidence('子reporterの出力', { stdout })]);
  } finally { await rm(directory, { recursive: true, force: true }); }
});

test('[reporter.collection-error] 収集エラーを非0で報告し詳細を保存する', async ({}, info) => {
  const directory = await mkdtemp(join(tmpdir(), 'meltype-collection-'));
  let stdout = '';
  try {
    await withEvidence(info, async () => {
      await writeFile(join(directory, 'broken.fixture.ts'), 'export const broken = ;');
      const reporter = fileURLToPath(new URL('../../src/reporting/compact-reporter.ts', import.meta.url));
      await writeFile(join(directory, 'playwright.config.ts'), `export default {
        testDir: '.', testMatch: '*.fixture.ts', reporter: [[${JSON.stringify(reporter)}]],
      };`);
      const cli = fileURLToPath(new URL('../../node_modules/playwright/cli.js', import.meta.url));
      const script = `process.chdir(${JSON.stringify(directory)}); process.argv = ['node', ${JSON.stringify(cli)}, 'test', '--config', ${JSON.stringify(join(directory, 'playwright.config.ts'))}]; require(${JSON.stringify(cli)});`;
      const result = await executeProcess(process.execPath, ['-e', script]); stdout = result.stdout;
      expect(result.exitCode).toBe(1); expect(stdout).toContain('FAIL 実行基盤');
      expect(await readFile(join(directory, 'artifacts/run-errors.jsonl'), 'utf8')).toContain('broken.fixture.ts');
    }, () => [jsonEvidence('収集エラーの出力', { stdout })]);
  } finally { await rm(directory, { recursive: true, force: true }); }
});
