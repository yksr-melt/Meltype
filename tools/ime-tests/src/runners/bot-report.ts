// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 既存botのreportコマンドを、ケースごとの偽APIと再現結果ファイルで実行する。
 * 比較ロジックをここに複製せず、botが生成したコメントとラベル要求を観測する。
 */

import type { TestInfo } from '@playwright/test';
import { createServer } from 'node:http';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { BotReportCase } from '../schemas/bot-report';
import { executeProcess, requireSuccess, type ProcessEvidence } from '../process';
import { checkEqual } from '../reporting/check';
import { jsonEvidence, withEvidence } from '../reporting/evidence';

type Observation = { readonly kind: 'read-comments' } | { readonly kind: 'comment'; readonly text: string } |
  { readonly kind: 'labels'; readonly labels: readonly string[] } | { readonly kind: 'unexpected'; readonly method: string; readonly path: string };
export async function verifyBotReport(item: BotReportCase, info: TestInfo): Promise<void> {
  const requests: Observation[] = [];
  let processResult: ProcessEvidence | null = null;
  const server = createServer(async (request, response) => {
    let text = ''; for await (const chunk of request) text += chunk;
    response.setHeader('Content-Type', 'application/json');
    const method = request.method!, path = request.url!;
    try {
      if (method === 'GET' && path === '/repos/test/local/issues/1/comments?per_page=100') {
        requests.push({ kind: 'read-comments' }); response.end('[]'); return;
      }
      const value: unknown = JSON.parse(text);
      if (method === 'POST' && path === '/repos/test/local/issues/1/comments' &&
          typeof value === 'object' && value !== null && 'body' in value && typeof value.body === 'string') {
        requests.push({ kind: 'comment', text: value.body }); response.end('{}'); return;
      }
      if (method === 'POST' && path === '/repos/test/local/issues/1/labels' &&
          typeof value === 'object' && value !== null && 'labels' in value && Array.isArray(value.labels) &&
          value.labels.every((label: unknown) => typeof label === 'string')) {
        requests.push({ kind: 'labels', labels: value.labels }); response.end('{}'); return;
      }
      // 想定外のAPI要求を成功応答で隠さない。要求の種類とパスも証拠に残す。
      throw new Error('未対応の偽APIリクエスト');
    } catch {
      requests.push({ kind: 'unexpected', method, path }); response.writeHead(500); response.end('{}');
    }
  });
  const directory = await mkdtemp(join(tmpdir(), 'meltype-bot-ci-'));
  try {
    await withEvidence(info, async () => {
      // 空きポートのlocalhostへ限定し、botへ実トークンや実リポジトリを渡さない。
      await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
      const address = server.address();
      if (!address || typeof address === 'string') throw new Error('localhost APIを起動できません');
      await writeFile(join(directory, 'event.json'), '{}');
      await writeFile(join(directory, 'repro-main.json'), JSON.stringify({ engine: 'Mozc (CI fake)',
        committed: item.operation.committed, composing: item.operation.composing, converted: item.operation.converted }));
      const script = fileURLToPath(new URL('../../../../.github/bot/meltype-bot.mjs', import.meta.url));
      processResult = await executeProcess(process.execPath, [script, 'report'], 10_000, {
        GITHUB_API_URL: `http://127.0.0.1:${address.port}`, GITHUB_TOKEN: 'local-test-only',
        GITHUB_REPOSITORY: 'test/local', GITHUB_EVENT_PATH: join(directory, 'event.json'),
        GITHUB_SERVER_URL: 'https://example.invalid', GITHUB_RUN_ID: '1',
        BOT_ACTION: 'repro', BOT_ISSUE: '1', BOT_RESULTS: directory, BOT_KEYS: 'dummy', BOT_LAST: 'enter',
        BOT_EXPECTED: item.operation.expected, BOT_REPORTED: item.operation.reported, BOT_COMMENT_ID: '', BOT_JOB_RESULT: 'success',
      });
      // 部分的な出力が期待どおりでも、CLIが失敗していれば判定比較へ進まない。
      requireSuccess(processResult);
      const comments = requests.filter((request): request is Extract<Observation, { kind: 'comment' }> => request.kind === 'comment');
      checkEqual('コメント数', 1, comments.length);
      const body = comments[0]!.text;
      // botの利用者向け文言が判定の観測点。文言変更時はこの対応も見直す。
      const verdict = body.includes('期待どおり') ? 'match' : body.includes('再現しました') ? 'reproduced' : body.includes('報告とは違う結果') ? 'different' : 'unknown';
      checkEqual('判定', item.expected.verdict, verdict);
      if (item.expected.verdict === 'different') {
        checkEqual('成功文言', false, body.includes('期待どおり'));
        checkEqual('再現文言', false, body.includes('再現しました'));
      }
      checkEqual('再現済みラベル数', item.expected.verdict === 'reproduced' ? 1 : 0, requests.filter(request => request.kind === 'labels').length);
    }, () => [jsonEvidence('bot期待値と観測', { case: item, requests, process: processResult })]);
  } finally {
    server.closeAllConnections(); await new Promise<void>(resolve => server.close(() => resolve()));
    await rm(directory, { recursive: true, force: true });
  }
}
