// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121
import test from 'node:test';
import assert from 'node:assert/strict';
import { buildSearchQuery, extractIssueSignals, findRelatedIssues, formatRelatedIssueComment, scoreCandidate } from '../issue-relation-candidates.mjs';

test('extracts identifiers and excludes log/environment sections', () => {
  const signals = extractIssueSignals({
    title: '[不具合] Win32Error after launch',
    body: '### どうなったか\nWin32Error 0x80004005\n### ログ\nSecretExampleException\n### 実行環境\nPrivatePath',
  });
  assert.deepEqual(signals.references, []);
  assert.ok(signals.identifiers.includes('0x80004005'));
  assert.ok(signals.identifiers.includes('win32error'));
  assert.ok(!signals.text.includes('secretexampleexception'));
  assert.ok(!signals.text.includes('privatepath'));
});

test('builds repository-scoped queries with at most five OR operators', () => {
  const terms = [
    ['ordinarylongterm', 1],
    ['0x80004005', 1],
    ['Win32Error', 1],
    ['featureproposal', 1],
    ['anotherterm', 1],
    ['fifthterm', 1],
    ['sixthterm', 1],
    ['seventhterm', 1],
    ['ORDINARYLONGTERM', 0],
  ];
  const query = buildSearchQuery('yksr-melt/Meltype', terms);
  assert.ok(query.includes('repo:yksr-melt/Meltype is:issue'));
  assert.match(query, /"0x80004005"/);
  assert.match(query, /"win32error"/);
  assert.equal(query.split(' OR ').length - 1, 5);
  assert.equal((query.match(/"ordinarylongterm"/g) ?? []).length, 1);
  assert.equal(buildSearchQuery('yksr-melt/Meltype', [['0x80004005', 4], ['IME自動切替', 4]]).split(' OR ').length - 1, 1);
  assert.equal(buildSearchQuery('yksr-melt/Meltype', []), null);
});


test('ignores generic issue-form phrases when scoring body-only overlap', () => {
  const current = {
    number: 20,
    title: 'Alpha startup failure',
    body: '### 何をしたら\n入力欄でローマ字を打つ\n### どうなったか\n変換されない\n### どうなってほしかったか\n日本語になってほしい',
  };
  const boilerplateOnly = {
    number: 21,
    title: 'Unrelated printer behavior',
    body: '### 何をしたら\n入力欄でローマ字を打つ\n### どうなったか\n変換されない\n### どうなってほしかったか\n日本語になってほしい',
  };
  const titleMatch = {
    number: 22,
    title: 'Alpha startup failure',
    body: '### Details\nA distinct explanation with no generic form phrases.',
  };
  assert.equal(scoreCandidate(current, boilerplateOnly), null);
  assert.ok(scoreCandidate(current, titleMatch));
});

test('keeps candidates supported by distinctive body terms even when titles differ', () => {
  const current = {
    number: 40,
    title: 'Input stops after startup',
    body: '### Details\nKensington trackball firmware reset sequence fails',
  };
  const related = {
    number: 41,
    title: 'Unexpected reset behavior',
    body: '### Details\nKensington trackball firmware reset sequence fails intermittently',
  };
  const result = scoreCandidate(current, related);
  assert.ok(result);
  assert.ok(result.score >= 2);
});

test('explicit references and identifiers rank above weak overlap', () => {
  const current = { number: 10, title: 'Win32Error on launch', body: '### 詳細\n0x80004005 launch failure #12', labels: [{ name: 'bug' }] };
  const referenced = { number: 12, title: 'Win32Error 0x80004005 launch failure', body: '', labels: [{ name: 'bug' }] };
  const weak = { number: 13, title: 'launch', body: 'nothing else', labels: [] };
  assert.ok(scoreCandidate(current, referenced).score > (scoreCandidate(current, weak)?.score ?? 0));
  assert.equal(scoreCandidate(current, { ...referenced, number: 10 }), null);
  assert.equal(scoreCandidate(current, { ...referenced, number: 14, pull_request: {} }), null);
});

test('skips pull requests/current issue and caps candidates', async () => {
  const current = { number: 10, title: '[不具合] Win32Error 起動失敗', body: '### どうなったか\nWin32Error 0x80004005 起動できない #11', labels: [{ name: 'bug' }] };
  const issues = [
    { number: 10, title: current.title, body: current.body },
    { number: 11, title: 'Win32Error 0x80004005 起動できない', body: '', labels: [{ name: 'bug' }] },
    { number: 12, title: 'Win32Error 0x80004005 起動できない', body: '', labels: [{ name: 'bug' }], pull_request: {} },
    { number: 13, title: 'Win32Error 0x80004005 起動できない', body: '', labels: [{ name: 'bug' }] },
  ];
  const found = await findRelatedIssues(current, {
    repo: 'yksr-melt/Meltype', search: async () => ({ items: issues }),
    getIssue: async number => issues.find(item => item.number === number), maxCandidates: 1,
  });
  assert.equal(found.length, 1);
  assert.equal(found[0].issue.number, 11);
});

test('returns an empty list when search finds no candidates', async () => {
  const current = { number: 30, title: 'Alpha startup failure', body: '### Details\nUnique launch sequence.' };
  const found = await findRelatedIssues(current, {
    repo: 'yksr-melt/Meltype',
    search: async () => ({ items: [] }),
    getIssue: async () => null,
  });
  assert.deepEqual(found, []);
});

test('propagates search API failures to the caller', async () => {
  const current = { number: 31, title: 'Alpha startup failure', body: '### Details\nUnique launch sequence.' };
  await assert.rejects(
    findRelatedIssues(current, {
      repo: 'yksr-melt/Meltype',
      search: async () => { throw new Error('Issue search failed: 429'); },
      getIssue: async () => null,
    }),
    /Issue search failed: 429/,
  );
});

test('comment explains evidence and requires human review', () => {
  const comment = formatRelatedIssueComment({ number: 10 }, [{
    issue: { number: 11, title: 'Related issue', html_url: 'https://github.com/yksr-melt/Meltype/issues/11' },
    reasons: ['エラー識別子が一致: 0x80004005'],
  }]);
  assert.match(comment, /#11 Related issue/);
  assert.match(comment, /関連性を確定できません/);
});
