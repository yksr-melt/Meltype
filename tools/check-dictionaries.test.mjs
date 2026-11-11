// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 souta

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
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

test('english-phrases: 行頭の空白はエラー (Core は繋げた形が合わず登録しない)', () => {
  const { errors } = checkDictionary('english-phrases.txt', ' bytheway\tby the way\n');
  assert.deepEqual(messages(errors), ['1: 欄の前後に空白がある']);
});

test('english-phrases: 行末の余分なタブはエラー (Core は欄の数が合わず登録しない)', () => {
  const { errors, warnings } = checkDictionary('english-phrases.txt', 'thankyou\tthank you\t\n');
  assert.deepEqual(messages(errors), ['1: タブ区切りの欄が 3 個 (2 個のはず)']);
  assert.deepEqual(messages(warnings), ['1: 行の終わりに空白がある']);
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

test('空白を含むパスに置いても辞書を検査する', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'check dictionaries '));
  try {
    fs.mkdirSync(path.join(dir, 'tools'));
    fs.mkdirSync(path.join(dir, 'dictionaries'));
    fs.copyFileSync(fileURLToPath(new URL('./check-dictionaries.mjs', import.meta.url)), path.join(dir, 'tools', 'check-dictionaries.mjs'));
    fs.writeFileSync(path.join(dir, 'dictionaries', 'english-phrases.txt'), 'seeyou\tsee me\n');
    const run = spawnSync(process.execPath, [path.join(dir, 'tools', 'check-dictionaries.mjs')], { encoding: 'utf8' });
    assert.equal(run.status, 1, run.stdout + run.stderr);
    assert.match(run.stdout, /辞書の形式: 1 ファイル、エラー 1、/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('シンボリックリンク経由で起動しても辞書を検査する', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'check-dictionaries-'));
  try {
    fs.mkdirSync(path.join(dir, 'tools'));
    fs.mkdirSync(path.join(dir, 'dictionaries'));
    fs.copyFileSync(fileURLToPath(new URL('./check-dictionaries.mjs', import.meta.url)), path.join(dir, 'tools', 'check-dictionaries.mjs'));
    fs.writeFileSync(path.join(dir, 'dictionaries', 'english-phrases.txt'), 'seeyou\tsee me\n');
    fs.symlinkSync(path.join(dir, 'tools', 'check-dictionaries.mjs'), path.join(dir, 'link.mjs'));
    const run = spawnSync(process.execPath, [path.join(dir, 'link.mjs')], { encoding: 'utf8' });
    assert.equal(run.status, 1, run.stdout + run.stderr);
    assert.match(run.stdout, /辞書の形式: 1 ファイル、エラー 1、/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('english: 日本語由来の節では行末のコメント・タブ・大文字はエラー (Core は # 以降も語として読み、空白でだけ区切り、小文字にしない)', () => {
  const { errors } = checkDictionary('english.txt', '# --- 日本語由来の語 ---\nsushi ramen # 注記\n# 節の中のコメント行\ntofu\tmiso\nSake\n\nhello # 注記\nworld\tWide\n');
  assert.deepEqual(messages(errors), [
    '2: 日本語由来の節では行末にコメントを書けない (Core は # 以降も日本語由来の語として読む)',
    '4: 日本語由来の節では語をタブで区切れない (Core は空白でだけ区切る)',
    '5: 日本語由来の節では大文字を使えない (Core は小文字にせずに読むので、小文字で引いても見つからない)',
  ]);
});

test('english: 日本語由来を含まない見出しの節は、ふつうの語として見る', () => {
  const { errors, warnings } = checkDictionary('english.txt', '# --- 日本語辞書 (v2) の語と先頭が重なる英単語 ---\nhello # 注記\n# --- 技術用語 ---\nworld\tWide\n');
  assert.deepEqual(errors, []);
  assert.deepEqual(warnings, []);
});

test('english-readable / english: 大文字小文字だけ違う語は、Core と同じく同じ語の重複として警告', () => {
  assert.deepEqual(checkDictionary('english-readable.txt', 'open\nOpen\n'), { errors: [], warnings: [{ line: 2, message: '「open」が 1 行目と重複' }] });
  assert.deepEqual(checkDictionary('english.txt', 'hello\nHello\n'), { errors: [], warnings: [{ line: 2, message: '「hello」が 1 行目と重複' }] });
});

test('contexts: : の前後の空白は問わない (Core は最初の : で分ける)', () => {
  const { errors } = checkDictionary('contexts.txt', 'あつい 暑い :気温\nあつい 暑い: 天気\nあつい 暑い:夏\nあつい　暑い\t:\t季節\n');
  assert.deepEqual(errors, []);
});

test('contexts: 読み・候補・手がかりが揃わない行はエラー', () => {
  const { errors } = checkDictionary('contexts.txt', 'あつい 暑い 気温\nあつい : 気温\nあつい 暑い 熱い : 気温\nあつい 暑い :\n');
  assert.deepEqual(messages(errors), ['1: 「読み 候補 : 手がかり …」の形でない', '2: 候補が無い', '3: 候補は 1 つだけ', '4: 手がかりが無い']);
});

test('candidates: 全角空白は Core と同じく区切り', () => {
  const { errors } = checkDictionary('candidates.txt', 'はし　橋　箸\n');
  assert.deepEqual(errors, []);
});
