// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// JMdict (EDRDG、CC BY-SA 4.0) から、英単語とそのカタカナ表記の表 dictionaries/katakana-spellings.txt を作る。
// 英語と判定した語 (export) を変換したとき、候補にカタカナ表記 (エクスポート) を出すのに使う。
//   node tools/make-katakana-spellings.mjs JMdict_e.xml dictionaries/english-words.txt cmudict.dict > dictionaries/katakana-spellings.txt
// cmudict.dict は https://github.com/cmusphinx/cmudict の発音辞書 (BSD-2-Clause)。生成時の照合にだけ使う。
// 形式は「英単語 カタカナ カタカナ …」。
// 入れるのは、漢字の書き方の無いカタカナ語で、英訳が英単語 1 語のもの (エクスポート = export)。括弧の説明は外す。
// 英訳は綴りとは限らない (ビスケット = cookie)。CMUdict の発音とカタカナの子音の並びが一致する組だけを残す。
// 母音・長音・促音の違いは許す。厳密な音訳ではないので同音の別語までは区別できず、未収録語・読みの違いの大きい語は入らない。
// 英語以外から来た語 (アルバイト)・和製英語 (ナイター)・擬音語や俗語 (ブツブツ = grunt) は、英語の綴りの読みでないので入れない。
// 英単語は、よく使う英単語の一覧 (english-words.txt) にある語だけ。まれな英訳まで入れると、ふつうの英語の文で出る候補が増える。
import fs from 'node:fs';

const xml = fs.readFileSync(process.argv[2], 'utf8');
const common = new Set(fs.readFileSync(process.argv[3], 'utf8').split(/\r?\n/).filter(l => /^[a-z]+$/.test(l)));

// L/R・B/V、シ/ス・ジ/ズ・チ/ティはカタカナで揺れる。子音の前の鼻音 (bank・scramble) と、
// 子音の後の W/Y (キュートなど)、促音・子音の重なりも区別しない。
const normalize = s => s.replace(/S/g, 's').replace(/c/g, 't').replace(/d[zj]/g, 'j').replace(/z/g, 'j')
  .replace(/ng(?=[kg])/g, 'n').replace(/m(?=[bp])/g, 'n')
  .replace(/hw/g, 'w').replace(/(?<=[a-zA-Z])[wy]/g, '').replace(/(.)\1+/g, '$1');
const consonants = { B: 'b', CH: 'c', D: 'd', DH: 'z', F: 'f', G: 'g', HH: 'h', JH: 'j', K: 'k', L: 'r', M: 'm', N: 'n', NG: 'ng', P: 'p', R: 'r', S: 's', SH: 'S', T: 't', TH: 's', V: 'b', W: 'w', Y: 'y', Z: 'z', ZH: 'j' };
const vowels = new Set('AA AE AH AO AW AY EH ER EY IH IY OW OY UH UW'.split(' '));
const pronunciations = new Map();
for (const line of fs.readFileSync(process.argv[4], 'utf8').split(/\r?\n/)) {
  const [spelling, ...rawPhones] = line.split('#')[0].trim().split(/\s+/);
  const word = spelling.replace(/\(\d+\)$/, '').toLowerCase();
  if (!common.has(word)) continue;
  const phones = rawPhones.map(p => p.replace(/[012]/g, ''));
  const key = normalize(phones.map((p, i) => {
    // 母音の後で語末・子音の前にある R は、カタカナでは長音になる (export → エクスポート)。
    if (p === 'R' && vowels.has(phones[i - 1]) && !vowels.has(phones[i + 1])) return '';
    // ER は R の付いた母音。次が母音なら R が残る (camera → カメラ、marine → マリン)。
    if (p === 'ER' && vowels.has(phones[i + 1])) return 'r';
    return consonants[p] ?? '';
  }).join(''));
  const keys = pronunciations.get(word) ?? new Set();
  keys.add(key);
  pronunciations.set(word, keys);
}

