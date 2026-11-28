// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// benizara (紅皿、MIT License、Copyright (c) 2018 綾木健一郎) の配列定義ファイル (.bnz) から、同時打鍵のかな配列の表
// dictionaries/kana-layouts-benizara.txt を作る (形式は dictionaries/kana-layouts.txt と同じ)。
//   git clone https://github.com/k-ayaki/benizara
//   node tools/make-kana-layouts.mjs benizara > dictionaries/kana-layouts-benizara.txt
//
// 打つキーは日本語キーボードの刻印の英字・記号 (q、;、@ など)、親指キーは L (無変換)・R (変換)、Space、小指シフトは Shift。
// 同時に押すキーは + でつなぎ、最後が文字のキー。
// 機能キー (BackSpace・Enter・矢印など)・英字のモードの切り替え・カーソルを動かすかっこの対は入れない
// (Meltype の変換ボックスの外の操作なので)。
import fs from 'node:fs';
import path from 'node:path';

const dir = process.argv[2];

// [Meltype の名前, ファイル, 配列の名前, 小指シフトの面をかなに使うか]
const layouts = [
  ['NewJis', '新JISかな配列.bnz', '新JIS かな配列', true],
  ['Asuka', '飛鳥123.bnz', '飛鳥配列', false],
  ['Geta', '下駄配列.bnz', '下駄配列', false],
  ['ShinGeta', '新下駄.bnz', '新下駄配列', false],
  ['Naginata', '薙刀式配列v15Fix版(JIS横書き).bnz', '薙刀式 (v15)', false],
  ['Hachimitsu', '蜂蜜小梅.bnz', '蜂蜜小梅配列', false],
  ['ShinHachimitsu', 'シン蜂蜜小梅配列(小指シフトで英大文字).bnz', 'シン蜂蜜小梅配列', false],
  ['Yodaka', 'よだか配列.bnz', 'よだか配列', false],
  ['Kawasemi', 'かわせみ配列.bnz', 'かわせみ配列', false],
];

// 表の段 (数字の段・Q の段・A の段・Z の段) のキー。KanaLayoutEngine.Rows と同じ並び
const rows = ['1234567890-^\\', 'qwertyuiop@[', 'asdfghjkl;:]', 'zxcvbnm,./_'];

const sections = {
  'ローマ字シフト無し': '',
  'ローマ字左親指シフト': 'L',
  'ローマ字右親指シフト': 'R',
  'ローマ字スペースシフト': 'Space',
  'ローマ字小指シフト': 'Shift',
};

// 全角の記号は、日本語入力で打ったときの文字にする (，→ 、 など)。そのほかの全角英数字・記号は半角にしない (全角のまま入れる)
const symbols = { '，': '、', '．': '。', '－': 'ー', '／': '・', '［': '「', '］': '」', '濁': '゛', '半': '゜' };

function read(file) {
  const bytes = fs.readFileSync(path.join(dir, file));
  return bytes[0] === 0xff && bytes[1] === 0xfe ? new TextDecoder('utf-16le').decode(bytes.subarray(2))
    : (() => {
      // UTF-8 のファイルと Shift_JIS のファイルがある
      try { return new TextDecoder('utf-8', { fatal: true }).decode(bytes); } catch { return new TextDecoder('shift_jis').decode(bytes); }
    })();
}

function cell(text) {
  // 全角の数字は半角にする (ふつうに数字を打ったときと同じに、3じ → 3時 と変換できるように)
  const value = /^[０-９]$/.test(text) ? String.fromCharCode(text.charCodeAt(0) - 0xfee0) : symbols[text] ?? text;
  if (/^[0-9]$/.test(value)) return value;
  // かな (小書き・ゔ・゛゜を含む)・長音・句読点・全角の記号だけ。機能キー (消・入・左…) や '…' で囲んだ文字列は入れない
  if (/^[ぁ-ゖ゛゜ー、。・「」]+$/.test(value)) return value;
  if (/^[！-～～]$/.test(value) && !/^[Ａ-Ｚａ-ｚ]$/.test(value)) return value;
  return null;
}

function parse(file, pinkyKana) {
  const lines = read(file).split(/\r?\n/);
  const entries = new Map();
  let section = null, chord = null, row = 0;
  for (const raw of lines) {
    const line = raw.trim();
    if (line.startsWith(';') || line === '') continue;
    const header = /^\[(.+)\]$/.exec(line);
    if (header) {
      // [英数シフト無し ローマ字シフト無し] のように複数の面をまとめた見出しもある
      const names = header[1].split(/\s+/).filter(name => name in sections);
      section = names.length > 0 ? sections[names[0]] : null;
      if (section === 'Shift' && !pinkyKana) section = null;
      chord = [];
      row = 0;
      continue;
    }
    if (section === null) continue;
    if (/^[<{]/.test(line)) {
      // <k>・{j}<*>・{j}{w}<*> のどれも、* 以外のキーを同時に押すキーとして読む (最初の書き方だけを見る)
      const first = line.split(/\s+/)[0];
      chord = [...first.matchAll(/[<{]([^>}]+)[>}]/g)].map(m => m[1]).filter(key => key !== '*');
      row = 0;
      continue;
    }
    if (row >= rows.length) continue;
    const cells = line.split(',');
    const keys = [...rows[row]];
    row++;
    const shift = [...(section ? [section] : []), ...chord];
    // 同時に押すキーが表の段に無い (機能キーの面) なら入れない
    if (chord.some(key => !rows.some(r => r.includes(key)))) continue;
    for (let i = 0; i < keys.length && i < cells.length; i++) {
      const value = cell(cells[i].trim());
      if (value === null || shift.includes(keys[i])) continue;
      const id = `${[...shift].sort().join('+')}\t${keys[i]}`;
      if (!entries.has(id)) entries.set(id, value);
    }
  }
  return [...entries].map(([id, value]) => `${id}\t${value}`);
}

const out = [
  '# 同時打鍵のかな配列の表。作成元: tools/make-kana-layouts.mjs (手で直さない)',
  '# 表は benizara (https://github.com/k-ayaki/benizara、MIT License、Copyright (c) 2018 綾木健一郎) の定義ファイルから取った',
  '# 形式は kana-layouts.txt と同じ: 配列<Tab>打つキー<Tab>かな',
];
for (const [name, file, , pinky] of layouts) {
  for (const entry of parse(file, pinky)) {
    const [chord, key, kana] = entry.split('\t');
    out.push(`${name}\t${chord === '' ? key : `${chord}+${key}`}\t${kana}`);
  }
}
process.stdout.write(out.join('\n') + '\n');
