/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング調査用のローマ字→ひらがな。打った人に今の入力を見せることと、結果の採点に使う。
// 集めたデータには打ったローマ字をそのまま残すので、ここで読み替えを変えても過去のデータは作り直せる。
// 決まり: 打ち方は変えさせない。ヘボン式・訓令式・x/l の小さい字のどれで打っても同じかなにする。

const VOWELS = ["a", "i", "u", "e", "o"];

const BASE = {
  a: "あ", i: "い", u: "う", e: "え", o: "お",
  ka: "か", ki: "き", ku: "く", ke: "け", ko: "こ",
  sa: "さ", si: "し", su: "す", se: "せ", so: "そ", shi: "し",
  ta: "た", ti: "ち", tu: "つ", te: "て", to: "と", chi: "ち", tsu: "つ",
  na: "な", ni: "に", nu: "ぬ", ne: "ね", no: "の",
  ha: "は", hi: "ひ", hu: "ふ", he: "へ", ho: "ほ", fu: "ふ",
  ma: "ま", mi: "み", mu: "む", me: "め", mo: "も",
  ya: "や", yu: "ゆ", yo: "よ", yi: "い", ye: "いぇ",
  ra: "ら", ri: "り", ru: "る", re: "れ", ro: "ろ",
  la: "ぁ", li: "ぃ", lu: "ぅ", le: "ぇ", lo: "ぉ",
  xa: "ぁ", xi: "ぃ", xu: "ぅ", xe: "ぇ", xo: "ぉ",
  wa: "わ", wi: "うぃ", wu: "う", we: "うぇ", wo: "を",
  ga: "が", gi: "ぎ", gu: "ぐ", ge: "げ", go: "ご",
  za: "ざ", zi: "じ", zu: "ず", ze: "ぜ", zo: "ぞ", ji: "じ",
  da: "だ", di: "ぢ", du: "づ", de: "で", do: "ど",
  ba: "ば", bi: "び", bu: "ぶ", be: "べ", bo: "ぼ",
  pa: "ぱ", pi: "ぴ", pu: "ぷ", pe: "ぺ", po: "ぽ",
  va: "ゔぁ", vi: "ゔぃ", vu: "ゔ", ve: "ゔぇ", vo: "ゔぉ",
  fa: "ふぁ", fi: "ふぃ", fe: "ふぇ", fo: "ふぉ",
  ja: "じゃ", ju: "じゅ", je: "じぇ", jo: "じょ",
  sha: "しゃ", shu: "しゅ", she: "しぇ", sho: "しょ",
  cha: "ちゃ", chu: "ちゅ", che: "ちぇ", cho: "ちょ",
  tsa: "つぁ", tsi: "つぃ", tse: "つぇ", tso: "つぉ",
  tha: "てゃ", thi: "てぃ", thu: "てゅ", the: "てぇ", tho: "てょ",
  dha: "でゃ", dhi: "でぃ", dhu: "でゅ", dhe: "でぇ", dho: "でょ",
  twu: "とぅ", dwu: "どぅ",
  lya: "ゃ", lyu: "ゅ", lyo: "ょ", xya: "ゃ", xyu: "ゅ", xyo: "ょ",
  ltu: "っ", xtu: "っ", ltsu: "っ", xtsu: "っ", lwa: "ゎ", xwa: "ゎ",
  lka: "ゕ", xka: "ゕ", lke: "ゖ", xke: "ゖ",
  nn: "ん", "n'": "ん", xn: "ん",
  "-": "ー", ",": "、", ".": "。", "!": "！", "?": "？", "~": "〜", "[": "「", "]": "」", "(": "（", ")": "）",
};

// きゃ・しゃ (sya) など「子音 + y + 母音」と、きぇ (kye) などをまとめて作る
const Y_ROWS = { k: "き", s: "し", t: "ち", n: "に", h: "ひ", m: "み", r: "り", g: "ぎ", z: "じ", j: "じ", d: "ぢ", b: "び", p: "ぴ", c: "ち", f: "ふ", v: "ゔ" };
const SMALL = { a: "ゃ", i: "ぃ", u: "ゅ", e: "ぇ", o: "ょ" };
for (const [consonant, kana] of Object.entries(Y_ROWS)) {
  for (const vowel of VOWELS) BASE[`${consonant}y${vowel}`] ??= kana + SMALL[vowel];
}

const TABLE = new Map(Object.entries(BASE));
const LONGEST = Math.max(...[...TABLE.keys()].map((key) => key.length));
// ある文字列が、まだどれかの綴りの途中かどうか
const PREFIXES = new Set([...TABLE.keys()].flatMap((key) => [...key].map((_, i) => key.slice(0, i + 1))));

const isConsonant = (c) => /[a-z]/.test(c) && !VOWELS.includes(c);

