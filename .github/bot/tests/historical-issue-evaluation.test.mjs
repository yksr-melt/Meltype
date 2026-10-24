// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121
// Historical evaluation set from public Meltype issues. This file is a regression
// sample, not a claim of exhaustive precision/recall measurement.
import test from 'node:test';
import assert from 'node:assert/strict';
import { findRelatedIssues, scoreCandidate } from '../issue-relation-candidates.mjs';
import { searchIssueFixtures } from '../fixture-issue-search.mjs';
import { historical } from '../fixtures/historical-issues.mjs';

test('historical false-positive control: #717 and #725 share an issue form and category, not a specific defect', () => {
  return Promise.all([
    findRelatedIssues(historical[717], {
      repo: 'yksr-melt/Meltype',
      search: query => searchIssueFixtures(query, Object.values(historical)),
      getIssue: async () => null,
    }),
    findRelatedIssues(historical[725], {
      repo: 'yksr-melt/Meltype',
      search: query => searchIssueFixtures(query, Object.values(historical)),
      getIssue: async () => null,
    }),
  ]).then(([first, second]) => {
    assert.ok(!first.some(candidate => candidate.issue.number === 725));
    assert.ok(!second.some(candidate => candidate.issue.number === 717));
  });
});

test('overlapping Japanese n-grams from #717 and #95 do not count as distinct evidence', () => {
  assert.equal(scoreCandidate(historical[717], historical[95]), null);
  assert.equal(scoreCandidate(historical[95], historical[717]), null);
});

test('historical recall: explicit relation from #316 retrieves #261 even when keyword search returns no results', async () => {
  const calls = [];
  const found = await findRelatedIssues(historical[316], {
    repo: 'yksr-melt/Meltype',
    search: async query => {
      calls.push(query);
      return { items: [] };
    },
    getIssue: async number => number === 261 ? historical[261] : null,
  });
  assert.ok(calls.length >= 1 && calls.length <= 2, 'search stays within the bounded query budget');
  assert.ok(found.some(candidate => candidate.issue.number === 261), 'explicitly referenced #261 must not be missed');
});

test('historical query-term retrieval discovers and ranks #874 for #873', async () => {
  const calls = [];
  const found = await findRelatedIssues(historical[873], {
    repo: 'yksr-melt/Meltype',
    search: query => {
      calls.push(query);
      return searchIssueFixtures(query, Object.values(historical));
    },
    getIssue: async () => null,
  });

  assert.ok(calls.length >= 1 && calls.length <= 2, 'candidate discovery stays within the bounded query budget');
  assert.match(calls[0], /^repo:yksr-melt\/Meltype is:issue/);
  assert.ok((calls[0].match(/ OR /g) ?? []).length <= 4, 'query must stay within GitHub OR limits');
  assert.ok(found.some(candidate => candidate.issue.number === 874));
  assert.match(found[0].reasons.join(' '), /特徴語が一致/);
});
