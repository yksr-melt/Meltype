// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void
import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const script = new URL('./cla.mjs', import.meta.url).pathname;
const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'cla-test-'));
const preload = path.join(dir, 'preload.mjs');
// fetch をインメモリの GitHub API に差し替え、書き込みを記録する
fs.writeFileSync(preload, `
import fs from 'node:fs';
const state = JSON.parse(process.env.CLA_TEST_STATE);
const json = body => ({ ok: true, status: 200, json: async () => body, text: async () => '' });
globalThis.fetch = async (url, init = {}) => {
  const u = new URL(url), route = u.pathname.replace('/repos/o/r', ''), method = init.method ?? 'GET';
  if (method !== 'GET') fs.appendFileSync(process.env.CLA_TEST_LOG, JSON.stringify({ method, route, body: JSON.parse(init.body ?? 'null') }) + '\\n');
  if (method !== 'GET') return json({});
  if (route === '/pulls') return json(state.pulls.slice((u.searchParams.get('page') - 1) * 100, u.searchParams.get('page') * 100));
  if (route.startsWith('/pulls/')) return json(state.pulls.find(p => p.number === Number(route.split('/')[2])));
  if (route === '/contents/signatures.json') return json({ sha: 's', content: Buffer.from(JSON.stringify({ signed: state.signed })).toString('base64') });
  if (route === '/branches/cla-signatures') return json({});
  if (route.endsWith('/comments')) return json([]);
  throw new Error('未対応: ' + method + ' ' + route);
};`);

const pull = (number, id, sha = 'abc', association = 'CONTRIBUTOR') => ({
  number, author_association: association, head: { sha }, user: { id, login: `user${id}`, type: 'User' },
});
const signed = id => ({ id, version: 1 });
const workflowRun = { workflow_run: { head_sha: 'abc' } };
const agree = number => ({
  issue: { number, pull_request: {} },
  comment: { id: 99, body: 'CLA に同意します', user: { id: 5, login: 'user5' }, html_url: 'u', created_at: 't' },
});

/** 本物の cla.mjs を動かし、終了コードと API の書き込みを返す。 */
function run(event, state) {
  const log = path.join(dir, `${crypto.randomUUID()}.log`), eventFile = `${log}.json`;
  fs.writeFileSync(log, '');
  fs.writeFileSync(eventFile, JSON.stringify(event));
  const result = spawnSync(process.execPath, ['--import', preload, script], {
    encoding: 'utf8',
    env: {
      ...process.env, GITHUB_REPOSITORY: 'o/r', GITHUB_TOKEN: 'job', GITHUB_EVENT_PATH: eventFile,
      CLA_TEST_STATE: JSON.stringify(state), CLA_TEST_LOG: log,
    },
  });
  const writes = fs.readFileSync(log, 'utf8').trim().split('\n').filter(Boolean).map(l => JSON.parse(l));
  return { status: result.status, writes };
}
const statuses = writes => writes.filter(w => w.route.startsWith('/statuses/')).map(w => w.body.state);
const comments = writes => writes.filter(w => /^\/issues\/\d+\/comments$/.test(w.route)).map(w => w.route);

test('同じ SHA の Pull Request に同意がまだの作者がいれば、一覧の順番によらず失敗にし、その作者にだけお願いする', () => {
  for (const pulls of [[pull(7, 5), pull(8, 6)], [pull(8, 6), pull(7, 5)]]) {
    const { status, writes } = run(workflowRun, { pulls, signed: [signed(6)] });
    assert.equal(status, 0);
    assert.deepEqual(statuses(writes), ['failure']);
    assert.deepEqual(comments(writes), ['/issues/7/comments']);
  }
});

test('同じ SHA の作者が全員同意済み・メンテナーなら成功にする。ほかの SHA の作者は見ない', () => {
  const pulls = [pull(7, 6), pull(8, 7, 'abc', 'COLLABORATOR'), pull(9, 5, 'other')];
  const { writes } = run(workflowRun, { pulls, signed: [signed(6)] });
  assert.deepEqual(statuses(writes), ['success']);
  assert.deepEqual(comments(writes), []);
});

test('開いている Pull Request の 2 ページ目も見る', () => {
  const fillers = Array.from({ length: 100 }, (_, i) => pull(100 + i, 6, 'other'));
  const { writes } = run(workflowRun, { pulls: [...fillers, pull(7, 5)], signed: [] });
  assert.deepEqual(statuses(writes), ['failure']);
});

test('同意しても、同じ SHA にほかの同意がまだの作者がいれば失敗のままにする', () => {
  const { writes } = run(agree(7), { pulls: [pull(7, 5), pull(8, 6)], signed: [] });
  assert.deepEqual(statuses(writes), ['failure']);
});

test('同意のコメントで記録し、status を成功にする', () => {
  const { writes } = run(agree(7), { pulls: [pull(7, 5)], signed: [] });
  assert.ok(writes.some(w => w.method === 'PUT' && w.route === '/contents/signatures.json'));
  assert.deepEqual(statuses(writes), ['success']);
});
