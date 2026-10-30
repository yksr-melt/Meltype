// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt
import test from 'node:test';
import assert from 'node:assert/strict';
import { decision, excessPulls, githubApi, Labels, runPolicy, WaitMs } from './policy.mjs';

const start = Date.parse('2026-10-25T00:00:00Z');
const user = login => ({ login, type: 'User' });
const bot = { login: 'github-actions[bot]', type: 'Bot' };
const issue = (number, labels = [], author = 'alice') => ({
  number, state: 'open', user: user(author), body: '報告', labels: labels.map(name => ({ name })),
  created_at: new Date(start - WaitMs * 2 + number * 1000).toISOString(),
});
const pull = (number, draft = false, author = 'alice', association = 'NONE') => ({
  ...issue(number, [], author), author_association: association, draft, head: { sha: 'original' }, pull_request: {}, node_id: `PR_${number}`,
});

/** インメモリの GitHub API。API の書き込みまで通して、閉じる対象とタイミングを確かめる。 */
function fixture(items) {
  const records = new Map(items.map(i => [i.number, structuredClone(i)]));
  const comments = new Map(items.map(i => [i.number, []]));
  const events = new Map(items.map(i => [i.number, []]));
  const reviewComments = new Map(items.map(i => [i.number, []]));
  const reviews = new Map(items.map(i => [i.number, []]));
  const writes = [];
  let serial = 1, now = start;
  const api = {
    async graphql(query, variables) {
      writes.push({ method: 'GRAPHQL', query, variables });
      const item = [...records.values()].find(i => i.node_id === variables.id);
      if (!item || !query.includes('convertPullRequestToDraft')) throw new Error('未対応の GraphQL');
      item.draft = true; addEvent(item.number, 'converted_to_draft');
      return { convertPullRequestToDraft: { pullRequest: { isDraft: true } } };
    },
    async list(path) {
      if (path.startsWith('/pulls?')) return structuredClone([...records.values()].filter(i => i.pull_request && i.state === 'open'));
      if (path === '/labels') return Object.values(Labels).map(name => ({ name }));
      if (path.startsWith('/issues?')) return structuredClone([...records.values()].filter(i => i.state === 'open' && i.labels.some(l => l.name === Labels.info)));
      const reviewMatch = path.match(/^\/pulls\/(\d+)\/(comments|reviews)$/);
      if (reviewMatch) return structuredClone((reviewMatch[2] === 'reviews' ? reviews : reviewComments).get(+reviewMatch[1]));
      const [, n, collection] = path.match(/^\/issues\/(\d+)\/(comments|events)$/) ?? [];
      if (!n) throw new Error(`未対応の一覧: ${path}`);
      return structuredClone((collection === 'comments' ? comments : events).get(+n));
    },
    async request(method, path, body) {
      if (method !== 'GET') writes.push({ method, path, body });
      const commentMatch = path.match(/^\/issues\/comments\/(\d+)$/);
      if (commentMatch) {
        for (const list of comments.values()) {
          const comment = list.find(c => c.id === +commentMatch[1]);
          if (comment) { Object.assign(comment, body); return structuredClone(comment); }
        }
        throw new Error('コメントがない');
      }
      const [, n, collection, value] = path.match(/^\/(?:issues|pulls)\/(\d+)(?:\/(comments|labels)(?:\/(.+))?)?$/) ?? [];
      const item = records.get(+n);
      if (!item) throw new Error(`対象がない: ${path}`);
      if (method === 'GET') return structuredClone(item);
      if (collection === 'comments') {
        const comment = { id: serial++, user: bot, body: body.body, created_at: new Date(now).toISOString() };
        comments.get(+n).push(comment);
        return structuredClone(comment);
      }
      if (collection === 'labels') {
        if (method === 'POST') {
          for (const name of body.labels) if (!item.labels.some(l => l.name === name)) {
            item.labels.push({ name }); addEvent(+n, 'labeled', name);
          }
        } else {
          const name = decodeURIComponent(value);
          item.labels = item.labels.filter(l => l.name !== name);
          addEvent(+n, 'unlabeled', name);
        }
      } else Object.assign(item, body);
      return structuredClone(item);
    },
  };
  function addEvent(n, event, label) {
    events.get(n).push({ id: serial++, event, label: { name: label }, created_at: new Date(now).toISOString() });
  }
  return {
    api, records, comments, events, reviewComments, reviews, writes, addEvent,
    async run(time = start, eventName = 'schedule', event = {}) { now = time; await runPolicy(api, event, eventName, now); },
    reply(n, author = 'alice', time = start + 1000) {
      comments.get(n).push({ id: serial++, user: user(author), body: '確認します', created_at: new Date(time).toISOString() });
    },
  };
}

