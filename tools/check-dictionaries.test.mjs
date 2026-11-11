// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 souta

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { checkDictionary } from './check-dictionaries.mjs';

const messages = problems => problems.map(p => `${p.line}: ${p.message}`);

test('english-phrases: 行末のコメントはエラー (Core はこの行を登録しない)。行頭の # はコメント', () => {
  const { errors, warnings } = checkDictionary('english-phrases.txt', '# 見出し\nseeyou\tsee you\nbytheway\tby the way # note\n');
  assert.deepEqual(messages(errors), ['3: 行末にコメントは書けない (コメントは行頭の # だけ。行末に書くとこの行は読み込まれない)']);
  assert.deepEqual(warnings, []);
});

test('english-phrases: 同じ繋げた形に違う区切りはエラー (Core は後の行で上書きする)', () => {
  const { errors } = checkDictionary('english-phrases.txt', 'seeyou\tsee you\nseeyou\tse ey ou\n');
  assert.deepEqual(messages(errors), ['2: 「seeyou」が 1 行目と食い違う (「see you」と「se ey ou」)']);
});

test('english-phrases: まったく同じ行は警告だけ、別の繋げた形は通す', () => {
  const { errors, warnings } = checkDictionary('english-phrases.txt', 'seeyou\tsee you\nseeyou\tsee you\nseeyouagain\tsee you again\n');
  assert.deepEqual(errors, []);
  assert.deepEqual(messages(warnings), ['2: 1 行目と同じ行']);
});

test('english-phrases: 繋げた形と語句が合わない行はエラー', () => {
  const { errors } = checkDictionary('english-phrases.txt', 'seeyou\tsee me\n');
  assert.deepEqual(messages(errors), ['1: 「seeyou」が語句「see me」を繋げた形でない']);
});

test('english-readable: 複数の語を置いた行でも、語ごとに重複を見る', () => {
  const { errors, warnings } = checkDictionary('english-readable.txt', 'open close\nopen # 注記\nclosed\n');
  assert.deepEqual(errors, []);
  assert.deepEqual(messages(warnings), ['2: 「open」が 1 行目と重複']);
});

test('english-readable: 同じ行を 2 度置くと語ごとに警告', () => {
  const { warnings } = checkDictionary('english-readable.txt', 'feature\nfeature\n');
  assert.deepEqual(messages(warnings), ['2: 1 行目と同じ行']);
});

test('ふつうの辞書は行全体で重複を見て、行末の # はコメント', () => {
  const { errors, warnings } = checkDictionary('english.txt', 'hello world # 注記\nhello world\nhello\n');
  assert.deepEqual(errors, []);
  assert.deepEqual(messages(warnings), ['2: 1 行目と同じ行']);
});
