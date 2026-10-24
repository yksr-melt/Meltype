// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Akatuki1121

const MAX_SEARCH_TERMS = 5;
const MAX_SEARCH_QUERIES = 2;
const MAX_EXTRACTED_TERMS = 80;
const MAX_CANDIDATES = 5;
const MAX_IDENTIFIER_REASONS = 3;
const MAX_MATCHED_TERMS_IN_REASON = 3;
const MAX_TITLE_MATCHES = 3;
const MAX_BODY_MATCHES = 4;
const MIN_BODY_TERM_LENGTH = 5;
const MIN_DISTINCT_MATCHES = 2;
const MIN_CANDIDATE_SCORE = 4;
const IDENTIFIER_PATTERN = /^(?:0x[0-9a-f]{4,}|[a-z][a-z0-9_.-]*(?:exception|error|failure)|e_fail|hresult|win32error|http[45]\d{2})$/i;
const FALLBACK_LABEL_PATTERN = /^(?:win10|win11|windows|mac|linux)$|^アプリ:/i;

const STOPWORDS = new Set([
  'this', 'that', 'with', 'from', 'have', 'when', 'where', 'what', 'which',
  'would', 'could', 'should', 'there', 'their', 'about', 'into', 'issue',
  'meltype', 'windows', 'linux', 'mac', 'version', 'expected', 'actual',
  '報告', '問題', '機能', '提案', '確認', '結果', '期待', '具体例', '利用シーン',
  '困っていること', '解決したい問題', '期待できる効果', 'メリット', 'こうなってほしい',
]);
const CATEGORY_LABELS = new Set(['bug', '誤判定', '提案', '機能提案']);
const IGNORED_HEADINGS = /ログ|実行環境|前後の文|スクリーンショット/i;
const GENERIC_BODY_PHRASES = [
  '入力欄でローマ字を打つ',
  '変換されない',
  '日本語になってほしい',
];