const Day = 24 * 60 * 60 * 1000;
const limited = (f, n) => f.records.get(n).labels.some(l => l.name === Labels.limit);

test('Draft も上限に含め、作者ごとの古い 2 件を残す', () => {
  assert.deepEqual([...excessPulls([pull(4), pull(1, true), pull(3), pull(2), pull(5, false, 'bob')])], [3, 4]);
});

for (const association of ['OWNER', 'MEMBER', 'COLLABORATOR']) {
  test(`${association}: メンテナーには上限の通知をせず、一般投稿者の 3 件目だけを順番待ちにする`, async () => {
    const f = fixture([
      ...[1, 2, 3].map(n => pull(n, false, 'alice', association)),
      ...[4, 5, 6].map(n => pull(n, false, 'bob')),
    ]);
    await f.run();
    for (const n of [1, 2, 3]) {
      assert.deepEqual(f.records.get(n).labels, []);
      assert.deepEqual(f.comments.get(n), []);
    }
    assert.equal(f.comments.get(6).length, 1);
    assert.ok(limited(f, 6));
    assert.ok(!limited(f, 5));
  });
}

for (const association of ['CONTRIBUTOR', 'FIRST_TIME_CONTRIBUTOR', 'FIRST_TIMER', 'MANNEQUIN', 'NONE']) {
  test(`${association}: 一般投稿者は上限の対象にする`, () => {
    assert.deepEqual([...excessPulls([1, 2, 3].map(n => pull(n, false, 'alice', association)))], [3]);
  });
}

test('メンテナーになった投稿者の上限超過ラベルと通知を解除する', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]);
  await f.run();
  for (const pr of f.records.values()) pr.author_association = 'COLLABORATOR';
  await f.run(start + Day);
  assert.deepEqual(f.records.get(3).labels, []);
  assert.equal(f.comments.get(3).length, 1);
  assert.match(f.comments.get(3)[0].body, /対象ではなく/);
});

test('メンテナーの PR も PR要対応ラベルによる期限管理の対象にする', async () => {
  const pr = pull(1, false, 'alice', 'OWNER'); pr.labels = [{ name: Labels.rule }];
  const f = fixture([pr]);
  await f.run();
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'closed');
});

test('3 件目は順番待ちにするだけで、時間がたっても Draft にも閉じもしない', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]);
  await f.run();
  assert.equal(f.comments.get(1).length, 0);
  assert.equal(f.comments.get(3).length, 1);
  assert.match(f.comments.get(3)[0].body, /順番待ち/);
  await f.run(start + WaitMs * 2);
  assert.equal(f.writes.some(w => w.method === 'GRAPHQL'), false);
  assert.equal(f.records.get(3).draft, false);
  assert.equal(f.records.get(3).state, 'open');
  assert.ok(limited(f, 3));
  assert.equal(f.comments.get(3).length, 1);
});

test('Draft にしても枠は空かない (上限を避けるために Draft にしなくてよい)', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]);
  await f.run();
  f.records.get(3).draft = true; f.addEvent(3, 'converted_to_draft');
  await f.run(start + Day);
  assert.ok(limited(f, 3));
  assert.match(f.comments.get(3)[0].body, /"status":"waiting"/);
});

test('枠が空いたらラベルを外して通知を解除する', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]);
  await f.run();
  f.records.get(1).state = 'closed';
  await f.run(start + Day);
  assert.equal(f.records.get(3).labels.length, 0);
  assert.match(f.comments.get(3)[0].body, /対象ではなく/);
});

test('次にレビューの付いた PR は、古い PR より先に枠へ入れる', () => {
  const next = pull(3); next.labels.push({ name: Labels.next });
  assert.deepEqual([...excessPulls([pull(1), pull(2), next])], [2]);
});

test('次にレビューを付けた順番待ちの PR を枠に入れ、代わりに古い PR を順番待ちにする', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]);
  await f.run();
  f.records.get(3).labels.push({ name: Labels.next }); f.addEvent(3, 'labeled', Labels.next);
  await f.run(start + Day);
  assert.ok(limited(f, 2));
  assert.ok(!limited(f, 3));
  assert.equal(f.records.get(2).draft, false);
});

test('前の版の「Draft にする」超過の通知は、順番待ちの通知に書き直す', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]);
  await f.run();
  const notice = f.comments.get(3)[0];
  notice.body = notice.body.replace('"version":3', '"version":2');
  await f.run(start + Day);
  assert.equal(f.records.get(3).draft, false);
  assert.equal(f.comments.get(3).length, 1);
  assert.match(f.comments.get(3)[0].body, /"version":3/);
});

test('他の人・bot のコメントやレビュー通知では投稿者の返信とみなさない', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  await f.run();
  f.reply(1, 'reviewer');
  f.comments.get(1).push({ user: bot, body: 'CI 成功', created_at: new Date(start + 1000).toISOString() });
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'closed');
});

