// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// KRADFILE (漢字 → 構成要素) と KANJIDIC2 (使われる頻度・画数) から、漢字の構成要素の表 dictionaries/kanji-parts.txt を作る (#345)。
// 「ごんべんにふるとり」のような部首の説明から漢字を探すのに使う。
//   node tools/make-kanji-parts.mjs kradfile kanjidic2.xml.gz > dictionaries/kanji-parts.txt
// 入手先: http://ftp.edrdg.org/pub/Nihongo/kradzip.zip の kradfile (EUC-JP)、https://www.edrdg.org/kanjidic/kanjidic2.xml.gz
// 形式は「漢字<Tab>要素 要素 …」。よく使う字を先に並べる (候補はこの順に出す)。
import fs from 'node:fs';
import zlib from 'node:zlib';

const krad = new TextDecoder('euc-jp').decode(fs.readFileSync(process.argv[2]));
const kanjidic = zlib.gunzipSync(fs.readFileSync(process.argv[3])).toString('utf8');

// 頻度 (新聞での順位。小さいほどよく使う) と画数
const info = new Map();
for (const [, body] of kanjidic.matchAll(/<character>([\s\S]*?)<\/character>/g)) {
  const literal = body.match(/<literal>(.*?)<\/literal>/)[1];
  const freq = Number(body.match(/<freq>(\d+)<\/freq>/)?.[1] ?? Infinity);
  const strokes = Number(body.match(/<stroke_count>(\d+)<\/stroke_count>/)[1]);
  info.set(literal, { freq, strokes });
}

const rows = [];
for (const line of krad.split('\n')) {
  if (line.startsWith('#') || !line.includes(' : ')) continue;
  const [kanji, parts] = line.split(' : ');
  rows.push({ kanji, parts: parts.trim().split(/\s+/), ...(info.get(kanji) ?? { freq: Infinity, strokes: 99 }) });
}
// 頻度の順。頻度の無い字 (あまり使わない字) は画数の少ない順
rows.sort((a, b) => a.freq - b.freq || a.strokes - b.strokes || (a.kanji < b.kanji ? -1 : 1));

console.log([
  '# Meltype 漢字の構成要素 (漢字<Tab>要素 要素 …)。よく使う字を先に並べる。',
  '# KRADFILE と KANJIDIC2 (Electronic Dictionary Research and Development Group) から tools/make-kanji-parts.mjs で作成。',
  '# EDRDG のライセンス: Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) https://www.edrdg.org/edrdg/licence.html',
  '# この派生データも CC BY-SA 4.0。要素は JIS X 0208 の字で、単独で無い要素はそれを含む字で表す (にんべん → 化、さんずい → 汁)。',
  '# 作り直し: node tools/make-kanji-parts.mjs kradfile kanjidic2.xml.gz > dictionaries/kanji-parts.txt',
  ...rows.map(r => `${r.kanji}\t${r.parts.join(' ')}`),
].join('\n'));