function searchableBody(body = '') {
  const withoutFences = body.replace(/\x60{3}[\s\S]*?\x60{3}/g, ' ');
  const sections = withoutFences.split(/^###\s+/m);
  const kept = [sections[0] ?? ''];
  for (const section of sections.slice(1)) {
    const newline = section.indexOf('\n');
    const heading = (newline < 0 ? section : section.slice(0, newline)).trim();
    if (!IGNORED_HEADINGS.test(heading)) kept.push(newline < 0 ? '' : section.slice(newline + 1));
  }
  let text = kept.join('\n').replace(/<!--[^]*?-->/g, ' ').replace(/https?:\/\/\S+/g, ' ');
  for (const phrase of GENERIC_BODY_PHRASES) {
    text = text.split(phrase).join(' ');
  }
  return text;
}

function extractIdentifiers(text) {
  return [...new Set([
    ...(text.match(/\b0x[0-9a-f]{4,}\b/gi) ?? []),
    ...(text.match(/\b[A-Z][A-Za-z0-9_.-]*(?:Exception|Error|Failure)\b/g) ?? []),
    ...(text.match(/\b(?:E_FAIL|HRESULT|Win32Error|HTTP\s*[45]\d\d)\b/gi) ?? []),
  ].map(value => value.toLowerCase()))];
}

const KEY_CONTEXT = /\b(?:key|keys|keyboard|shortcut|chord|press|pressed|type|typing)\b|キー|打鍵|押す|押した|打つ|打てる/i;
const MODIFIER_CHORD = /\b((?:(?:ctrl|control|alt|shift|meta|cmd|command|win|windows)\s*\+\s*)+)([a-z0-9])\b/gi;

function nearbyKeyContext(text, index, length) {
  const start = Math.max(
    text.lastIndexOf('\n', index - 1),
    text.lastIndexOf('.', index - 1),
    text.lastIndexOf('。', index - 1),
    text.lastIndexOf('!', index - 1),
    text.lastIndexOf('！', index - 1),
    text.lastIndexOf('?', index - 1),
    text.lastIndexOf('？', index - 1),
    text.lastIndexOf(';', index - 1),
    text.lastIndexOf('；', index - 1),
  ) + 1;
  const endCandidates = [
    text.indexOf('\n', index + length),
    text.indexOf('.', index + length),
    text.indexOf('。', index + length),
    text.indexOf('!', index + length),
    text.indexOf('！', index + length),
    text.indexOf('?', index + length),
    text.indexOf('？', index + length),
    text.indexOf(';', index + length),
    text.indexOf('；', index + length),
  ].filter(position => position >= 0);
  const end = endCandidates.length ? Math.min(...endCandidates) : text.length;
  return KEY_CONTEXT.test(text.slice(start, end));
}

function keySequenceToken(keys) {
  return 'keyseq_' + keys.map(key => key.toLowerCase()).join('_');
}

function chordFamilyPattern() {
  return /\b([a-z])\s*\+\s*([a-z](?:\s*\/\s*[a-z])*)\b/gi;
}

function compactChordFamilyPattern() {
  return /\b[a-z]{2}(?:\s*[,、/]\s*[a-z]{2})+\b/gi;
}

function normalizeKeyInputText(text) {
  let normalized = text.replace(chordFamilyPattern(), (match, first, rest, index) => {
    if (!nearbyKeyContext(text, index, match.length)) return match;
    return rest.match(/[a-z]/gi).map(key => keySequenceToken([first, key])).join(' ');
  });
  normalized = normalized.replace(compactChordFamilyPattern(), (match, index) => {
    if (!nearbyKeyContext(normalized, index, match.length)) return match;
    const keys = match.match(/[a-z]{2}/gi);
    if (new Set(keys.map(key => key[0].toLowerCase())).size !== 1) return match;
    return keys.map(key => keySequenceToken([...key])).join(' ');
  });
  return normalized.replace(MODIFIER_CHORD, (match, modifiers, key) => {
    const keys = [...modifiers.matchAll(/\b(?:ctrl|control|alt|shift|meta|cmd|command|win|windows)\b/gi)]
      .map(([modifier]) => modifier);
    keys.push(key);
    return keySequenceToken(keys);
  });
}

function extractKeyInputSearchTerms(text) {
  const terms = [];
  const add = (keys, phrase) => {
    terms.push([phrase, 20], [keys.join(''), 20]);
  };
  for (const match of text.matchAll(chordFamilyPattern())) {
    if (!nearbyKeyContext(text, match.index, match[0].length)) continue;
    for (const key of match[2].match(/[a-z]/gi)) {
      add([match[1], key], match[1] + ' + ' + key);
    }
  }
  for (const match of text.matchAll(compactChordFamilyPattern())) {
    if (!nearbyKeyContext(text, match.index, match[0].length)) continue;
    const sequences = match[0].match(/[a-z]{2}/gi);
    if (new Set(sequences.map(key => key[0].toLowerCase())).size !== 1) continue;
    for (const sequence of sequences) {
      add([...sequence], sequence[0] + ' + ' + sequence[1]);
    }
  }
  for (const match of text.matchAll(MODIFIER_CHORD)) {
    const modifiers = [...match[1].matchAll(/\b(?:ctrl|control|alt|shift|meta|cmd|command|win|windows)\b/gi)]
      .map(([modifier]) => modifier.toLowerCase());
    const keys = [...modifiers, match[2].toLowerCase()];
    const compact = keys.join('+');
    const spaced = keys.join(' + ');
    terms.push([compact, 20], [spaced, 20]);
  }
  return terms;
}

function extractTerms(title, body) {
  const terms = new Map();
  const add = (value, weight = 1, fromTitle = false) => {
    const term = value.trim().toLowerCase();
    if (term.length < 2 || STOPWORDS.has(term) || /^\d+$/.test(term)) return;
    const rankedWeight = weight + (fromTitle ? 5 : 0);
    const previous = terms.get(term);
    if (!previous || rankedWeight > previous.weight) {
      terms.set(term, { weight: rankedWeight, fromTitle });
    }
  };
  const collect = (source, fromTitle) => {
    for (const word of source.match(/[A-Za-z][A-Za-z0-9_.:-]{3,}/g) ?? []) add(word, 4, fromTitle);
    for (const run of source.match(/[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}]{2,}/gu) ?? []) {
      if (run.length <= 12) add(run, Math.min(run.length, 6), fromTitle);
      for (let size = Math.min(4, run.length); size >= 2; size--) {
        for (let i = 0; i <= run.length - size; i++) add(run.slice(i, i + size), size, fromTitle);
      }
    }
  };
  collect(title, true);
  collect(body, false);
  return [...terms]
    .map(([term, metadata]) => [term, metadata.weight, metadata.fromTitle])
    .sort((a, b) => b[1] - a[1] || b[0].length - a[0].length);
}

