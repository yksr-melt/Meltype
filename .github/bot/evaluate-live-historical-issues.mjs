// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121
// Live evaluation of the candidate pipeline against current public historical Issues.
// Manual-only: results can change as Issues/labels change or API limits are hit.
import { findRelatedIssues, scoreCandidate } from './issue-relation-candidates.mjs';

const repo = process.env.EVALUATION_REPOSITORY || 'yksr-melt/Meltype';
const api = process.env.GITHUB_API_URL || 'https://api.github.com';
const token = process.env.GITHUB_TOKEN;
if (!token) throw new Error('Set GITHUB_TOKEN with read-only Issues access.');

const headers = { Authorization: 'Bearer ' + token, Accept: 'application/vnd.github+json', 'User-Agent': 'meltype-historical-evaluation' };

async function getIssue(number) {
  const response = await fetch(api + '/repos/' + repo + '/issues/' + number, { headers });
  if (response.status === 404) return null;
  if (!response.ok) throw new Error('GET issue #' + number + ': HTTP ' + response.status);
  const issue = await response.json();
  return issue.pull_request ? null : issue;
}

async function searchIssues(query) {
  const url = new URL(api + '/search/issues');
  url.searchParams.set('q', query);
  url.searchParams.set('per_page', '100');
  const response = await fetch(url, { headers });
  if (!response.ok) throw new Error('Issue search: HTTP ' + response.status);
  return { items: (await response.json()).items || [] };
}

const cases = [
  { source: 873, expected: 874, related: true, note: 'Brave/PWA IME input failure; later grouped in #878' },
  { source: 316, expected: 261, related: true, note: 'explicit reference; recall when keyword search misses' },
  { source: 717, expected: 725, related: false, note: 'same form/category but different defect' },
];

const results = [];
for (const item of cases) {
  const source = await getIssue(item.source);
  if (!source) throw new Error('Could not fetch source Issue #' + item.source);
  const expectedIssue = await getIssue(item.expected);
  if (!expectedIssue) throw new Error('Could not fetch expected Issue #' + item.expected);
  const candidates = await findRelatedIssues(source, { repo, search: searchIssues, getIssue });
  const numbers = candidates.map(candidate => candidate.issue.number);
  const found = numbers.includes(item.expected);
  const directScore = scoreCandidate(source, expectedIssue);
  const classifiedRelated = directScore !== null;
  results.push({
    source: item.source, expected: item.expected, related: item.related,
    pass: item.related ? found && classifiedRelated : !found && !classifiedRelated, found,
    directClassification: classifiedRelated ? { score: directScore.score, reasons: directScore.reasons } : null,
    candidates: candidates.map(candidate => ({ number: candidate.issue.number, score: candidate.score, reasons: candidate.reasons })),
    note: item.note,
  });
}

const positives = results.filter(result => result.related);
const negatives = results.filter(result => !result.related);
console.log(JSON.stringify({
  mode: 'live GitHub Issue API + candidate search/scoring pipeline',
  boundedRequests: '3 source Issues, 3 expected Issues, at most 6 bounded Issue searches, plus explicit-reference lookups only',
  summary: {
    cases: results.length,
    passed: results.filter(result => result.pass).length,
    failed: results.filter(result => !result.pass).length,
    positiveRecallOnSample: positives.filter(result => result.found).length / positives.length,
    hardNegativeRejectionOnSample: negatives.filter(result => !result.found).length / negatives.length,
  },
  results,
}, null, 2));
if (results.some(result => !result.pass)) process.exitCode = 1;
