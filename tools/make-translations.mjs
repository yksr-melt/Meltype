// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Meltype Contributors
//
// JMdict (EDRDG、CC BY-SA 4.0) から、よく使う語の英訳の表 dictionaries/translations.txt を作る。
//   node tools/make-translations.mjs JMdict_e.xml > dictionaries/translations.txt
// JMdict_e.gz は https://www.edrdg.org/jmdict/j_jmdict.html (http://ftp.edrdg.org/pub/Nihongo/JMdict_e.gz) から。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
// よく使う語の印 (ichi1: 日常語、news1: 新聞の頻出語、spec1/2: よく使う、gai1: よく使う外来語)
const common = /<(?:ke|re)_pri>(?:ichi1|news1|spec1|spec2|gai1)<\/(?:ke|re)_pri>/;
const toHiragana = s => s.replace(/[ァ-ヶ]/g, c => String.fromCharCode(c.charCodeAt(0) - 0x60));

// 品詞をまとめる: な形容詞 / い形容詞 / 動詞 / 副詞 / 名詞など
function kind(pos) {
  if (pos.includes('&adj-na;')) return 'na';
  if (pos.some(p => p === '&adj-i;' || p === '&adj-ix;')) return 'i';
  if (pos.includes('&adv;')) return 'adv';
  // 動詞は活用の種類 (v1 = 一段、v5k = 五段 …) で見る。する の付く名詞 (講演 = vs、vt / vi は自他の印) は名詞として扱う。
  if (pos.some(p => /^&v(?:1|2|4|5|k|n|r|z|-unspec)/.test(p))) return 'v';
  return 'n';
}

// 候補として使える短い英語にする: 括弧の説明を外し、動詞の to を外す。長いもの・説明文は使わない。
function clean(gloss, k) {
  let g = gloss.replace(/\([^)]*\)/g, '').replace(/\s+/g, ' ').trim();
  if (k === 'v') g = g.replace(/^to /, '');
  if (!g || g.length > 22 || /[;:…"]|\.\.\.|etc|e\.g|[^\x20-\x7e]/.test(g) || g.split(' ').length > 3) return null;
  return g;
}

const lines = new Map();
for (const [, entry] of xml.matchAll(/<entry>([\s\S]*?)<\/entry>/g)) {
  if (!common.test(entry)) continue;
  // 書き方: よく使う印の付いた漢字表記。無ければ、よく使う印の付いた読み (かなだけの語)。
  const kanji = [...entry.matchAll(/<k_ele>([\s\S]*?)<\/k_ele>/g)].filter(m => common.test(m[1])).map(m => m[1].match(/<keb>(.*?)<\/keb>/)[1]);
  const kana = [...entry.matchAll(/<r_ele>([\s\S]*?)<\/r_ele>/g)].filter(m => common.test(m[1])).map(m => toHiragana(m[1].match(/<reb>(.*?)<\/reb>/)[1]));
  const surfaces = kanji.length > 0 ? kanji : kana;
  if (surfaces.length === 0) continue;

  // 意味ごとの品詞と英訳 (品詞が書かれていない意味は、前の意味の品詞を引き継ぐ)。最初の 3 つの意味だけ。
  let pos = [];
  const byKind = new Map();
  for (const [, sense] of [...entry.matchAll(/<sense>([\s\S]*?)<\/sense>/g)].slice(0, 3)) {
    const own = [...sense.matchAll(/<pos>(.*?)<\/pos>/g)].map(m => m[1]);
    if (own.length > 0) pos = own;
    if (/<misc>&(?:arch|obs|rare|vulg|X|derog);<\/misc>/.test(sense)) continue;
    const k = kind(pos);
    const glosses = [...sense.matchAll(/<gloss>(.*?)<\/gloss>/g)].map(m => clean(m[1], k)).filter(Boolean);
    const list = byKind.get(k) ?? [];
    for (const g of glosses) if (!list.includes(g) && list.length < 4) list.push(g);
    byKind.set(k, list);
  }
  for (const surface of surfaces) {
    for (const [k, list] of byKind) {
      if (list.length === 0) continue;
      const key = `${surface}\t${k}`;
      const existing = lines.get(key) ?? [];
      for (const g of list) if (!existing.includes(g) && existing.length < 4) existing.push(g);
      lines.set(key, existing);
    }
  }
}

const out = [
  '# Meltype 英訳の候補 (漢字かなの書き方 [TAB] 品詞 [TAB] 英訳,英訳…)',
  '# 品詞: na = な形容詞、i = い形容詞、v = 動詞、adv = 副詞、n = 名詞など',
  '# JMdict (Electronic Dictionary Research and Development Group) のよく使う語から tools/make-translations.mjs で作成。',
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  ...[...lines].map(([key, list]) => `${key}\t${list.join(',')}`),
];
process.stdout.write(out.join('\n') + '\n');
