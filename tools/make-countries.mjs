// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Unicode CLDR の英語の国・地域名 (common/main/en.xml の territories。別名を含む) から、
// 英語として扱う国名の語の一覧 dictionaries/countries.txt を作る (#1615・#1650)。
//   node tools/make-countries.mjs en.xml dictionaries/english-words.txt > dictionaries/countries.txt
// en.xml は https://github.com/unicode-org/cldr の common/main/en.xml。
// 英語と判定されるかは日英判別の重みで変わるので、いま英語になる国名も含めてすべての語を書く (#1615)。
// ただし今は、ローマ字として最後まで読める語 (Oman・San) を Core が読み込むときに外している (ProperNouns.Load のワークアラウンド。#1869)。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
// よく使う英単語 (Republic・Island・South・City) は英語として既に知っている。固有名詞にすると候補に大文字の形が出るので入れない
const common = new Set(fs.readFileSync(process.argv[3], 'utf8').split(/\r?\n/).filter(w => /^[a-z]+$/.test(w)));

const names = new Set();
// 国・地域 (英字 2 文字のコード) の名前。大陸などの地域 (数字 3 文字のコード) は除く
for (const [, code, name] of xml.matchAll(/<territory type="([A-Z]{2})"[^>]*>([^<]+)<\/territory>/g)) {
  if (code === 'ZZ') continue; // 不明な地域
  names.add(name.replace(/&amp;/g, '&'));
}

const words = new Set();
for (const name of [...names].sort()) {
  for (const word of name.split(/[\s,.&()-]+/)) {
    // 大文字で始まる英字だけの語。小文字の語 (da・and・of) は日本語のローマ字 (だ) を英語にしてしまうので入れない。
    // アクセント付き (Côte・Réunion) は打った英字と一致しないので入れない
    if (!/^[A-Z][A-Za-z]+$/.test(word) || common.has(word.toLowerCase())) continue;
    words.add(word);
  }
}

console.log([
  '# Meltype 英語として扱う国名の語 (propernouns.txt と同じ形式)。ローマ字として最後まで読める語 (Oman・San) は Core が読み込むときに外す (ProperNouns.Load)',
  '# Unicode CLDR common/main/en.xml の国・地域名 (別名を含む) を語に分け、よく使う英単語 (english-words.txt) にある語を除いた。',
  '# Copyright © 1991-2026 Unicode, Inc. Unicode License v3 (SPDX: Unicode-3.0)。全文は THIRD-PARTY-NOTICES.md。',
  '# 作り直し: node tools/make-countries.mjs en.xml dictionaries/english-words.txt > dictionaries/countries.txt',
  ...[...words].sort(),
].join('\n'));