test('手動の PR要対応ラベルも通知から 14 日待ち、通知を重複投稿しない', async () => {
  const pr = pull(1); pr.labels = [{ name: Labels.rule }];
  const f = fixture([pr]);
  await f.run(); await f.run(start + 1000);
  assert.equal(f.comments.get(1).length, 1);
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'closed');
});

test('情報待ちも、古い Issue を即座に閉じず初回通知から 14 日待つ', async () => {
  const f = fixture([issue(1, [Labels.info]), issue(2)]);
  await f.run();
  assert.equal(f.records.get(1).state, 'open');
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'closed');
  assert.equal(f.records.get(2).state, 'open');
});

test('情報待ちへの返信と本文追記で、メンテナーの確認待ちにする', async () => {
  const f = fixture([issue(1, [Labels.info]), issue(2, [Labels.info])]);
  await f.run(); f.reply(1); f.records.get(2).body += '情報';
  await f.run(start + WaitMs * 2);
  assert.equal(f.records.get(1).state, 'open');
  assert.equal(f.records.get(2).state, 'open');
});

test('ラベル解除で期限管理を止め、付け直すと新しく 14 日待つ', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  await f.run();
  f.records.get(1).labels = [];
  f.addEvent(1, 'unlabeled', Labels.info);
  await f.run(start + WaitMs, 'issues', { issue: { number: 1 } });
  assert.match(f.comments.get(1)[0].body, /対象ではなく/);
  f.records.get(1).labels = [{ name: Labels.info }];
  f.addEvent(1, 'labeled', Labels.info);
  await f.run(start + WaitMs * 2);
  assert.equal(f.records.get(1).state, 'open');
  await f.run(start + WaitMs * 3);
  assert.equal(f.records.get(1).state, 'closed');
});

test('再オープン時は、古い通知の期限で即クローズしない', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  await f.run(); await f.run(start + WaitMs);
  f.records.get(1).state = 'open'; f.addEvent(1, 'reopened');
  await f.run(start + WaitMs * 2);
  assert.equal(f.records.get(1).state, 'open');
});

test('イベント処理は通知だけで、期限切れでもクローズしない', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  await f.run();
  await f.run(start + WaitMs, 'issues', { issue: { number: 1 } });
  assert.equal(f.records.get(1).state, 'open');
});

test('閉じる直前の再取得で返信が見つかったら閉じない', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  await f.run();
  const original = f.api.list; let reads = 0;
  f.api.list = async path => {
    if (path === '/issues/1/comments' && ++reads === 2) f.reply(1);
    return original(path);
  };
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
});

test('ユーザーが通知マーカーを偽装しても、期限切れとして扱わない', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  await f.run();
  f.comments.get(1)[0].user = user('attacker');
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
  assert.equal(f.comments.get(1).length, 2);
});

test('API の 2 ページ目まで読む。API エラー時は欠けた一覧を返さない', async () => {
  const calls = [];
  const api = githubApi({ token: 'test', repository: 'test/repo', fetcher: async url => {
    calls.push(url);
    return { ok: true, json: async () => url.endsWith('page=1') ? Array.from({ length: 100 }, (_, i) => ({ number: i })) : [{ number: 100 }] };
  } });
  assert.equal((await api.list('/pulls?state=open')).length, 101);
  assert.equal(calls.length, 2);
  const failing = githubApi({ token: 'test', repository: 'test/repo', fetcher: async () => ({ ok: false, status: 403 }) });
  await assert.rejects(failing.list('/pulls'), /403/);
});

test('Draft からレビューへ戻したら、手動ルールの古い期限を引き継がない', async () => {
  const pr = pull(1); pr.labels = [{ name: Labels.rule }];
  const f = fixture([pr]); await f.run();
  f.addEvent(1, 'converted_to_draft'); f.addEvent(1, 'ready_for_review');
  await f.run(start + WaitMs * 2);
  assert.equal(f.records.get(1).state, 'open');
});

test('壊れた通知はクローズの根拠にしない', async () => {
  const f = fixture([issue(1, [Labels.info])]); await f.run();
  f.comments.get(1)[0].body = f.comments.get(1)[0].body.replace(/"since":"[^"]+"/, '"since":"invalid"');
  assert.equal(decision(f.records.get(1), 'info', f.comments.get(1), [], start + WaitMs).action, 'warn');
});