const kanaConsonants = new Map();
for (const [chars, sound] of [
  ['カキクケコ', 'k'], ['ガギグゲゴ', 'g'], ['サスセソ', 's'], ['シ', 'S'], ['ザズゼゾヅ', 'z'], ['ジヂ', 'j'],
  ['タテト', 't'], ['チ', 'c'], ['ツ', 'ts'], ['ダデド', 'd'], ['ナニヌネノン', 'n'], ['ハヒヘホ', 'h'], ['フ', 'f'],
  ['バビブベボヴ', 'b'], ['パピプペポ', 'p'], ['マミムメモ', 'm'], ['ヤユヨ', 'y'], ['ラリルレロ', 'r'], ['ワヰヱヲ', 'w'],
]) for (const c of chars) kanaConsonants.set(c, sound);
const kanaKey = s => normalize([...s.replace(/ウ[ィェォ]/g, 'ワ')].map(c => kanaConsonants.get(c) ?? '').join(''));
const withoutExplanation = gloss => {
  // 入れ子の括弧も内側から外す。説明を外しても複数語なら、英単語の一覧とは一致しない。
  while (/\([^()]*\)/.test(gloss)) gloss = gloss.replace(/\([^()]*\)/g, ' ');
  return gloss.trim();
};

const lines = new Map();
for (const [, entry] of xml.matchAll(/<entry>([\s\S]*?)<\/entry>/g)) {
  // 漢字やひらがなでも書く語 (ウン・うむ = yes) は、もとから日本語の語
  if (entry.includes('<k_ele>') || /<reb>[^<]*[ぁ-ゖ]/.test(entry)) continue;
  // カタカナだけの読み。まれな読み (&sk; など) は除く。
  const spellings = [...entry.matchAll(/<r_ele>([\s\S]*?)<\/r_ele>/g)]
    .filter(m => !/<re_inf>/.test(m[1]))
    .map(m => m[1].match(/<reb>(.*?)<\/reb>/)[1])
    .filter(r => /^[ァ-ヺー]+$/.test(r));
  // よく使う語の印 (gai1 など) の付いた語を前に出す
  const rank = /<re_pri>/.test(entry) ? 0 : 1;
  for (const [, sense] of entry.matchAll(/<sense>([\s\S]*?)<\/sense>/g)) {
    if (/<lsource\b[^>]*(?:xml:lang="(?!eng")[^"]*"|ls_wasei="y")/.test(sense)) continue;
    // 擬音語・俗語・略語・方言の意味は除く。ノートの「notebook」は略語だが、次の意味「note」は使える。
    if (/<misc>|<dial>/.test(sense)) continue;
    // 最初の訳が別の語でも、後の訳に元の綴りがある (ビスケット = cookie, biscuit / メモ = note, memo)。
    const words = new Set([...sense.matchAll(/<gloss>(.*?)<\/gloss>/g)].map(m => withoutExplanation(m[1])).filter(w => common.has(w)));
    const readings = [...sense.matchAll(/<stagr>(.*?)<\/stagr>/g)].map(m => m[1]);
    for (const word of words) {
      const list = lines.get(word) ?? [];
      for (const s of spellings) {
        if (readings.length > 0 && !readings.includes(s)) continue;
        if (pronunciations.get(word)?.has(kanaKey(s)) && !list.some(e => e.s === s)) list.push({ s, rank });
      }
      lines.set(word, list);
    }
  }
}

const out = [
  '# Meltype 英単語のカタカナ表記 (英単語 カタカナ カタカナ …)。export → エクスポート',
  '# JMdict (Electronic Dictionary Research and Development Group) のカタカナ語の英訳から tools/make-katakana-spellings.mjs で作成。',
  '# JMdict は Creative Commons Attribution-ShareAlike 4.0 (CC BY-SA 4.0) で公開されている: https://www.edrdg.org/edrdg/licence.html',
  '# CMUdict (Carnegie Mellon University、BSD-2-Clause) の発音で子音の並びを照合。出典・ライセンス: THIRD-PARTY-NOTICES.md',
  ...[...lines].filter(([, list]) => list.length > 0).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
    .map(([w, list]) => `${w} ${list.sort((a, b) => a.rank - b.rank).map(e => e.s).join(' ')}`),
];
process.stdout.write(out.join('\n') + '\n');
