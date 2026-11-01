// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
import test from 'node:test';
import assert from 'node:assert/strict';
import { githubApi, linkedIssues, mismatched, runAssigneeCheck } from './assignee.mjs';

const bot = { login: 'github-actions[bot]', type: 'Bot' };
const repository = 'yksr-melt/Meltype';
const issue = (number, assignees = []) => ({ number, assignees: assignees.map(login => ({ login })) });

/** インメモリの GitHub API。Draft にしたか・コメントを書いたかを記録する。 */
function fixture(issues, pr = {}) {
  const records = new Map(issues.map(i => [i.number, structuredClone(i)]));
  const comments = [];
  const writes = [];
  const pull = {
    number: 50, state: 'open', draft: false, node_id: 'PR_50', author_association: 'CONTRIBUTOR',
    user: { login: 'bob', type: 'User' }, body: 'Fixes #1', ...pr,
  };
  const api = {
    async request(method, path, body) {
      if (method !== 'GET') writes.push({ method, path, body });
      if (method === 'GET' && path.startsWith('/issues/50/comments')) return structuredClone(comments);
      if (method === 'GET' && path === '/pulls/50') return structuredClone(pull);
      const issueMatch = path.match(/^\/issues\/(\d+)$/);
      if (method === 'GET' && issueMatch) return structuredClone(records.get(+issueMatch[1]) ?? null);
      if (method === 'POST' && path === '/issues/50/comments') { comments.push({ id: comments.length + 1, user: bot, body: body.body }); return {}; }
      const edit = path.match(/^\/issues\/comments\/(\d+)$/);
      if (method === 'PATCH' && edit) { comments.find(c => c.id === +edit[1]).body = body.body; return {}; }
      throw new Error(`未対応: ${method} ${path}`);
    },
    async list(path) {
      if (path === '/pulls?state=open') return pull.state === 'open' ? [structuredClone(pull)] : [];
      throw new Error(`未対応の一覧: ${path}`);
    },
    async graphql(query, variables) {
      assert.match(query, /convertPullRequestToDraft/);
      assert.equal(variables.id, 'PR_50');
      writes.push({ method: 'GRAPHQL' });
      pull.draft = true;
      return {};
    },
  };
  return {
    api, comments, writes, pull,
    // PR のイベントで確かめた結果 (確かめなければ 'skip')
    run: async (action = 'opened', extra = {}) =>
      (await runAssigneeCheck(api, { action, pull_request: structuredClone(pull), ...extra }, 'pull_request_target', repository))[50] ?? 'skip',
  };
}

test('本文の Fixes / Closes / Resolves から閉じる Issue を集め、テンプレートの説明の中は読まない', () => {
  assert.deepEqual(linkedIssues('Fixes #1\nfixed #2, closes: #3\nResolves #1\n#4 も見た\n<!-- 「Fixes #123」と書いてください -->'), [1, 2, 3]);
  assert.deepEqual(linkedIssues(''), []);
  assert.deepEqual(linkedIssues('<!-<!-- x -->- Fixes #5 -->Fixes #6'), [6], '入れ子のコメントの中も読まない');
});

test('このリポジトリの Issue の URL・owner/name#123 も読み、ほかのリポジトリのものは読まない', () => {
  const body = 'Fixes https://github.com/yksr-melt/Meltype/issues/1208\ncloses yksr-melt/meltype#5\n'
    + 'Fixes https://github.com/other/repo/issues/9\nfixes other/repo#8';
  assert.deepEqual(linkedIssues(body, repository), [1208, 5]);
});

test('担当のいる Issue で、作者が担当に入っていないものだけを返す (大文字・小文字は区別しない)', () => {
  const issues = [issue(1, ['alice']), issue(2, ['Bob']), issue(3)];
  assert.deepEqual(mismatched('bob', issues).map(i => i.number), [1]);
});

