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
  // 繋げた形<Tab>空白で区切った語句。繋げた形が、語句の空白を除いた形と一致することまで確かめる
  // コメントは行頭の # だけ (comments を見る)。行末に # を書くと Core はその行を登録しない
  'english-phrases.txt': noInlineComment(tabbed(2, 2, ([joined, phrase]) => [
    /^[a-z]+$/.test(joined) ? null : `「${joined}」が小文字の英字でない`,
    phrase.split(' ').every(w => /^[a-z]+$/.test(w)) ? null : `語句「${phrase}」が小文字の英字の語でない`,
    joined === phrase.replace(/ /g, '') ? null : `「${joined}」が語句「${phrase}」を繋げた形でない`,
  ])),
  // ローマ字として最後まで読めても英語にする語。ファイル冒頭の決まりどおり 3 文字以上だけ
  'english-readable.txt': words(w => /^[a-z]{3,}$/.test(w) ? null : `「${w}」が 3 文字以上の小文字の英字の語でない`),
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

function noInlineComment(check) {
  return line => line.includes('#') ? ['行末にコメントは書けない (コメントは行頭の # だけ。行末に書くとこの行は読み込まれない)'] : check(line);
}

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

/** コメントの始まり (無ければ -1)。Core の読み方に合わせる。ふつうは # から行末まで。 */
const comments = {
  // 顔文字に # が入るので、行頭の # だけがコメント
  'emoji.txt': line => line.trimStart().startsWith('#') ? 0 : -1,
  'emoji-cldr.txt': line => line.trimStart().startsWith('#') ? 0 : -1,
  // Core (EnglishPhraseSpacing) は 1 文字目の # だけをコメントとして読み飛ばす。行末の # 以降は欄に残り、その行は登録されない
  'english-phrases.txt': line => line.startsWith('#') ? 0 : -1,
};

/**
 * 重複を見る単位 ([キー, 中身] の並び)。ふつうは行全体。
 * キーが同じで中身が違えば、後の行が前の行を上書きするか、どちらが効くか決まらないのでエラー。中身まで同じなら警告。
 */
const keys = {
  // Core は繋げた形をキーに、後の行で上書きする
  'english-phrases.txt': line => { const [joined, phrase] = line.split('\t'); return [[joined, phrase]]; },
  // 1 行に複数の語を置けるので、語ごとに見る
  'english-readable.txt': line => line.split(/\s+/).filter(Boolean).map(w => [w, w]),
};

/** 辞書 file の本文 text を確かめ、{ errors, warnings } (どちらも { line, message } の並び。line は無いこともある) を返す。 */
export function checkDictionary(file, text) {
  const errors = [], warnings = [];
  if (text.charCodeAt(0) === 0xfeff) warnings.push({ message: '先頭に BOM がある' });
  const format = formats[file];
  if (!format) { warnings.push({ message: '形式を確かめる決まりが無い辞書 (tools/check-dictionaries.mjs に足してください)' }); return { errors, warnings }; }
  const commentAt = comments[file] ?? (line => line.indexOf('#'));
  const keysOf = keys[file] ?? (line => [[line.trim(), line.trim()]]);
  const seen = new Map();
  text.split('\n').forEach((raw, i) => {
    const lineNo = i + 1;
    let line = raw.replace(/\r$/, '');
    const hash = commentAt(line);
    if (hash >= 0) line = line.slice(0, hash);
    if (line.trim() === '') return;
    if (/[ \t]$/.test(raw.replace(/\r$/, '')) && hash < 0) warnings.push({ line: lineNo, message: '行の終わりに空白がある' });
    const problems = format(line.trim() === line || file.startsWith('emoji') ? line : line.trim()).filter(Boolean);
    for (const problem of problems) errors.push({ line: lineNo, message: problem });
    if (problems.length > 0) return; // 読めない行は重複を見ない
    for (const [key, value] of keysOf(line.trim())) {
      const first = seen.get(key);
      if (!first) seen.set(key, { lineNo, value, text: line.trim() });
      else if (first.value !== value) errors.push({ line: lineNo, message: `「${key}」が ${first.lineNo} 行目と食い違う (「${first.value}」と「${value}」)` });
      else warnings.push({ line: lineNo, message: first.text === line.trim() ? `${first.lineNo} 行目と同じ行` : `「${key}」が ${first.lineNo} 行目と重複` });
    }
  });
  return { errors, warnings };
}

if (process.argv[1] && path.resolve(process.argv[1]) === path.resolve(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))) {
  const files = process.argv.slice(2).length > 0
    ? process.argv.slice(2).map(f => path.basename(f)).filter(f => f.endsWith('.txt'))
    : fs.readdirSync(root).filter(f => f.endsWith('.txt'));
  let errors = 0, warnings = 0;
  const report = [];
  for (const file of files) {
    const full = path.join(root, file);
    if (!fs.existsSync(full)) continue; // 消した辞書
    const result = checkDictionary(file, fs.readFileSync(full, 'utf8'));
    const where = p => `file=dictionaries/${file}${p.line ? `,line=${p.line}` : ''}`;
    for (const p of result.errors) report.push(`::error ${where(p)}::${p.message}`);
    for (const p of result.warnings) report.push(`::warning ${where(p)}::${p.message}`);
    errors += result.errors.length;
    warnings += result.warnings.length;
  }
  console.log(report.join('\n'));
  console.log(`辞書の形式: ${files.length} ファイル、エラー ${errors}、警告 ${warnings}`);
  process.exitCode = errors > 0 ? 1 : 0;
}
