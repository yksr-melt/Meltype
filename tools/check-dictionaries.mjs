// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// dictionaries/*.txt の形式を確かめる (Pull Request のチェック「辞書の形式」で使う)。
//   node tools/check-dictionaries.mjs [ファイル …]   … 省略するとすべての辞書
// 読み込めない行 (エラー) があれば終了コード 1。直した方がよい行 (警告) は表示だけ。
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const self = fileURLToPath(import.meta.url);
const root = path.join(path.dirname(self), '..', 'dictionaries');
const hiragana = /^[ぁ-ゖー゛゜ゔ]+$/;
const kana = /^[ぁ-ゖァ-ヺー・ゔヴ]+$/;
const ascii = /^[\x21-\x7e]+$/;
// Core (WordList.ParseWords・ProperNouns.AddText) の語の区切り。全角空白は区切りでない (含むトークンは語にならず捨てられる)
const coreTokens = line => line.split(/[ \t\r,]+/).filter(Boolean);

/**
 * 辞書ごとの扱い。各辞書には既定 (checkDictionary) と違う項目だけを書く。
 *   format    … # 以降を除いた行を渡すと、エラーの並び (null は問題なし) を返す。必須
 *   commentAt … コメントの始まり (無ければ -1)。Core の読み方に合わせる。既定は # から行末まで
 *   untrimmed … 前後の空白を除かずに format に渡すか。既定は除く
 *   keysOf    … 重複を見る単位 ([キー, 中身] の並び)。既定は行全体。
 *               キーが同じで中身が違えば、後の行が前の行を上書きするか、どちらが効くか決まらないのでエラー。中身まで同じなら警告
 *   allowRepeats … 中身まで同じ重複を警告しないか。既定は警告する
 *   sections  … 節で決まりが変わる辞書の検査を作る関数。作った検査に 1 行ずつ (\r を除いた行全体) 渡すとエラーの並びを返す。既定は無し
 */