test('担当でない人の PR は Draft にして、理由と担当をコメントする', async () => {
  const f = fixture([issue(1, ['alice'])]);
  assert.equal(await f.run(), 'draft');
  assert.equal(f.pull.draft, true);
  assert.equal(f.comments.length, 1);
  assert.match(f.comments[0].body, /#1 の担当: `alice`/);
  assert.match(f.comments[0].body, /Ready for review/);
});

for (const [name, issues, body] of [
  ['担当の PR', [issue(1, ['alice', 'bob'])], 'Fixes #1'],
  ['担当のいない Issue', [issue(1)], 'Fixes #1'],
  ['関連 Issue が書かれていない PR', [issue(1, ['alice'])], '直しました (#1 を参照)'],
  ['無い Issue を指す PR', [], 'Fixes #9'],
]) {
  test(`${name}は、そのままにする`, async () => {
    const f = fixture(issues, { body });
    assert.equal(await f.run(), 'ok');
    assert.equal(f.pull.draft, false);
    assert.equal(f.writes.length, 0);
  });
}

test('関連 Issue が複数あれば、どれか 1 つでも担当でなければ Draft にする', async () => {
  const f = fixture([issue(1, ['bob']), issue(2, ['alice'])], { body: 'Fixes #1\nFixes #2' });
  assert.equal(await f.run(), 'draft');
  assert.doesNotMatch(f.comments[0].body, /#1 の担当/);
  assert.match(f.comments[0].body, /#2 の担当/);
});

test('メンテナー・bot の PR と、Draft の PR は対象にしない', async () => {
  for (const pr of [{ author_association: 'OWNER' }, { author_association: 'COLLABORATOR' }, { user: { login: 'dependabot[bot]', type: 'Bot' } }, { draft: true }]) {
    const f = fixture([issue(1, ['alice'])], pr);
    assert.equal(await f.run(), 'skip');
    assert.equal(f.writes.length, 0);
  }
});

test('Ready for review に戻しても担当でなければ、また Draft にし、コメントは書き換えて増やさない', async () => {
  const f = fixture([issue(1, ['alice'])]);
  await f.run();
  f.pull.draft = false;
  assert.equal(await f.run('ready_for_review'), 'draft');
  assert.equal(f.comments.length, 1);
  assert.equal(f.writes.filter(w => w.method === 'GRAPHQL').length, 2);
});

test('本文を書き換えたときだけ確かめ直す (タイトルだけの変更・同期では何もしない)', async () => {
  const f = fixture([issue(1, ['alice'])]);
  assert.equal(await f.run('edited', { changes: { title: { from: '前' } } }), 'skip');
  assert.equal(await f.run('synchronize'), 'skip');
  assert.equal(await f.run('edited', { changes: { body: { from: '' } } }), 'draft');
});

test('イベントの時点ではなく、確かめる直前の本文で判定する', async () => {
  // イベントでは他人の担当の #1 だったが、job が動くまでに自分の担当の #2 に書き換えた
  const f = fixture([issue(1, ['alice']), issue(2, ['bob'])]);
  const event = { action: 'edited', changes: { body: { from: '' } }, pull_request: structuredClone(f.pull) };
  f.pull.body = 'Fixes #2';
  assert.equal((await runAssigneeCheck(f.api, event, 'pull_request_target', repository))[50], 'ok');
  assert.equal(f.writes.length, 0);
  // 閉じた・Draft にした PR も、今の状態で見る
  f.pull.body = 'Fixes #1'; f.pull.state = 'closed';
  assert.equal((await runAssigneeCheck(f.api, event, 'pull_request_target', repository))[50], 'skip');
});

test('定期実行では、開いているすべての PR を確かめる (イベントの job が取り消されても拾う)', async () => {
  const f = fixture([issue(1, ['alice'])]);
  assert.deepEqual(await runAssigneeCheck(f.api, {}, 'schedule', repository), { 50: 'draft' });
  assert.equal(f.pull.draft, true);
  assert.deepEqual(await runAssigneeCheck(f.api, {}, 'schedule', repository), { 50: 'skip' }, 'Draft になった PR はもう見ない');
  assert.equal(f.comments.length, 1);
});

test('PR の一覧は 2 ページ目以降も読む。API エラーなら欠けた一覧で続けない', async () => {
  const calls = [];
  const api = githubApi({ token: 'test', repository, fetcher: async url => {
    calls.push(url);
    const page = Number(new URL(url).searchParams.get('page'));
    return { ok: true, status: 200, json: async () => page === 1 ? Array.from({ length: 100 }, (_, i) => ({ number: i })) : [{ number: 100 }] };
  } });
  assert.equal((await api.list('/pulls?state=open')).length, 101);
  assert.equal(calls.length, 2);
  const failing = githubApi({ token: 'test', repository, fetcher: async () => ({ ok: false, status: 403 }) });
  await assert.rejects(failing.list('/pulls?state=open'), /403/);
});
