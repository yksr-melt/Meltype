// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 収集したケースのIDを検査し、短い失敗表示と実行時ソースへの導線を作る。
 * このreporter自身の障害や実行0件も失敗にし、検査できなかった実行を成功扱いしない。
 */

import type { FullConfig, FullResult, Reporter, Suite, TestCase, TestResult } from '@playwright/test/reporter';
import { appendFileSync, cpSync, existsSync, mkdirSync, readFileSync, realpathSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';
import { parseFailure, type FailureSummary } from '../schemas/failure';

/** タイトルの予約ID部分を収集結果で検査する。手動のケース台帳は持たない。 */
export function caseKey(title: string): string {
  const match = /^\[([^\]\s]+)\] /.exec(title);
  if (!match) throw new Error(`ケースIDがありません: ${title}`);
  return match[1]!;
}
export function validateCollectedCases(items: readonly { project: string; title: string }[]): void {
  if (!items.length) throw new Error('テストが0件です');
  const seen = new Set<string>();
  for (const item of items) {
    // プロジェクト間の同一IDは許可し、同じプロジェクト内の変種はタイトルのIDで区別する。
    const key = `${item.project}:${caseKey(item.title)}`;
    if (seen.has(key)) throw new Error(`ケースIDが重複しています: ${key}`);
    seen.add(key);
  }
}
function oneLine(text: string): string { return text.replace(/\u001b\[[0-9;]*m/g, '').replace(/[\r\n]+/g, ' ').slice(0, 300); }
export function failureLines(scope: string, title: string, summary?: FailureSummary, error = '詳細を確認してください'): readonly [string, string] {
  return [`FAIL ${scope} ${oneLine(title)}`, summary
    ? `${oneLine(summary.label)}: 期待 ${oneLine(summary.expected)} / 実際 ${oneLine(summary.actual)}`
    : `エラー: ${oneLine(error)}`];
}
interface RunInfo {
  readonly revision: string;
  readonly dirty: boolean;
  readonly startedAt: string;
  readonly platform: string;
  readonly node: string;
}
export default class CompactReporter implements Reporter {
  private root = '';
  private testRoot = '';
  private sourceRoot = '';
  private failed = false;
  private passed = 0;
  private skipped = 0;
  onBegin(config: FullConfig, suite: Suite): void {
    this.root = dirname(realpathSync(config.configFile!));
    this.testRoot = realpathSync(config.rootDir);
    if (!this.failed) rmSync(join(this.root, 'artifacts/run-errors.jsonl'), { force: true });
    try {
      validateCollectedCases(suite.allTests().map(test => ({ project: test.parent.project()?.name ?? '', title: test.title })));
      const output = join(this.root, 'artifacts');
      mkdirSync(output, { recursive: true });
      // 実行中の編集に影響されないよう開始時に保存する。製品ソース全体の保存ではない。
      const source = join(output, 'source');
      this.sourceRoot = join(source, 'tests');
      rmSync(source, { recursive: true, force: true });
      mkdirSync(source, { recursive: true });
      for (const name of ['tests', 'src', 'index.html', 'playwright.config.ts', 'package.json', 'package-lock.json', 'tsconfig.json']) {
        const path = join(this.root, name);
        if (existsSync(path)) cpSync(path, join(source, name), { recursive: true });
      }
      const repositoryContext = fileURLToPath(new URL('../../', import.meta.url));
      // 一時configで実行するreporter自身のテストも、収集された元specを保存できるようにする。
      for (const test of suite.allTests()) {
        const path = relative(this.testRoot, realpathSync(test.location.file));
        if (path.startsWith('..')) throw new Error('テストソースが収集ルート外です');
        const target = join(this.sourceRoot, path);
        mkdirSync(dirname(target), { recursive: true }); cpSync(test.location.file, target);
      }
      const info: RunInfo = {
        revision: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: repositoryContext, encoding: 'utf8' }).trim(),
        dirty: execFileSync('git', ['status', '--porcelain'], { cwd: repositoryContext, encoding: 'utf8' }).trim().length > 0,
        startedAt: new Date().toISOString(), platform: process.platform, node: process.version,
      };
      writeFileSync(join(output, 'run-info.json'), JSON.stringify(info, null, 2));
      console.log(`Run ${info.revision}${info.dirty ? ' (未コミット変更あり・テストソース保存済み)' : ''}`);
      console.log('Details: artifacts/report/ · artifacts/results.json · artifacts/source/');
    } catch (error) { this.onError({ message: error instanceof Error ? error.message : String(error) }); }
  }
  onTestEnd(test: TestCase, result: TestResult): void {
    if (result.status === 'passed' && test.expectedStatus === 'passed') { this.passed++; return; }
    if (result.status === 'skipped') { this.skipped++; return; }
    this.failed = true;
    let summary: FailureSummary | undefined;
    const attachment = result.attachments.find(item => item.name === 'failure-summary');
    if (attachment) {
      try {
        const body = attachment.body ?? (attachment.path ? readFileSync(attachment.path) : undefined);
        if (body) summary = parseFailure(JSON.parse(body.toString('utf8')));
      } catch (error) {
        for (const line of failureLines('reporter', '[reporter.invalid-summary] 失敗要約', undefined, String(error))) console.log(line);
      }
    }
    const scope = relative(this.testRoot, realpathSync(test.location.file)).replace(/\\/g, '/').replace(/\.spec\.ts$/, '').replace(/^e2e\//, 'E2E/');
    const sourcePath = join(this.sourceRoot, relative(this.testRoot, realpathSync(test.location.file)));
    // 失敗後の作業ツリーではなく開始時の写しを、後続のHTML/JSON reporterへ渡す。
    if (existsSync(sourcePath)) result.attachments.push({ name: 'case-source', contentType: 'text/plain', path: sourcePath });
    const error = result.status === 'timedOut' ? 'timeout' : result.errors[0]?.message?.split('\n').find(line => line.trim()) ?? result.status;
    for (const line of failureLines(scope, test.title, summary, error)) console.log(line);
  }
  onError(error: { message?: string; value?: string; stack?: string }): void {
    this.failed = true;
    const output = join(this.root, 'artifacts');
    mkdirSync(output, { recursive: true });
    appendFileSync(join(output, 'run-errors.jsonl'), JSON.stringify(error) + '\n');
    for (const line of failureLines('実行基盤', '[run.error] テスト実行', undefined, error.message ?? error.value ?? '不明なエラー')) console.log(line);
  }
  async onEnd(result: FullResult): Promise<{ status: FullResult['status'] }> {
    if (!this.failed && this.passed === 0) this.onError({ message: '実行できたテストが0件です' });
    console.log(`${this.passed} passed, ${this.skipped} skipped`);
    return { status: this.failed ? 'failed' : result.status };
  }
}
