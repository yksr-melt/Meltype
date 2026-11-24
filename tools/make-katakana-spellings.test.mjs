// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const script = fileURLToPath(new URL('./make-katakana-spellings.mjs', import.meta.url));
const glosses = (...words) => words.map(w => `<gloss>${w}</gloss>`).join('');
const entry = (reading, senses) => `<entry><r_ele><reb>${reading}</reb></r_ele>${senses}</entry>`;
const sense = (...words) => `<sense>${glosses(...words)}</sense>`;
// CMUdict の発音の抜粋。著作権・BSD-2-Clause の全文は THIRD-PARTY-NOTICES.md。
const phones = `test T EH1 S T
file F AY1 L
key K IY1
mode M OW1 D
time T AY1 M
set S EH1 T
image IH1 M AH0 JH
cookie K UH1 K IY0
biscuit B IH1 S K AH0 T
delete D IH0 L IY1 T
note N OW1 T
memo M EH1 M OW0
binding B AY1 N D IH0 NG
calibration K AE2 L AH0 B R EY1 SH AH0 N
adopting AH0 D AA1 P T IH0 NG
herb ER1 B
herb(2) HH ER1 B # 別の発音も照合する
export EH1 K S P AO0 R T
application AE2 P L AH0 K EY1 SH AH0 N
game G EY1 M
camera K AE1 M ER0 AH0
series S IH1 R IY0 Z
visitor V IH1 Z IH0 T ER0
scramble S K R AE1 M B AH0 L
bank B AE1 NG K
`;

function generate(xml) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'katakana-spellings-'));
  try {
    const source = path.join(dir, 'JMdict.xml');
    const common = path.join(dir, 'english.txt');
    const cmu = path.join(dir, 'cmudict.dict');
    fs.writeFileSync(source, xml);
    fs.writeFileSync(common, 'test\r\nfile\r\nkey\r\nmode\r\ntime\r\nset\r\nimage\r\ncookie\r\nbiscuit\r\ndelete\r\nnote\r\nmemo\r\nbinding\r\ncalibration\r\nadopting\r\nherb\r\nexport\r\napplication\r\ngame\r\ncamera\r\nseries\r\nvisitor\r\nscramble\r\nbank\r\nunlisted\r\n');
    fs.writeFileSync(cmu, phones);
    const run = spawnSync(process.execPath, [script, source, common, cmu], { encoding: 'utf8' });
    assert.equal(run.status, 0, run.stdout + run.stderr);
    return new Map(run.stdout.split('\n').filter(l => l && !l.startsWith('#')).map(l => {
      const [word, ...spellings] = l.split(' ');
      return [word, spellings];
    }));
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
}

test('括弧の説明が付いた日常語も収録する。入れ子の括弧と CRLF にも対応する', () => {
  const data = generate([
    entry('テスト', sense('test (of ability, knowledge, etc.)')),
    entry('ファイル', sense('file (for holding papers)')),
    entry('キー', sense('key (for a lock)')),
    entry('モード', sense('mode (e.g. of a machine (or device))')),
    entry('タイム', sense('time (for, to do)')),
    entry('セット', sense('set (of things)')),
    entry('イメージ', sense("image (in one's mind)")),
  ].join(''));
  assert.deepEqual(Object.fromEntries(data), { file: ['ファイル'], image: ['イメージ'], key: ['キー'], mode: ['モード'], set: ['セット'], test: ['テスト'], time: ['タイム'] });
});

test('英訳が一致しても読みが違う組は除き、同じ項目の正しい綴りと別の意味の読みは残す', () => {
  const data = generate([
    entry('クッキー', sense('cookie', 'biscuit')),
    entry('ビスケット', sense('cookie', 'biscuit')),
    entry('デリート', sense('delete')),
    entry('トル', sense('delete')),
    entry('メモ', sense('note', 'memo')),
    entry('ノート', `<sense><misc>&abbr;</misc>${glosses('notebook')}</sense>${sense('note')}`),
    entry('バインド', sense('binding')),
    entry('バインディング', sense('binding')),
    entry('キャリブレート', sense('calibration')),
    entry('キャリブレーション', sense('calibration')),
    entry('アドプト', sense('adopting')),
  ].join(''));
  assert.deepEqual(Object.fromEntries(data), { binding: ['バインディング'], biscuit: ['ビスケット'], calibration: ['キャリブレーション'], cookie: ['クッキー'], delete: ['デリート'], memo: ['メモ'], note: ['ノート'] });
});

