// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// dictionaries/*.txt の形式を確かめる (Pull Request のチェック「辞書の形式」で使う)。
//   node tools/check-dictionaries.mjs [ファイル …]   … 省略するとすべての辞書
// 読み込めない行 (エラー) があれば終了コード 1。直した方がよい行 (警告) は表示だけ。
import fs from 'node:fs';
import path from 'node:path';

const root = path.join(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')), '..', 'dictionaries');
const hiragana = /^[ぁ-ゖー゛゜ゔ]+$/;
const kana = /^[ぁ-ゖァ-ヺー・ゔヴ]+$/;
const ascii = /^[\x21-\x7e]+$/;

/** 辞書ごとの形式。line は # 以降を除いた行。 */
const formats = {
  // 読み 候補 候補 … (空白区切り)。読みは打ったときの読みと完全一致で引くので、キーを打って作れる文字列だけを通す。
  // 記号の行 (/ @ . ,) は読みが記号。ローマ字入力で全角になる記号 (-> の読みは ー＞) も通す。全角の英数字は打っても出てこない
  'candidates.txt': spaced((reading, rest) => [
    hiragana.test(reading) || /^[\x21-\x7e]+$/.test(reading) || /^[ー！-／：-＠［-｀｛-～]+$/.test(reading) ? null : `読み「${reading}」がひらがなでも記号でもない`,
    rest.length === 0 ? '候補が無い' : null,
  ]),
  'brands.txt': spaced((reading, rest) => [
    hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
    rest.length === 0 ? '候補が無い' : null,
    ...rest.filter(w => !ascii.test(w.replace(/[ÈÉÊËÀÂÔÛÙ'’&-]/g, 'x'))).map(w => `「${w}」が英字でない`),
  ]),
  'loanwords.txt': spaced((reading, rest) => [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`, rest.length === 0 ? '候補が無い' : null]),
  'propernouns.txt': words(w => /^[A-Za-z][A-Za-z0-9.+&'-]*$/.test(w) ? null : `「${w}」が英字の語でない`),
  'english.txt': words(w => /^[a-z][a-z0-9_'-]*$/.test(w) ? null : `「${w}」が小文字の英字の語でない`),
  'english-words.txt': words(w => /^[a-z]{2,}$/.test(w) ? null : `「${w}」が小文字の英字の語でない`),
  'japanese.txt': words(w => /^[a-z]+$/.test(w) ? null : `「${w}」が小文字のローマ字でない`),
  'contexts.txt': line => {
    const [left, right] = line.split(' : ');
    if (right === undefined) return ['「読み 候補 : 手がかり …」の形でない'];
    const [reading, word, extra] = left.trim().split(/\s+/);
    return [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`, word ? null : '候補が無い', extra ? '候補は 1 つだけ' : null, right.trim() ? null : '手がかりが無い'];
  },
  // 読み<Tab>語句。同じ数の | で区切ると別の文節にする (いち|がっき<Tab>一|学期)
  'phrases.txt': tabbed(2, 2, ([reading, word]) => [
    reading.replace(/^\^/, '').split('|').every(r => hiragana.test(r)) ? null : `読み「${reading}」がひらがなでない`,
    reading.split('|').length === word.split('|').length && !word.split('|').includes('') ? null : `読み「${reading}」と語句「${word}」の | の数が違う`,
  ]),
  // 読み<Tab>語句<Tab>出し始める読み (予測変換の定型句)。出し始める読みは、読みの打ちかけ
  'greetings.txt': tabbed(3, 3, ([reading, , start]) => [
    hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
    reading.startsWith(start) && start.length < reading.length ? null : `出し始める読み「${start}」が、読み「${reading}」の打ちかけでない`,
  ]),
  'misspellings.txt': tabbed(1, 2, fields => fields.map(f => kana.test(f) ? null : `「${f}」がかなでない`)),
  'translations.txt': tabbed(3, 3, ([, kind]) => [['n', 'na', 'i', 'v', 'adv'].includes(kind) ? null : `品詞「${kind}」が n / na / i / v / adv でない`]),
  'meanings.txt': tabbed(3, 3, ([, reading]) => [reading === '' || hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`]),
  'readings.txt': tabbed(1, 2, ([reading]) => [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`]),
  'readings-extra.txt': tabbed(1, 2, ([reading]) => [
    hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
    [...reading].length >= 3 ? null : `読み「${reading}」が 3 文字より短い`,
  ]),
  'katakana.txt': tabbed(1, 1, ([reading]) => [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`]),
  'emoji.txt': tabbed(2, 99, () => []),
  'emoji-cldr.txt': tabbed(2, 99, () => []),
};

function spaced(check) {
  return line => {
    const [reading, ...rest] = line.split(/[ \t　]+/).filter(Boolean);
    return check(reading, rest);
  };
}

function words(check) {
  return line => line.split(/\s+/).filter(Boolean).map(check);
}

function tabbed(min, max, check) {
  return line => {
    const fields = line.split('\t');
    if (fields.length < min || fields.length > max) return [`タブ区切りの欄が ${fields.length} 個 (${min === max ? min : `${min}〜${max}`} 個のはず)`];
    if (fields.some(f => f !== f.trim())) return ['欄の前後に空白がある'];
    return check(fields);
  };
}

const files = process.argv.slice(2).length > 0
  ? process.argv.slice(2).map(f => path.basename(f)).filter(f => f.endsWith('.txt'))
  : fs.readdirSync(root).filter(f => f.endsWith('.txt'));
let errors = 0, warnings = 0;
const report = [];
for (const file of files) {
  const full = path.join(root, file);
  if (!fs.existsSync(full)) continue; // 消した辞書
  const format = formats[file];
  const text = fs.readFileSync(full, 'utf8');
  if (text.charCodeAt(0) === 0xfeff) { report.push(`::warning file=dictionaries/${file}::先頭に BOM がある`); warnings++; }
  if (!format) { report.push(`::warning file=dictionaries/${file}::形式を確かめる決まりが無い辞書 (tools/check-dictionaries.mjs に足してください)`); warnings++; continue; }
  const seen = new Map();
  text.split('\n').forEach((raw, i) => {
    const lineNo = i + 1;
    let line = raw.replace(/\r$/, '');
    // 絵文字の辞書は顔文字に # が入るので、行頭の # だけがコメント
    const hash = file.startsWith('emoji') ? (line.trimStart().startsWith('#') ? 0 : -1) : line.indexOf('#');
    if (hash >= 0) line = line.slice(0, hash);
    if (line.trim() === '') return;
    if (/[ \t]$/.test(raw.replace(/\r$/, '')) && hash < 0) { report.push(`::warning file=dictionaries/${file},line=${lineNo}::行の終わりに空白がある`); warnings++; }
    for (const problem of format(line.trim() === line || file.startsWith('emoji') ? line : line.trim()).filter(Boolean)) {
      report.push(`::error file=dictionaries/${file},line=${lineNo}::${problem}`);
      errors++;
    }
    const key = line.trim();
    if (seen.has(key)) { report.push(`::warning file=dictionaries/${file},line=${lineNo}::${seen.get(key)} 行目と同じ行`); warnings++; }
    else seen.set(key, lineNo);
  });
}
console.log(report.join('\n'));
console.log(`辞書の形式: ${files.length} ファイル、エラー ${errors}、警告 ${warnings}`);
process.exitCode = errors > 0 ? 1 : 0;
