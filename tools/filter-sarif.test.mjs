// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { filter } from './filter-sarif.mjs';

test('既知の移行提案だけを外し、警告・エラー・セキュリティの指摘は残す', () => {
  const kept = [
    { ruleId: 'SYSLIB1054', level: 'warning' },
    { ruleId: 'SYSLIB1045', level: 'error' },
    { ruleId: 'CA1305', level: 'warning' },
    { ruleId: 'security', level: 'note' },
    { ruleId: 'SYSLIB1096' },
  ];
  const sarif = { runs: [{ results: [
    { ruleId: 'SYSLIB1054', level: 'note' },
    { ruleId: 'SYSLIB1045', level: 'note' },
    { ruleId: 'SYSLIB1096', level: 'note' },
    ...kept,
  ] }] };
  assert.deepEqual(filter(sarif), { migrations: 3, naming: 0 });
  assert.deepEqual(sarif.runs[0].results, kept);
  assert.deepEqual(filter(sarif), { migrations: 0, naming: 0 });
});

test('ルール既定の重要度を見て、抑制済みの結果と複数の run も保つ', () => {
  const suppressed = { ruleId: 'SYSLIB1054', level: 'note', suppressions: [{ kind: 'inSource' }] };
  const warning = { ruleId: 'SYSLIB1096' };
  const sarif = { runs: [
    { tool: { driver: { rules: [{ id: 'SYSLIB1045', defaultConfiguration: { level: 'note' } }] } }, results: [{ ruleId: 'SYSLIB1045' }, suppressed] },
    { results: [warning] },
  ] };
  assert.deepEqual(filter(sarif), { migrations: 1, naming: 0 });
  assert.deepEqual(sarif.runs.map(r => r.results), [[suppressed], [warning]]);
});

test('命名は理由付きのソース内抑制だけを反映し、未抑制・理由なし・外部抑制・エラーを残す', () => {
  const kept = [
    { ruleId: 'CA1711', level: 'warning' },
    { ruleId: 'CA1711', level: 'warning', suppressions: [{ kind: 'inSource' }] },
    { ruleId: 'CA1711', level: 'warning', suppressions: [{ kind: 'external', justification: '外部の設定' }] },
    { ruleId: 'CA1711', level: 'error', suppressions: [{ kind: 'inSource', justification: '公開名' }] },
    { ruleId: 'CA1711', level: 'warning', suppressions: [{ kind: 'inSource', status: 'rejected', justification: '公開名' }] },
    { ruleId: 'security', level: 'warning', suppressions: [{ kind: 'inSource', justification: 'これは除かない' }] },
  ];
  const sarif = { runs: [{ results: [
    { ruleId: 'CA1711', level: 'warning', suppressions: [{ kind: 'inSource', justification: '日本語入力の辞書' }] },
    { ruleId: 'CA1720', level: 'warning', suppressions: [{ kind: 'inSource', justification: '文字列リテラルの中' }] },
    ...kept,
  ] }] };
  assert.deepEqual(filter(sarif), { migrations: 0, naming: 2 });
  assert.deepEqual(sarif.runs[0].results, kept);
});