const dictionaries = {
  // 読み 候補 候補 … (空白区切り)。読みは打ったときの読みと完全一致で引くので、キーを打って作れる文字列だけを通す。
  // 記号の行 (/ @ . ,) は読みが記号。ローマ字入力で全角になる記号 (-> の読みは ー＞) も通す。全角の英数字は打っても出てこない
  'candidates.txt': {
    format: spaced((reading, rest) => [
      hiragana.test(reading) || /^[\x21-\x7e]+$/.test(reading) || /^[ー！-／：-＠［-｀｛-～]+$/.test(reading) ? null : `読み「${reading}」がひらがなでも記号でもない`,
      rest.length === 0 ? '候補が無い' : null,
    ]),
  },
  'brands.txt': {
    format: spaced((reading, rest) => [
      hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
      rest.length === 0 ? '候補が無い' : null,
      ...rest.filter(w => !ascii.test(w.replace(/[ÈÉÊËÀÂÔÛÙ'’&-]/g, 'x'))).map(w => `「${w}」が英字でない`),
    ]),
  },
  'loanwords.txt': {
    format: spaced((reading, rest) => [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`, rest.length === 0 ? '候補が無い' : null]),
  },
  'propernouns.txt': {
    // Core (ProperNouns) は空白・タブ・, で区切った語のうち、ASCII の英数字だけの語を登録し、それ以外は黙って捨てる
    format: words(w => /^[A-Za-z][A-Za-z0-9]*$/.test(w) ? null : `「${w}」が英字で始まる英数字だけの語でない (Core は登録しない)`),
    // Core は行を trim せず半角空白・タブ・, で区切るので、行頭・行末の全角空白は隣の語に付いたまま残り、その語は登録されない
    untrimmed: true,
    // Core は小文字で引き、先に出た綴りを正規形として表示する。小文字で同じ語に違う綴りがあると、後の綴りは使われない
    keysOf: line => coreTokens(line).map(w => [w.toLowerCase(), w]),
    // 分類ごとに同じ語を置いてよい (同じ綴りなら正規形は変わらない)
    allowRepeats: true,
  },
  // Core は小文字にして読むので、大文字を含んでも同じ語 (日本語由来の節は sections で見る)
  'english.txt': {
    format: words(w => /^[A-Za-z][A-Za-z0-9_'-]*$/.test(w) ? null : `「${w}」が英字の語でない`),
    // Core は小文字にして読む
    keysOf: line => [[line.trim().toLowerCase(), line.trim().toLowerCase()]],
    sections: japaneseOriginSection,
  },
  'english-words.txt': {
    format: words(w => /^[a-z]{2,}$/.test(w) ? null : `「${w}」が小文字の英字の語でない`),
  },
  // 繋げた形<Tab>空白で区切った語句。繋げた形が、語句の空白を除いた形と一致することまで確かめる
  'english-phrases.txt': {
    // コメントは行頭の # だけ (commentAt を見る)。行末に # を書くと Core はその行を登録しない
    format: noInlineComment(tabbed(2, 2, ([joined, phrase]) => [
      /^[a-z]+$/.test(joined) ? null : `「${joined}」が小文字の英字でない`,
      phrase.split(' ').every(w => /^[a-z]+$/.test(w)) ? null : `語句「${phrase}」が小文字の英字の語でない`,
      // 1 語だけだと Core は空白を入れず、繋げた形を定型句として英語扱いするだけになる
      phrase.includes(' ') ? null : `語句「${phrase}」が 1 語だけ (定型句は 2 語以上。空白が入らないので登録する意味が無い)`,
      joined === phrase.replace(/ /g, '') ? null : `「${joined}」が語句「${phrase}」を繋げた形でない`,
    ])),
    // Core (EnglishPhraseSpacing) は 1 文字目の # だけをコメントとして読み飛ばす。行末の # 以降は欄に残り、その行は登録されない
    commentAt: line => line.startsWith('#') ? 0 : -1,
    // Core が行を trim せずタブで分けるので、行頭の空白や行末の余分なタブがあると登録されない
    untrimmed: true,
    // Core は繋げた形をキーに、後の行で上書きする
    keysOf: line => { const [joined, phrase] = line.split('\t'); return [[joined, phrase]]; },
  },
  // ローマ字として最後まで読めても英語にする語。ファイル冒頭の決まりどおり 3 文字以上だけ。Core は小文字にして読む
  'english-readable.txt': {
    format: words(w => /^[A-Za-z]{3,}$/.test(w) ? null : `「${w}」が 3 文字以上の英字の語でない`),
    // 1 行に複数の語を置けるので、語ごとに見る。Core は小文字にして読むので、大文字小文字だけ違う語は同じ語
    keysOf: line => coreTokens(line).map(w => [w.toLowerCase(), w.toLowerCase()]),
  },
  'japanese.txt': {
    format: words(w => /^[a-z]+$/.test(w) ? null : `「${w}」が小文字のローマ字でない`),
  },
  // 読み 候補 : 手がかり …。Core (ConversionPreferences) は最初の : で分け、前後を空白・タブ・全角空白で区切る (: の前後の空白は問わない)
  'contexts.txt': {
    format: line => {
      const colon = line.indexOf(':');
      if (colon < 0) return ['「読み 候補 : 手がかり …」の形でない'];
      const [reading, word, extra] = line.slice(0, colon).split(/[ \t　]+/).filter(Boolean);
      const cues = line.slice(colon + 1).split(/[ \t　]+/).filter(Boolean);
      return [
        reading === undefined || hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
        reading === undefined ? '読みが無い' : word ? null : '候補が無い',
        extra ? '候補は 1 つだけ' : null,
        cues.length > 0 ? null : '手がかりが無い',
      ];
    },
  },
  // 読み<Tab>語句。同じ数の | で区切ると別の文節にする (いち|がっき<Tab>一|学期)
  'phrases.txt': {
    format: tabbed(2, 2, ([reading, word]) => [
      reading.replace(/^\^/, '').split('|').every(r => hiragana.test(r)) ? null : `読み「${reading}」がひらがなでない`,
      reading.split('|').length === word.split('|').length && !word.split('|').includes('') ? null : `読み「${reading}」と語句「${word}」の | の数が違う`,
    ]),
  },
  // 読み<Tab>語句<Tab>出し始める読み (予測変換の定型句)。出し始める読みは、読みの打ちかけ
  'greetings.txt': {
    format: tabbed(3, 3, ([reading, , start]) => [
      hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
      reading.startsWith(start) && start.length < reading.length ? null : `出し始める読み「${start}」が、読み「${reading}」の打ちかけでない`,
    ]),
  },
  'misspellings.txt': {
    format: tabbed(1, 2, fields => fields.map(f => kana.test(f) ? null : `「${f}」がかなでない`)),
  },
  'translations.txt': {
    format: tabbed(3, 3, ([, kind]) => [['n', 'na', 'i', 'v', 'adv'].includes(kind) ? null : `品詞「${kind}」が n / na / i / v / adv でない`]),
  },
  'meanings.txt': {
    format: tabbed(3, 3, ([, reading]) => [reading === '' || hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`]),
  },
  'readings.txt': {
    format: tabbed(1, 2, ([reading]) => [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`]),
  },
  'readings-extra.txt': {
    format: tabbed(1, 2, ([reading]) => [
      hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
      [...reading].length >= 3 ? null : `読み「${reading}」が 3 文字より短い`,
    ]),
  },
  // 通貨コード<Tab>1 ユーロあたりのレート (tools/make-exchange-rates.mjs で作る。日付は「# 日付:」の行)
  'exchange-rates.txt': {
    format: tabbed(2, 2, ([code, rate]) => [
      /^[A-Z]{3}$/.test(code) ? null : `通貨コード「${code}」が英大文字 3 文字でない`,
      /^[0-9]+(\.[0-9]+)?$/.test(rate) && Number(rate) > 0 ? null : `レート「${rate}」が正の数でない`,
    ]),
  },
  'katakana.txt': {
    format: tabbed(1, 1, ([reading]) => [hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`]),
  },
  // 顔文字に # が入るので、行頭の # だけがコメント。空白も中身なので前後の空白を除かない
  'emoji.txt': {
    format: tabbed(2, 99, () => []),
    commentAt: line => line.trimStart().startsWith('#') ? 0 : -1,
    untrimmed: true,
  },
  'emoji-cldr.txt': {
    format: tabbed(2, 99, () => []),
    commentAt: line => line.trimStart().startsWith('#') ? 0 : -1,
    untrimmed: true,
  },
};
// 国名の語 (tools/make-countries.mjs で作る)。Core は propernouns.txt と同じく読む
dictionaries['countries.txt'] = dictionaries['propernouns.txt'];

/**
 * 分野の辞書 (dictionaries/domain-*.txt。tools/make-domain-dictionaries.py で作る)。読み<Tab>表記<Tab>表記 …。
 * Core (CandidateDictionary.AddDomainText) は行頭の # だけをコメントとし、タブだけで区切る。読みは打ったかなと完全一致で引く
 */
const domain = {
  format: tabbed(2, Infinity, ([reading, ...words]) => [
    hiragana.test(reading) ? null : `読み「${reading}」がひらがなでない`,
    ...words.filter(w => w === '').map(() => '空の表記がある'),
  ]),
  commentAt: line => line.startsWith('#') ? 0 : -1,
  keysOf: line => [[line.split('\t')[0], line]],
};
const configOf = file => dictionaries[file] ?? (/^domain-[a-z-]+\.txt$/.test(file) ? domain : undefined);

function noInlineComment(check) {
  return line => line.includes('#') ? ['行末にコメントは書けない (コメントは行頭の # だけ。行末に書くとこの行は読み込まれない)'] : check(line);
}

function spaced(check) {
  return line => {
    const [reading, ...rest] = line.split(/[ \t　]+/).filter(Boolean);
    return check(reading, rest);
  };
}

/** Core と同じ区切りで分けた語ごとに確かめる。 */
function words(check) {
  return line => coreTokens(line).map(check);
}

function tabbed(min, max, check) {
  return line => {
    const fields = line.split('\t');
    if (fields.length < min || fields.length > max) return [`タブ区切りの欄が ${fields.length} 個 (${min === max ? min : `${min}〜${max}`} 個のはず)`];
    if (fields.some(f => f !== f.trim())) return ['欄の前後に空白がある'];
    return check(fields);
  };
}

/**
 * english.txt の日本語由来の節の検査を作る。
 * Core (CompositionDetector.JapaneseOriginWords) は「# ---」で始まる行を節の見出しとし、「日本語由来」を含む節では
 * 空行までの # で始まらない行を、# 以降も含めて半角空白だけで区切り、小文字にせずに日本語由来の語とする
 */
function japaneseOriginSection() {
  let inSection = false;
  return line => {
    if (line.startsWith('# ---')) { inSection = line.includes('日本語由来'); return []; }
    if (!inSection || line.startsWith('#')) return [];
    if (line.trim() === '') { inSection = false; return []; }
    return [
      line.includes('#') ? '日本語由来の節では行末にコメントを書けない (Core は # 以降も日本語由来の語として読む)' : null,
      // Core と同じく半角空白で分けて前後の空白を除いた語に、タブ・全角空白・, が残れば、その語は日本語由来の語にならない
      line.split(' ').some(w => /[\t\u3000,]/.test(w.trim())) ? '日本語由来の節では語をタブ・全角空白・, で区切れない (Core は半角空白でだけ区切る)' : null,
      /[A-Z]/.test(line) ? '日本語由来の節では大文字を使えない (Core は小文字にせずに読むので、小文字で引いても見つからない)' : null,
    ].filter(Boolean);
  };
}

/** 辞書 file の本文 text を確かめ、{ errors, warnings } (どちらも { line, message } の並び。line は無いこともある) を返す。 */
export function checkDictionary(file, text) {
  const errors = [], warnings = [];
  if (text.charCodeAt(0) === 0xfeff) warnings.push({ message: '先頭に BOM がある' });
  const config = configOf(file);
  if (!config) { warnings.push({ message: '形式を確かめる決まりが無い辞書 (tools/check-dictionaries.mjs に足してください)' }); return { errors, warnings }; }
  const {
    format,
    commentAt = line => line.indexOf('#'),
    untrimmed = false,
    keysOf = line => [[line.trim(), line.trim()]],
    allowRepeats = false,
    sections,
  } = config;
  const sectionCheck = sections?.();
  const seen = new Map();
  text.split('\n').forEach((raw, i) => {
    const lineNo = i + 1;
    let line = raw.replace(/\r$/, '');
    for (const problem of sectionCheck?.(line) ?? []) errors.push({ line: lineNo, message: problem });
    const hash = commentAt(line);
    if (hash >= 0) line = line.slice(0, hash);
    if (line.trim() === '') return;
    if (/[ \t]$/.test(raw.replace(/\r$/, '')) && hash < 0) warnings.push({ line: lineNo, message: '行の終わりに空白がある' });
    const problems = format(untrimmed ? line : line.trim()).filter(Boolean);
    for (const problem of problems) errors.push({ line: lineNo, message: problem });
    if (problems.length > 0) return; // 読めない行は重複を見ない
    for (const [key, value] of keysOf(line.trim())) {
      const first = seen.get(key);
      if (!first) seen.set(key, { lineNo, value, text: line.trim() });
      else if (first.value !== value) errors.push({ line: lineNo, message: `「${key}」が ${first.lineNo} 行目と食い違う (「${first.value}」と「${value}」)` });
      else if (!allowRepeats) warnings.push({ line: lineNo, message: first.text === line.trim() ? `${first.lineNo} 行目と同じ行` : `「${key}」が ${first.lineNo} 行目と重複` });
    }
  });
  return { errors, warnings };
}

/**
 * node で直接起動されたか。シンボリックリンク経由でも、実体のパスで比べる。
 * 実体のパスを調べられないときは、検査しないまま終了コード 0 で終わらないよう、エラーで終わる。
 */
function isMain() {
  if (!process.argv[1]) return false;
  try {
    return fs.realpathSync(process.argv[1]) === fs.realpathSync(self);
  } catch (e) {
    console.error(`起動したファイル ${process.argv[1]} の実体のパスを調べられないので、辞書を検査できない: ${e.message}`);
    process.exit(1);
  }
}

if (isMain()) {
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
