// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// ウィクショナリー日本語版 (CC BY-SA 4.0) から、よく使う語の日本語の意味の表 dictionaries/meanings.txt を作る。
// 変換中に候補で止まったとき、その候補の意味を出すのに使う (箸 → 食物を挟む二本一組の棒 …)。
//   node tools/make-meanings.mjs kaikki.org-dictionary-日本語.jsonl dictionaries/translations.txt > dictionaries/meanings.txt
// jsonl は kaikki.org (wiktextract) がウィクショナリー日本語版から取り出したもの:
//   https://kaikki.org/jawiktionary/日本語/index.html
// 対象の語は translations.txt (JMdict のよく使う語) にある書き方だけ。
// 形式: 書き方 [TAB] 読み (その意味がその読みのときだけ。無ければ空) [TAB] 意味
import fs from 'node:fs';
import readline from 'node:readline';

const [jsonl, translations] = process.argv.slice(2);
const toHiragana = s => s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60));

const wanted = new Set();
for (const line of fs.readFileSync(translations, 'utf8').split('\n')) {
  if (!line || line.startsWith('#')) continue;
  wanted.add(line.split('\t')[0]);
}

const MaxSenses = 3, MaxLength = 60;

// 意味の文を短くする: 最初の文 (。まで) だけ、長ければ切る。(サ変) などの文法の印は外す。
function clean(gloss) {
  let g = gloss.replace(/\s+/g, ' ').trim();
  let reading = '';
  const marked = g.match(/^[（(]([ぁ-んァ-ヶー・]+)[）)]\s*/);
  if (marked) {
    reading = toHiragana(marked[1].replace(/・/g, ''));
    g = g.slice(marked[0].length);
  }
  g = g.replace(/^([（(][^）)]{1,12}[）)]\s*)+/, '');
  const end = g.indexOf('。');
  if (end >= 0) g = g.slice(0, end + 1);
  if ([...g].length > MaxLength) g = [...g].slice(0, MaxLength - 1).join('') + '…';
  return { reading, gloss: g.replace(/\t/g, ' ') };
}

const senses = new Map(); // 書き方 → [{ reading, gloss }]
const redirects = new Map(); // 漢字の書き方 → かなの書き方 (うつくしい の漢字表記。)
const input = readline.createInterface({ input: fs.createReadStream(jsonl) });
for await (const line of input) {
  const entry = JSON.parse(line);
  if (entry.lang_code !== 'ja') continue;
  const word = entry.word;
  for (const sense of entry.senses ?? []) {
    for (const raw of sense.glosses ?? []) {
      if (!raw) continue;
      const redirect = raw.match(/^([ぁ-んァ-ヶー]+)の(?:漢字)?表記。?$/);
      if (redirect) {
        if (wanted.has(word)) redirects.set(word, toHiragana(redirect[1]));
        continue;
      }
      if (/の(?:旧字体|異体字|古い表記|誤記|略)。?$/.test(raw)) continue;
      const { reading, gloss } = clean(raw);
      if (gloss.length < 2) continue;
      const list = senses.get(word) ?? [];
      if (list.length < MaxSenses * 2 && !list.some(s => s.gloss === gloss)) list.push({ reading, gloss });
      senses.set(word, list);
    }
  }
}

const out = [
  '# Meltype 候補の意味 (書き方 [TAB] 読み [TAB] 意味)。読みはその意味がその読みのときだけ (橋: はし / きょう)。',
  '# ウィクショナリー日本語版 (Wiktionary の執筆者) の記事から、kaikki.org (wiktextract) が取り出したデータを元に tools/make-meanings.mjs で作成。',
  '# ウィクショナリーは Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://ja.wiktionary.org/',
];
const targets = new Map();
for (const kana of redirects.values()) targets.set(kana, (targets.get(kana) ?? 0) + 1);
let count = 0;
for (const word of [...wanted].sort()) {
  let list = senses.get(word);
  // かなの記事に送るだけの書き方 (会う → あう の漢字表記)。同じかなに送る書き方が複数 (会う・合う・遭う) あると、
  // かなの記事の意味がどの漢字のものか分からないので出さない (英訳の候補の意味が出る)。
  if (!list && redirects.has(word) && targets.get(redirects.get(word)) === 1) list = senses.get(redirects.get(word));
  if (!list) continue;
  for (const { reading, gloss } of list.slice(0, MaxSenses * 2)) out.push(`${word}\t${reading}\t${gloss}`);
  count++;
}
process.stdout.write(out.join('\n') + '\n');
console.error(`${count} 語`);
