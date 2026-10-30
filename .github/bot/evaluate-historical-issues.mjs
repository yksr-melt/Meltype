// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121
// Deterministic offline regression evaluation. This small sample is not a statistical precision/recall estimate.
import { extractIssueSignals, findRelatedIssues, scoreCandidate } from './issue-relation-candidates.mjs';
import { searchIssueFixtures } from './fixture-issue-search.mjs';
import { historical, cases } from './fixtures/historical-issues.mjs';

const results = [];
for (const item of cases) {
  const source = historical[item.source];
  const expectedIssue = historical[item.expected];
  if (!source || !expectedIssue) {
    results.push({ ...item, pass: false, error: 'Fixture issue missing' });
    continue;
  }
  const queryMatches = new Set();
  const candidates = await findRelatedIssues(source, {
    repo: 'yksr-melt/Meltype',
    search: async query => {
      const result = searchIssueFixtures(query, Object.values(historical));
      for (const candidate of result.items) queryMatches.add(candidate.number);
      return result;
    },
    getIssue: async number => historical[number] ?? null,
  });
  const found = candidates.some(candidate => candidate.issue.number === item.expected);
  const queryRetrieved = queryMatches.has(item.expected);
  const queryEvaluationApplicable = !extractIssueSignals(source).references.includes(item.expected);
  const directScore = scoreCandidate(source, expectedIssue);
  const classifiedRelated = directScore !== null;
  const pass = item.related
    ? found && classifiedRelated && (!queryEvaluationApplicable || queryRetrieved)
    : !found && !classifiedRelated;
  results.push({
    source: item.source,
    expected: item.expected,
    related: item.related,
    pass,
    found,
    queryRetrieved,
    queryEvaluationApplicable,
    directClassification: classifiedRelated ? { score: directScore.score, reasons: directScore.reasons } : null,
    candidates: candidates.map(candidate => ({ number: candidate.issue.number, score: candidate.score, reasons: candidate.reasons })),
    note: item.note,
  });
}
const positives = results.filter(result => result.related);
const negatives = results.filter(result => !result.related);
const queryCases = results.filter(result => result.queryEvaluationApplicable);
const queryPositives = queryCases.filter(result => result.related);
const queryNegatives = queryCases.filter(result => !result.related);
console.log(JSON.stringify({
  mode: 'offline fixed Issue fixtures',
  fixtureIssues: Object.keys(historical).length,
  disclaimer: 'The fixture search matches OR query phrases case-insensitively as substrings in title/body; it does not reproduce GitHub tokenization, ranking, freshness, or rate limits. This small sample is not a statistical precision/recall estimate.',
  summary: {
    cases: results.length,
    passed: results.filter(result => result.pass).length,
    failed: results.filter(result => !result.pass).length,
    positiveRecallOnSample: positives.length ? positives.filter(result => result.found).length / positives.length : null,
    hardNegativeRejectionOnSample: negatives.length ? negatives.filter(result => !result.found).length / negatives.length : null,
    queryRetrieval: {
      cases: queryCases.length,
      positiveCases: queryPositives.length,
      positiveRetrieved: queryPositives.filter(result => result.queryRetrieved).length,
      positiveRecall: queryPositives.length
        ? queryPositives.filter(result => result.queryRetrieved).length / queryPositives.length
        : null,
      negativeCases: queryNegatives.length,
      unrelatedIssuesAlsoReturnedByKeywordSearch: queryNegatives.filter(result => result.queryRetrieved).length,
    },
  },
  queryRetrievalGate: {
    applicablePositiveCases: queryPositives.length,
    retrieved: queryPositives.filter(result => result.queryRetrieved).length,
    passed: queryPositives.every(result => result.queryRetrieved),
  },
  results,
}, null, 2));
if (results.some(result => !result.pass)) process.exitCode = 1;