test('外国語由来・和製英語・俗語・方言・まれな読みは除き、意味の読みの制限を守る', () => {
  const blocked = [
    '<lsource xml:lang="ger">Test</lsource>', '<lsource ls_wasei="y">test</lsource>', '<misc>&sl;</misc>', '<dial>&ksb;</dial>',
  ].map(tag => entry('テスト', `<sense>${tag}${glosses('test')}</sense>`));
  blocked.push(entry('てすと', sense('test')), `<entry><k_ele><keb>試験</keb></k_ele><r_ele><reb>テスト</reb></r_ele>${sense('test')}</entry>`);
  assert.equal(generate(blocked.join('')).size, 0);
  const data = generate(`<entry>
    <r_ele><reb>ファイル</reb></r_ele><r_ele><reb>フィール</reb></r_ele>
    <r_ele><reb>フィル</reb><re_inf>&sk;</re_inf></r_ele>
    <sense><stagr>ファイル</stagr><lsource>file</lsource>${glosses('file')}</sense>
    </entry>${entry('テスト', `<sense><lsource xml:lang="eng">test</lsource>${glosses('test')}</sense>`)}`);
  assert.deepEqual(Object.fromEntries(data), { file: ['ファイル'], test: ['テスト'] });
});

test('発音の別形も使い、発音の無い語・複数語の訳は推測して入れない', () => {
  const data = generate(entry('ハーブ', sense('herb')) + entry('テスト', sense('unlisted', 'test case')));
  assert.deepEqual(Object.fromEntries(data), { herb: ['ハーブ'] });
});

test('長音になる R、シ/ス・ジ/ズ、子音の前の鼻音の違いで、よく使う語を落とさない', () => {
  const data = generate([
    entry('エクスポート', sense('export')), entry('カメラ', sense('camera')), entry('シリーズ', sense('series')),
    entry('ビジター', sense('visitor')), entry('スクランブル', sense('scramble')), entry('バンク', sense('bank')),
  ].join(''));
  assert.deepEqual(Object.fromEntries(data), { bank: ['バンク'], camera: ['カメラ'], export: ['エクスポート'], scramble: ['スクランブル'], series: ['シリーズ'], visitor: ['ビジター'] });
});

test('同梱する辞書にも、報告された語と正しい対の候補が入り、誤った組は入らない', () => {
  const dictionary = new Map(fs.readFileSync(new URL('../dictionaries/katakana-spellings.txt', import.meta.url), 'utf8')
    .split('\n').filter(l => l && !l.startsWith('#')).map(l => {
      const [word, ...spellings] = l.split(' ');
      return [word, spellings];
    }));
  for (const [word, spelling] of Object.entries({ test: 'テスト', file: 'ファイル', key: 'キー', mode: 'モード', time: 'タイム', set: 'セット', image: 'イメージ', cookie: 'クッキー', biscuit: 'ビスケット', delete: 'デリート', note: 'ノート', memo: 'メモ', binding: 'バインディング', calibration: 'キャリブレーション', export: 'エクスポート', application: 'アプリケーション', game: 'ゲーム', camera: 'カメラ', series: 'シリーズ', visitor: 'ビジター', scramble: 'スクランブル', bank: 'バンク' })) {
    assert.ok(dictionary.get(word)?.includes(spelling), `${word}: ${spelling} が無い`);
  }
  for (const [word, spelling] of Object.entries({ cookie: 'ビスケット', delete: 'トル', note: 'メモ', binding: 'バインド', calibration: 'キャリブレート', adopting: 'アドプト' })) {
    assert.ok(!dictionary.get(word)?.includes(spelling), `${word}: ${spelling} が残っている`);
  }
});
