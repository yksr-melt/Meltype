// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121
import test from 'node:test';
import assert from 'node:assert/strict';
import {
  buildSearchQuery,
  buildSearchQueries,
  extractIssueSignals,
  findRelatedIssues,
  formatRelatedIssueComment,
  scoreCandidate,
} from '../issue-relation-candidates.mjs';
import { searchIssueFixtures } from '../fixture-issue-search.mjs';
import { keyInputIssues } from '../fixtures/key-input-normalization.mjs';

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

test('builds repository-scoped queries with at most five OR alternatives', () => {
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
  assert.equal((query.match(/ OR /g) ?? []).length, 4, 'the query uses exactly five distinct alternatives');
  assert.equal(query.match(/"[^"]+"/g).length, 5);
  assert.equal((query.match(/"ordinarylongterm"/g) ?? []).length, 1);
  const identifierQuery = buildSearchQuery(
    'yksr-melt/Meltype',
    [['0x80004005', 4], ['IME自動切替', 4]],
  );
  assert.equal(identifierQuery.split(' OR ').length - 1, 1);
  assert.equal(buildSearchQuery('yksr-melt/Meltype', []), null);
});

test('normalizes general key combinations without collapsing the individual keys', () => {
  const spacedCtrlIssue = {
    number: 9201,
    title: 'Ctrl + X shortcut',
    body: 'Press Ctrl+X to cut text.',
  };
  const compactCtrlIssue = {
    number: 9202,
    title: 'CTRL+X shortcut',
    body: 'Press Ctrl + X to cut text.',
  };
  const spacedCtrl = extractIssueSignals(spacedCtrlIssue);
  const compactCtrl = extractIssueSignals(compactCtrlIssue);
  assert.ok(spacedCtrl.normalizedTitle.includes('keyseq_ctrl_x'));
  assert.ok(compactCtrl.normalizedTitle.includes('keyseq_ctrl_x'));
  assert.ok(spacedCtrl.terms.some(([term]) => term === 'keyseq_ctrl_x'));
  assert.equal(spacedCtrl.text.includes('ctrl + x'), true, 'raw text remains available for references and identifiers');
  assert.ok(scoreCandidate(spacedCtrlIssue, compactCtrlIssue));

  const chord = extractIssueSignals(keyInputIssues[234]);
  const compactSequences = extractIssueSignals(keyInputIssues[338]);
  for (const key of ['h', 'j', 'k', 'l']) {
    const token = `keyseq_z_${key}`;
    assert.ok(chord.normalizedTitle.includes(token));
    assert.ok(compactSequences.normalizedBody.includes(token));
  }
  assert.ok(scoreCandidate(keyInputIssues[234], keyInputIssues[338]));
  assert.ok(scoreCandidate(keyInputIssues[338], keyInputIssues[234]));
});

test('query-term search finds a modifier chord across spacing and case variants', async () => {
  const source = {
    number: 9203,
    title: 'Ctrl + X shortcut',
    body: 'Press Ctrl + X to cut selected text.',
  };
  const candidate = {
    number: 9204,
    title: 'CTRL+X shortcut',
    body: 'Press CTRL+X to cut selected text.',
  };
  const calls = [];
  const found = await findRelatedIssues(source, {
    repo: 'yksr-melt/Meltype',
    search: query => {
      calls.push(query);
      return searchIssueFixtures(query, [candidate]);
    },
    getIssue: async () => null,
  });
  assert.ok(calls[0].includes('"ctrl+x"'));
  assert.deepEqual(found.map(entry => entry.issue.number), [9204]);
});

test('normalizes key-input list separators and case, but leaves ordinary letter text untouched', () => {
  const slashList = extractIssueSignals({
    title: 'A + B / C key combinations',
    body: 'The a b / a c sequences are alternatives.',
  });
  const commaList = extractIssueSignals({
    title: 'AB, AC key combinations',
    body: 'Press ab, ac for the same actions.',
  });
  assert.ok(slashList.normalizedTitle.includes('keyseq_a_b'));
  assert.ok(slashList.normalizedTitle.includes('keyseq_a_c'));
  assert.ok(commaList.normalizedTitle.includes('keyseq_a_b'));
  assert.ok(commaList.normalizedTitle.includes('keyseq_a_c'));

  const ordinary = extractIssueSignals({
    title: 'This is an ordinary English sentence',
    body: 'A + B is a mathematical expression. A shortcut is documented later.',
  });
  assert.ok(!ordinary.normalizedTitle.includes('keyseq_'));
  assert.ok(!ordinary.normalizedBody.includes('keyseq_'));
  assert.match(ordinary.normalizedBody, /a \+ b/i);

  const unrelatedContext = extractIssueSignals({
    title: 'Letters are part of this name',
    body: 'A + B is a formula. The keyboard shortcut is Ctrl+X.',
  });
  assert.ok(!unrelatedContext.normalizedBody.includes('keyseq_a_b'));
  assert.ok(unrelatedContext.normalizedBody.includes('keyseq_ctrl_x'));

  const compactList = extractIssueSignals({
    title: 'zh / zj / zk / zl key sequence',
    body: '',
  });
  for (const key of ['h', 'j', 'k', 'l']) {
    assert.ok(compactList.normalizedTitle.includes(`keyseq_z_${key}`));
  }
});

test('retrieves fixture Issues by OR query terms and does not return unrelated text', async () => {
  const calls = [];
  const retrieved = new Set();
  const found = await findRelatedIssues(keyInputIssues[234], {
    repo: 'yksr-melt/Meltype',
    search: query => {
      calls.push(query);
      const result = searchIssueFixtures(query, Object.values(keyInputIssues));
      for (const issue of result.items) retrieved.add(issue.number);
      return result;
    },
    getIssue: async () => null,
  });
  assert.ok(calls.length >= 1 && calls.length <= 2);
  assert.ok(calls.some(query => query.includes('"z + h"')));
  assert.ok(calls.some(query => query.includes('"zh"')));
  assert.ok(!retrieved.has(9001), 'query-term search must not return the unrelated shortcut fixture');
  assert.ok(calls.every(query => (query.match(/ OR /g) ?? []).length <= 4));
  assert.deepEqual(found.map(candidate => candidate.issue.number), [338]);
  assert.ok(!found.some(candidate => candidate.issue.number === 9001));
});

test('fixture retrieval applies case-insensitive OR substring terms separately from labels', () => {
  const matches = searchIssueFixtures(
    'repo:yksr-melt/Meltype is:issue label:"bug" ("trackball" OR "firmware")',
    [
      { number: 1, title: 'Trackball reset', body: '', labels: [{ name: 'bug' }] },
      { number: 2, title: 'Driveway firmwared tool', body: '', labels: [{ name: 'bug' }] },
      { number: 3, title: 'Trackball reset', body: '', labels: [{ name: '提案' }] },
      { number: 4, title: 'Unrelated printer', body: '', labels: [{ name: 'bug' }] },
    ],
  );
  assert.deepEqual(matches.items.map(issue => issue.number), [1, 2]);
  assert.deepEqual(searchIssueFixtures('repo:yksr-melt/Meltype is:issue', [
    { number: 5, title: 'Anything', body: '', labels: [] },
  ]).items, []);
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

test('does not search for empty terms but still resolves explicit issue references', async () => {
  const source = { number: 9401, title: '[提案]', body: 'See #9402' };
  const referenced = { number: 9402, title: 'Unrelated title', body: '' };
  let searchCalls = 0;
  const found = await findRelatedIssues(source, {
    repo: 'yksr-melt/Meltype',
    search: async () => {
      searchCalls++;
      return { items: [] };
    },
    getIssue: async number => number === 9402 ? referenced : null,
  });
  assert.equal(searchCalls, 0);
  assert.deepEqual(found.map(candidate => candidate.issue.number), [9402]);
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

test('builds at most two searches and prioritizes an existing classification label', () => {
  const issue = {
    number: 44,
    title: 'Brave PWA IME input failure',
    body: '### Details\nBrave PWA input failure with Win32Error 0x80004005',
    labels: [{ name: 'bug' }, { name: 'win11' }, { name: '優先: 高' }],
  };
  const queries = buildSearchQueries('yksr-melt/Meltype', issue);
  assert.ok(queries.length <= 2);
  assert.match(queries[0], /label:"bug"/);
  assert.ok(queries.every(query => (query.match(/ OR /g) ?? []).length <= 4));
  assert.ok(queries.every(query => query.includes('repo:yksr-melt/Meltype is:issue')));
});

test('does not use priority labels as the classification filter', () => {
  const issue = {
    number: 46,
    title: 'A specific startup failure',
    body: '### Details\nWin32Error 0x80004005',
    labels: [{ name: '優先: 高' }],
  };
  const queries = buildSearchQueries('yksr-melt/Meltype', issue);
  assert.ok(queries.length <= 1);
  assert.ok(!queries.some(query => /label:/.test(query)));
});

test('does not issue the keyword fallback when the label query already finds a supported candidate', async () => {
  const current = {
    number: 47,
    title: 'Win32Error startup failure',
    body: '### Details\n0x80004005',
    labels: [{ name: 'bug' }],
  };
  const related = {
    number: 48,
    title: 'Win32Error 0x80004005 startup failure',
    body: '### Details\nStartup fails with 0x80004005',
    labels: [{ name: 'bug' }],
  };
  const calls = [];
  const found = await findRelatedIssues(current, {
    repo: 'yksr-melt/Meltype',
    search: async query => {
      calls.push(query);
      return { items: [related] };
    },
    getIssue: async () => null,
  });
  assert.equal(calls.length, 1);
  assert.match(calls[0], /label:"bug"/);
  assert.equal(found[0]?.issue.number, 48);
});

test('uses a label-filtered query and a bounded keyword fallback', async () => {
  const current = {
    number: 45, title: 'Brave PWA input failure',
    body: '### Details\nWin32Error 0x80004005 Brave PWA',
    labels: [{ name: 'bug' }],
  };
  const calls = [];
  await findRelatedIssues(current, {
    repo: 'yksr-melt/Meltype',
    search: async query => { calls.push(query); return { items: [] }; },
    getIssue: async () => null,
  });
  assert.equal(calls.length, 2, 'run the fallback only when the label query yields no supported candidates');
  assert.match(calls[0], /label:"bug"/);
  assert.ok(!/label:/.test(calls[1]), 'fallback should not add another label constraint');
  assert.ok(calls.every(query => query.includes('(') && query.includes(' OR ')));
  assert.ok(calls.every(query => (query.match(/ OR /g) ?? []).length <= 4));
});
