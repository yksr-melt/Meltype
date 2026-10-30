// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121

export function searchIssueFixtures(query, issues) {
  const terms = [...query.replace(/\blabel:"[^"]+"/g, '').matchAll(/"([^"]+)"/g)]
    .map(([, term]) => term.toLowerCase());
  if (terms.length === 0) return { items: [] };

  const label = query.match(/\blabel:"([^"]+)"/)?.[1]?.toLowerCase();
  return {
    items: issues.filter(issue => {
      if (!issue || issue.pull_request) return false;
      const labels = (issue.labels ?? []).map(value =>
        (typeof value === 'string' ? value : value.name ?? '').toLowerCase(),
      );
      if (label && !labels.includes(label)) return false;
      const searchableText = [issue.title ?? '', issue.body ?? ''].join('\n').toLowerCase();
      return terms.some(term => searchableText.includes(term));
    }),
  };
}