// 打ったローマ字をかなにする。kana は確定した部分、pending は綴りの途中で残っている部分。
// 打ち間違えた文字 (どの綴りにもならない文字) は、かなにせずそのまま kana に入れる。
export function toKana(romaji) {
  const text = String(romaji).toLowerCase();
  let kana = "";
  let i = 0;
  while (i < text.length) {
    const c = text[i];
    const next = text[i + 1];
    // 子音を重ねたら「っ」(nn は「ん」なので除く)
    if (next !== undefined && c === next && isConsonant(c) && c !== "n") {
      kana += "っ";
      i += 1;
      continue;
    }
    // n の後に母音・y・n 以外が来たら「ん」
    if (c === "n" && next !== undefined && !VOWELS.includes(next) && next !== "y" && next !== "n" && next !== "'") {
      kana += "ん";
      i += 1;
      continue;
    }
    let matched = false;
    for (let length = Math.min(LONGEST, text.length - i); length > 0; length -= 1) {
      const piece = text.slice(i, i + length);
      const value = TABLE.get(piece);
      if (value !== undefined) {
        kana += value;
        i += length;
        matched = true;
        break;
      }
    }
    if (matched) continue;
    const rest = text.slice(i);
    if (PREFIXES.has(rest)) return { kana, pending: rest };
    kana += c;
    i += 1;
  }
  return { kana, pending: "" };
}

// 打ち終わった入力を読みとして見る。最後に残った n は「ん」にする (変換を確定するときと同じ)。
export function finalKana(romaji) {
  const { kana, pending } = toKana(romaji);
  return kana + (pending === "n" ? "ん" : pending);
}

// お手本 (target) と打ったもの (typed) を 1 文字ずつ並べる。
// 返す ops は { op: "same" | "sub" | "omit" | "extra", want, got } の並び。
export function align(target, typed) {
  const a = [...target];
  const b = [...typed];
  const rows = a.length + 1;
  const cols = b.length + 1;
  const cost = new Uint32Array(rows * cols);
  for (let i = 0; i < rows; i += 1) cost[i * cols] = i;
  for (let j = 0; j < cols; j += 1) cost[j] = j;
  for (let i = 1; i < rows; i += 1) {
    for (let j = 1; j < cols; j += 1) {
      const diagonal = cost[(i - 1) * cols + j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1);
      cost[i * cols + j] = Math.min(diagonal, cost[(i - 1) * cols + j] + 1, cost[i * cols + j - 1] + 1);
    }
  }
  const ops = [];
  let i = a.length;
  let j = b.length;
  while (i > 0 || j > 0) {
    const here = cost[i * cols + j];
    if (i > 0 && j > 0 && here === cost[(i - 1) * cols + j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1)) {
      ops.push({ op: a[i - 1] === b[j - 1] ? "same" : "sub", want: a[i - 1], got: b[j - 1] });
      i -= 1;
      j -= 1;
    } else if (i > 0 && here === cost[(i - 1) * cols + j] + 1) {
      ops.push({ op: "omit", want: a[i - 1], got: "" });
      i -= 1;
    } else {
      ops.push({ op: "extra", want: "", got: b[j - 1] });
      j -= 1;
    }
  }
  return ops.reverse();
}

// かなを、ヘボン式のローマ字の 1 つの打ち方にする。打ったキーがお手本からどれだけ離れているかを見るのに使う (typing-check.js)
const HEPBURN = { し: "shi", ち: "chi", つ: "tsu", ふ: "fu", じ: "ji", ぢ: "di", づ: "du", を: "wo", ん: "nn", ー: "-", "、": ",", "。": ".", "！": "!", "？": "?", "〜": "~", "…": "...", "「": "[", "」": "]", "（": "(", "）": ")" };
const TO_ROMAJI = new Map();
for (const [key, value] of TABLE) {
  // 綴りの短いもの (x・l で始まらないもの) を優先する
  const current = TO_ROMAJI.get(value);
  const better = !current || (key[0] !== "x" && key[0] !== "l" && (current[0] === "x" || current[0] === "l" || key.length < current.length));
  if (better && !/['n]$/.test(key)) TO_ROMAJI.set(value, key);
}
for (const [kana, key] of Object.entries(HEPBURN)) TO_ROMAJI.set(kana, key);
for (const [kana, small] of [["しゃ", "sha"], ["しゅ", "shu"], ["しょ", "sho"], ["ちゃ", "cha"], ["ちゅ", "chu"], ["ちょ", "cho"], ["じゃ", "ja"], ["じゅ", "ju"], ["じょ", "jo"]]) TO_ROMAJI.set(kana, small);

export function toRomaji(kana) {
  const chars = [...kana];
  let out = "";
  for (let i = 0; i < chars.length; i += 1) {
    if (chars[i] === "っ") {
      // 次の音の子音を重ねる
      const next = TO_ROMAJI.get(chars[i + 1] + (chars[i + 2] || "")) || TO_ROMAJI.get(chars[i + 1]) || "";
      out += /^[a-z]/.test(next) && !/^[aiueon]/.test(next) ? next[0] : "xtu";
      continue;
    }
    const pair = TO_ROMAJI.get(chars[i] + (chars[i + 1] || ""));
    if (pair && chars[i + 1]) {
      out += pair;
      i += 1;
      continue;
    }
    out += TO_ROMAJI.get(chars[i]) ?? chars[i];
  }
  return out;
}