export function extractIssueSignals(issue) {
  const title = String(issue?.title ?? '').replace(/^\[[^\]]+\]\s*/, '');
  const body = searchableBody(issue?.body ?? '');
  const text = title + '\n' + body;
  const normalizedTitle = normalizeKeyInputText(title);
  const normalizedBody = normalizeKeyInputText(body);
  const references = [...new Set([...text.matchAll(/(?:^|[^\w])#(\d+)\b/g)].map(match => Number(match[1])))];
  const identifiers = extractIdentifiers(text);
  const terms = extractTerms(normalizedTitle, normalizedBody).slice(0, MAX_EXTRACTED_TERMS);
  const searchTerms = [
    ...extractTerms(title, '').slice(0, MAX_EXTRACTED_TERMS),
    ...extractTerms('', body).slice(0, MAX_EXTRACTED_TERMS),
    ...extractKeyInputSearchTerms(text),
  ];
  const labels = new Set(
    (issue?.labels ?? [])
      .map(label => typeof label === 'string' ? label : label.name)
      .filter(Boolean),
  );
  return {
    title,
    body,
    normalizedTitle: normalizedTitle.toLowerCase(),
    normalizedBody: normalizedBody.toLowerCase(),
    text: text.toLowerCase(),
    references,
    identifiers,
    terms,
    searchTerms,
    labels,
  };
}

function selectSearchTerms(terms) {
  const unique = new Map();
  for (const entry of terms) {
    const term = Array.isArray(entry) ? entry[0] : entry;
    const weight = Array.isArray(entry) && Number.isFinite(entry[1]) ? entry[1] : 0;
    const fromTitle = Array.isArray(entry) && entry[2] === true;
    if (typeof term !== 'string') continue;
    const normalized = term.trim().toLowerCase().replace(/["\\]/g, '');
    if (normalized.length < 2 || STOPWORDS.has(normalized) || /^\d+$/.test(normalized)) continue;
    const identifier = IDENTIFIER_PATTERN.test(normalized);
    const lengthBonus = normalized.length >= 8 ? 4 : normalized.length >= 5 ? 2 : 0;
    const priority = weight + lengthBonus + (identifier ? 100 : 0);
    const previous = unique.get(normalized);
    if (!previous || priority > previous.priority) {
      unique.set(normalized, { term: normalized, priority, fromTitle });
    }
  }
  const selected = [];
  let titleTerms = 0;
  for (const entry of [...unique.values()]
    .sort((a, b) =>
      b.priority - a.priority ||
      b.term.length - a.term.length ||
      a.term.localeCompare(b.term),
    )) {
    if (entry.fromTitle && titleTerms >= 3) continue;
    if (selected.some(previous =>
      Math.min(previous.term.length, entry.term.length) >= 3 &&
      (previous.term.includes(entry.term) || entry.term.includes(previous.term))
    )) continue;
    selected.push(entry);
    if (entry.fromTitle) titleTerms++;
    if (selected.length === MAX_SEARCH_TERMS) break;
  }
  return selected.map(entry => entry.term);
}

export function buildSearchQuery(repo, terms, label = null) {
  const usable = selectSearchTerms(terms);
  if (usable.length === 0) return null;
  const alternatives = usable.map(term => '"' + term + '"').join(' OR ');
  const labelFilter = label ? ' label:"' + label.replace(/["\\]/g, '') + '"' : '';
  return 'repo:' + repo + ' is:issue' + labelFilter + ' (' + alternatives + ')';
}

// Bounded strategy: one label-filtered query, followed by one keyword fallback.
export function buildSearchQueries(repo, issue) {
  const signals = extractIssueSignals(issue);
  const labels = [...signals.labels];
  const preferredLabel =
    labels.find(label => CATEGORY_LABELS.has(label)) ??
    labels.find(label => FALLBACK_LABEL_PATTERN.test(label));
  const primary = buildSearchQuery(repo, signals.searchTerms, preferredLabel ?? null);
  const fallback = buildSearchQuery(repo, signals.searchTerms);
  return [...new Set([primary, fallback].filter(Boolean))].slice(0, MAX_SEARCH_QUERIES);
}

export function scoreCandidate(currentIssue, candidate, current = extractIssueSignals(currentIssue)) {
  if (!candidate || candidate.pull_request || candidate.number === currentIssue.number) return null;
  const other = extractIssueSignals(candidate);
  const score = { value: 0, reasons: [], matchedTerms: [] };
  if (current.references.includes(candidate.number)) {
    score.value += 20;
    score.reasons.push('本文で #' + candidate.number + ' を明示的に参照');
  }
  const sharedIds = current.identifiers.filter(id => other.identifiers.includes(id));
  if (sharedIds.length) {
    score.value += 12 * sharedIds.length;
    score.reasons.push('エラー識別子が一致: ' + sharedIds.slice(0, MAX_IDENTIFIER_REASONS).join(', '));
  }
  // Compare signals in both directions. Otherwise a related term that survives
  // the 80-term cap on only one issue can be missed depending on which issue
  // triggered the search.
  const sharedTerms = new Map();
  for (const [term, weight] of [...current.terms, ...other.terms]) {
    if (term.length < 3 || STOPWORDS.has(term)) continue;
    sharedTerms.set(term, Math.max(sharedTerms.get(term) ?? 0, weight));
  }
  const titleMatches = [];
  const bodyMatches = [];
  for (const [term, weight] of sharedTerms) {
    const inCurrentTitle = current.title.toLowerCase().includes(term) || current.normalizedTitle.includes(term);
    const inOtherTitle = other.title.toLowerCase().includes(term) || other.normalizedTitle.includes(term);
    const inCurrentBody = term.length >= MIN_BODY_TERM_LENGTH &&
      (current.body.toLowerCase().includes(term) || current.normalizedBody.includes(term));
    const inOtherBody = term.length >= MIN_BODY_TERM_LENGTH &&
      (other.body.toLowerCase().includes(term) || other.normalizedBody.includes(term));
    if ((inCurrentTitle && inOtherTitle) ||
        (inCurrentTitle && inOtherBody) ||
        (inOtherTitle && inCurrentBody)) {
      titleMatches.push({ term, weight });
    } else if (inCurrentBody && inOtherBody) {
      bodyMatches.push({ term, weight });
    }
  }
  const allMatches = [...titleMatches, ...bodyMatches];
  const meaningfulMatches = matches => matches.filter(match =>
    !allMatches.some(otherMatch =>
      otherMatch.term.length > match.term.length && otherMatch.term.includes(match.term)
    )
  );
  const distinctTitleMatches = meaningfulMatches(titleMatches);
  const distinctBodyMatches = meaningfulMatches(bodyMatches);
  score.matchedTerms = [...distinctTitleMatches, ...distinctBodyMatches]
    .sort((a, b) => b.weight - a.weight || b.term.length - a.term.length);
  score.value += Math.min(MAX_TITLE_MATCHES, distinctTitleMatches.length) * 2;
  score.value += Math.min(MAX_BODY_MATCHES, distinctBodyMatches.length);
  if (score.matchedTerms.length >= MIN_DISTINCT_MATCHES) {
    score.reasons.push(
      '特徴語が一致: ' +
      score.matchedTerms.slice(0, MAX_MATCHED_TERMS_IN_REASON).map(item => item.term).join('、'),
    );
  }
  const sharedLabels = [...current.labels].filter(
    label => other.labels.has(label) && CATEGORY_LABELS.has(label),
  );
  if (sharedLabels.length) {
    score.value += sharedLabels.length;
    if (score.matchedTerms.length >= MIN_DISTINCT_MATCHES) {
      score.reasons.push('分類ラベルも一致: ' + sharedLabels.join('、'));
    }
  }
  if (
    !score.reasons.length ||
    (score.value < MIN_CANDIDATE_SCORE &&
      !current.references.includes(candidate.number) &&
      sharedIds.length === 0)
  ) {
    return null;
  }
  if (
    !current.references.includes(candidate.number) &&
    sharedIds.length === 0 &&
    score.matchedTerms.length < MIN_DISTINCT_MATCHES
  ) {
    return null;
  }
  return { issue: candidate, score: score.value, reasons: score.reasons };
}

function rankCandidates(issue, candidates, maxCandidates, currentSignals) {
  return [...candidates.values()]
    .map(candidate => scoreCandidate(issue, candidate, currentSignals))
    .filter(Boolean)
    .sort((a, b) => b.score - a.score || b.issue.number - a.issue.number)
    .slice(0, maxCandidates);
}

export async function findRelatedIssues(
  issue,
  { repo, search, getIssue, maxCandidates = MAX_CANDIDATES },
) {
  if (!issue || issue.pull_request || !repo) return [];
  const current = extractIssueSignals(issue);
  const candidates = new Map();
  for (const number of current.references) {
    if (number === issue.number || candidates.has(number)) continue;
    try {
      const found = await getIssue(number);
      if (found) candidates.set(found.number, found);
    } catch {
      // References to deleted or inaccessible issues are ignored.
    }
  }
  // Try the label-scoped query first. Use the keyword fallback only if it yields
  // no evidence-backed candidates; this keeps the normal path to one search request.
  const queries = buildSearchQueries(repo, issue);
  for (const query of queries) {
    const result = await search(query);
    for (const candidate of (result?.items ?? result ?? [])) {
      if (candidate?.number && !candidate.pull_request) candidates.set(candidate.number, candidate);
    }
    const ranked = rankCandidates(issue, candidates, maxCandidates, current);
    if (ranked.length > 0) return ranked;
  }
  return rankCandidates(issue, candidates, maxCandidates, current);
}

function safeTitle(title) {
  return String(title ?? '').replace(/[\r\n]+/g, ' ').replace(/[<>]/g, '')
    .replace(/([\\\x60*_{}\[\]()#+.!|])/g, '\\$1').slice(0, 180);
}
export function formatRelatedIssueComment(issue, candidates, repo = '') {
  const lines = [
    '関連Issueの候補（自動検出）', '',
    '文章の一致だけでは関連性を確定できません。リンク先の本文を確認して判断してください。', '',
  ];
  if (!candidates.length) lines.push('今回の検索では、十分な根拠がある候補は見つかりませんでした。');
  else for (const candidate of candidates) {
    const number = candidate.issue.number;
    const url = candidate.issue.html_url ?? 'https://github.com/' + repo + '/issues/' + number;
    lines.push('- [#' + number + ' ' + safeTitle(candidate.issue.title) + '](' + url + ')');
    for (const reason of candidate.reasons) lines.push('  - ' + reason);
  }
  lines.push('', '<sub>候補の提示のみを行います。Issueのラベルや状態は変更しません。</sub>');
  return lines.join('\n');
}