for (const kind of ['reviewComments', 'reviews']) {
  test(`${kind}: 投稿者のレビュー内の返信でも自動クローズを止める`, async () => {
    const pr = pull(1); pr.labels = [{ name: Labels.rule }];
    const f = fixture([pr]); await f.run();
    f[kind].get(1).push({ user: user('alice'), body: '修正しました',
      created_at: new Date(start + 1000).toISOString(), submitted_at: new Date(start + 1000).toISOString() });
    await f.run(start + WaitMs);
    assert.equal(f.records.get(1).state, 'open');
  });
}

test('昔のコメントを編集した返信も拾う', async () => {
  const f = fixture([issue(1, [Labels.info])]);
  f.reply(1, 'alice', start - 1000); await f.run();
  f.comments.get(1)[0].updated_at = new Date(start + 1000).toISOString();
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
});

test('フォームなど bot 名義の報告は、人からの補足でも待機を止める', async () => {
  const report = issue(1, [Labels.info]); report.user = bot;
  const f = fixture([report]); await f.run(); f.reply(1, 'reporter');
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
});

test('No Close の PR も、超過していれば順番待ちの通知のままにする (Draft にも閉じもしない)', async () => {
  const f = fixture([pull(1), pull(2), pull(3)]); await f.run();
  f.records.get(3).labels.push({ name: Labels.exempt }); f.addEvent(3, 'labeled', Labels.exempt);
  await f.run(start + WaitMs * 2);
  assert.equal(f.records.get(3).draft, false);
  assert.equal(f.records.get(3).state, 'open');
  assert.ok(f.records.get(3).labels.some(l => l.name === Labels.limit));
  assert.match(f.comments.get(3)[0].body, /順番待ち/);
  assert.doesNotMatch(f.comments.get(3)[0].body, /クローズを止めています/);
});

test('No Close の間は閉じず、外したあとは新しく 14 日待つ', async () => {
  const f = fixture([issue(1, [Labels.info, Labels.exempt])]);
  const pr = pull(3); pr.labels = [{ name: Labels.rule }, { name: Labels.exempt }]; f.records.set(3, pr);
  for (const map of [f.comments, f.events, f.reviewComments, f.reviews]) map.set(3, []);
  await f.run();
  await f.run(start + WaitMs * 2);
  assert.equal(f.records.get(1).state, 'open');
  assert.equal(f.records.get(3).state, 'open');
  assert.equal(f.comments.get(1).length, 0);
  const removed = start + WaitMs * 2;
  f.records.get(1).labels = [{ name: Labels.info }]; f.addEvent(1, 'unlabeled', Labels.exempt);
  await f.run(removed);
  assert.equal(f.records.get(1).state, 'open');
  await f.run(removed + WaitMs - 1);
  assert.equal(f.records.get(1).state, 'open');
  await f.run(removed + WaitMs);
  assert.equal(f.records.get(1).state, 'closed');
});

test('待っている途中で No Close を付けたら、通知を止めて閉じない', async () => {
  const f = fixture([issue(1, [Labels.info])]); await f.run();
  f.records.get(1).labels.push({ name: Labels.exempt }); f.addEvent(1, 'labeled', Labels.exempt);
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
  assert.match(f.comments.get(1)[0].body, /"status":"exempt"/);
});

test('情報待ちで閉じるときは、情報不足で閉じることと再オープンの頼み方を書く', async () => {
  const f = fixture([issue(1, [Labels.info])]); await f.run();
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'closed');
  assert.equal(f.writes.find(w => w.path === '/issues/1' && w.method === 'PATCH').body.state_reason, 'not_planned');
  assert.match(f.comments.get(1).at(-1).body, /情報が届かなかった[\s\S]*再オープン/);
});

test('情報待ちラベルを解除していれば期限切れでも閉じない', async () => {
  const f = fixture([issue(1, [Labels.info])]); await f.run();
  const original = f.api.request; let reads = 0;
  f.api.request = async (method, path, body) => {
    if (method === 'GET' && path === '/issues/1' && ++reads === 2) f.records.get(1).labels = [];
    return original(method, path, body);
  };
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
});

test('API 取得に失敗したら、自動クローズせずに異常終了する', async () => {
  const f = fixture([issue(1, [Labels.info])]); await f.run();
  const original = f.api.list;
  f.api.list = async path => {
    if (path === '/issues/1/events') throw new Error('rate limit');
    return original(path);
  };
  await assert.rejects(f.run(start + WaitMs), /rate limit/);
  assert.equal(f.records.get(1).state, 'open');
});

test('通知の投稿が遅れた場合も、実際の通知から丸 14 日待つ', async () => {
  const f = fixture([issue(1, [Labels.info])]); await f.run();
  f.comments.get(1)[0].created_at = new Date(start + 60_000).toISOString();
  await f.run(start + WaitMs);
  assert.equal(f.records.get(1).state, 'open');
  await f.run(start + WaitMs + 60_000);
  assert.equal(f.records.get(1).state, 'closed');
});
