// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { mkdtemp, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const bot = fileURLToPath(new URL('./cla.mjs', import.meta.url));
const label = 'CLA確認済み';
const sha = '1234567890abcdef1234567890abcdef12345678';
const makePr = (number, id, login, association = 'CONTRIBUTOR') => ({
  number, user: { id, login, type: 'User' }, author_association: association, head: { sha },
});

// 実際の bot を別プロセスで動かし、GitHub API だけを置き換える。
async function runBot(state, event) {
  const dir = await mkdtemp(join(tmpdir(), 'meltype-cla-'));
  const eventPath = join(dir, 'event.json');
  await writeFile(eventPath, JSON.stringify(event));
  const server = createServer(async (req, res) => {
    const path = decodeURIComponent(new URL(req.url, 'http://localhost').pathname).replace('/repos/test/repo', '');
    let data = '';
    for await (const chunk of req) data += chunk;
    const body = data ? JSON.parse(data) : undefined;
    const reply = (status, value) => {
      res.writeHead(status, { 'content-type': 'application/json' });
      res.end(JSON.stringify(value));
    };
    const issue = path.match(/^\/issues\/(\d+)\/labels(?:\/(.+))?$/);
    if (req.method === 'GET' && path === '/contents/signatures.json') {
      return reply(200, { content: Buffer.from(JSON.stringify({ signed: state.signed })).toString('base64'), sha: 'file-sha' });
    }
    if (req.method === 'PUT' && path === '/contents/signatures.json') {
      state.signed = JSON.parse(Buffer.from(body.content, 'base64').toString()).signed;
      return reply(200, {});
    }
    if (req.method === 'GET' && path === '/branches/cla-signatures') return reply(200, {});
    if (req.method === 'GET' && path === `/labels/${label}`) return reply(state.labelExists ? 200 : 404, {});
    if (req.method === 'POST' && path === '/labels') {
      state.labelExists = true;
      return reply(201, body);
    }
    if (issue && req.method === 'POST') {
      if (state.failLabelAdd) return reply(403, { message: 'denied' });
      state.labels[issue[1]] = [...new Set([...(state.labels[issue[1]] ?? []), ...body.labels])];
      return reply(200, state.labels[issue[1]].map(name => ({ name })));
    }
    if (issue && req.method === 'DELETE') {
      state.labels[issue[1]] = (state.labels[issue[1]] ?? []).filter(name => name !== issue[2]);
      return reply(200, {});
    }
    if (req.method === 'POST' && path.startsWith('/statuses/')) {
      state.statuses.push(body);
      return reply(201, {});
    }
    if (req.method === 'GET' && path.startsWith('/pulls/')) return reply(200, state.prs[path.split('/')[2]]);
    if (path.endsWith('/comments') && req.method === 'GET') return reply(200, []);
    if (path.endsWith('/comments') && req.method === 'POST') return reply(201, {});
    if (path.endsWith('/reactions') && req.method === 'POST') return reply(201, {});
    return reply(500, { message: `unexpected ${req.method} ${path}` });
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  try {
    const child = spawn(process.execPath, [bot], { env: {
      ...process.env,
      GITHUB_API_URL: `http://127.0.0.1:${server.address().port}`,
      GITHUB_REPOSITORY: 'test/repo', GITHUB_TOKEN: 'test', GITHUB_EVENT_PATH: eventPath,
    } });
    let stderr = '';
    child.stderr.on('data', chunk => { stderr += chunk; });
    const exit = await new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', resolve);
    });
    return { exit, stderr };
  } finally {
    await new Promise(resolve => server.close(resolve));
    await rm(dir, { recursive: true, force: true });
  }
}

const makeState = () => ({ signed: [], labels: {}, statuses: [], prs: {}, labelExists: false });

test('同じ SHA でも PR ごとに元の作者の同意を確認する', async () => {
  const state = makeState();
  state.signed.push({ id: 1, version: 1 });
  state.labels[2] = [label]; // 古い確認印が残っていても未同意なら外す
  assert.equal((await runBot(state, { pull_request: makePr(1, 1, 'signed') })).exit, 0);
  assert.equal((await runBot(state, { pull_request: makePr(2, 2, 'unsigned') })).exit, 0);
  assert.deepEqual(state.labels[1], [label]);
  assert.deepEqual(state.labels[2], []);
  assert.deepEqual(state.statuses.map(s => s.state), ['success', 'failure']);
});

test('本人以外のコメントでは確認せず、本人の同意を記録して印を付ける', async () => {
  const state = makeState();
  state.prs[3] = makePr(3, 3, 'author');
  const comment = id => ({ issue: { number: 3, pull_request: {} }, comment: {
    id: 7, user: { id, login: id === 3 ? 'author' : 'other' }, body: 'CLA に同意します',
    html_url: 'https://example.com/comment/7', created_at: '2026-11-03T00:00:00Z',
  } });
  assert.equal((await runBot(state, comment(4))).exit, 0);
  assert.deepEqual(state.labels, {});
  assert.equal((await runBot(state, comment(3))).exit, 0);
  assert.deepEqual(state.labels[3], [label]);
  assert.equal(state.signed[0].id, 3);
  assert.equal(state.statuses.at(-1).state, 'success');
});

test('既存方針のメンテナー免除を維持し、ラベルの付与失敗は成功扱いにしない', async () => {
  const state = makeState();
  state.failLabelAdd = true;
  const result = await runBot(state, { pull_request: makePr(4, 4, 'maintainer', 'MEMBER') });
  assert.equal(result.exit, 1);
  assert.match(result.stderr, /403/);
  assert.equal(state.labels[4], undefined);
  state.failLabelAdd = false;
  assert.equal((await runBot(state, { pull_request: makePr(4, 4, 'maintainer', 'MEMBER') })).exit, 0);
  assert.deepEqual(state.labels[4], [label]);
});
