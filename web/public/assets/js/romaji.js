/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング調査用のローマ字→ひらがな。打った人に今の入力を見せることと、結果の採点に使う。
// 集めたデータには打ったローマ字をそのまま残すので、ここで読み替えを変えても過去のデータは作り直せる。
// 決まり: 打ち方は変えさせない。ヘボン式・訓令式・x/l の小さい字のどれで打っても同じかなにする。

const VOWELS = ["a", "i", "u", "e", "o"];

// 綴りは Meltype 本体 (src/Meltype.Core/Detection/RomajiDetector.cs の Table と CompositionTable) と同じにする。
// 本体で打てる綴りがここで打てないと、Meltype の利用者がふだんどおり打っても間違いになるため。
const BASE = {
  a: "あ", i: "い", yi: "い", u: "う", whu: "う", wu: "う", e: "え", o: "お",
  ka: "か", ki: "き", ku: "く", ke: "け", ko: "こ", kya: "きゃ", kyu: "きゅ", kyo: "きょ",
  sa: "さ", shi: "し", si: "し", su: "す", se: "せ", ce: "せ", so: "そ", sha: "しゃ",
  sya: "しゃ", shu: "しゅ", syu: "しゅ", sho: "しょ", syo: "しょ", she: "しぇ", sye: "しぇ", ta: "た",
  chi: "ち", ti: "ち", tsu: "つ", tu: "つ", te: "て", to: "と", cha: "ちゃ", tya: "ちゃ",
  cya: "ちゃ", chu: "ちゅ", tyu: "ちゅ", cyu: "ちゅ", cho: "ちょ", tyo: "ちょ", cyo: "ちょ", che: "ちぇ",
  tye: "ちぇ", cye: "ちぇ", tha: "てゃ", thu: "てゅ", tho: "てょ", na: "な", ni: "に", nu: "ぬ",
  ne: "ね", no: "の", nya: "にゃ", nyu: "にゅ", nyo: "にょ", nyi: "にぃ", nye: "にぇ", ha: "は",
  hi: "ひ", fu: "ふ", hu: "ふ", he: "へ", ho: "ほ", hya: "ひゃ", hyu: "ひゅ", hyo: "ひょ",
  fa: "ふぁ", hwa: "ふぁ", fi: "ふぃ", fyi: "ふぃ", hwi: "ふぃ", fe: "ふぇ", fye: "ふぇ", hwe: "ふぇ",
  fo: "ふぉ", hwo: "ふぉ", fya: "ふゃ", fyu: "ふゅ", fyo: "ふょ", ma: "ま", mi: "み", mu: "む",
  me: "め", mo: "も", mya: "みゃ", myu: "みゅ", myo: "みょ", ya: "や", yu: "ゆ", yo: "よ",
  ra: "ら", ri: "り", ru: "る", re: "れ", ro: "ろ", rya: "りゃ", ryu: "りゅ", ryo: "りょ",
  wa: "わ", wo: "を", who: "うぉ", wha: "うぁ", ga: "が", gi: "ぎ", gu: "ぐ", ge: "げ",
  go: "ご", gya: "ぎゃ", gyu: "ぎゅ", gyo: "ぎょ", za: "ざ", ji: "じ", zi: "じ", zu: "ず",
  ze: "ぜ", zo: "ぞ", ja: "じゃ", jya: "じゃ", zya: "じゃ", ju: "じゅ", jyu: "じゅ", zyu: "じゅ",
  jo: "じょ", jyo: "じょ", zyo: "じょ", je: "じぇ", jye: "じぇ", zye: "じぇ", da: "だ", di: "ぢ",
  du: "づ", de: "で", do: "ど", dha: "でゃ", dhu: "でゅ", dho: "でょ", dya: "ぢゃ", dyu: "ぢゅ",
  dyo: "ぢょ", ba: "ば", bi: "び", bu: "ぶ", be: "べ", bo: "ぼ", bya: "びゃ", byu: "びゅ",
  byo: "びょ", pa: "ぱ", pi: "ぴ", pu: "ぷ", pe: "ぺ", po: "ぽ", pya: "ぴゃ", pyu: "ぴゅ",
  pyo: "ぴょ", twa: "とぁ", twi: "とぃ", twe: "とぇ", twu: "とぅ", two: "とぉ", dwa: "どぁ", dwi: "どぃ",
  dwe: "どぇ", dwu: "どぅ", dwo: "どぉ", kyi: "きぃ", kye: "きぇ", gyi: "ぎぃ", gye: "ぎぇ", syi: "しぃ",
  zyi: "じぃ", jyi: "じぃ", dyi: "ぢぃ", dye: "ぢぇ", hyi: "ひぃ", hye: "ひぇ", byi: "びぃ", bye: "びぇ",
  pyi: "ぴぃ", pye: "ぴぇ", myi: "みぃ", mye: "みぇ", ryi: "りぃ", rye: "りぇ", kwi: "くぃ", kwu: "くぅ",
  kwe: "くぇ", kwo: "くぉ", gwi: "ぐぃ", gwu: "ぐぅ", gwe: "ぐぇ", gwo: "ぐぉ", swa: "すぁ", swi: "すぃ",
  swu: "すぅ", swe: "すぇ", swo: "すぉ", zwa: "ずぁ", zwi: "ずぃ", zwu: "ずぅ", zwe: "ずぇ", zwo: "ずぉ",
  xa: "ぁ", la: "ぁ", xi: "ぃ", li: "ぃ", xyi: "ぃ", lyi: "ぃ", xu: "ぅ", lu: "ぅ",
  xe: "ぇ", le: "ぇ", xye: "ぇ", lye: "ぇ", xo: "ぉ", lo: "ぉ", xya: "ゃ", lya: "ゃ",
  xyu: "ゅ", lyu: "ゅ", xyo: "ょ", lyo: "ょ", xtu: "っ", ltu: "っ", xtsu: "っ", ltsu: "っ",
  xwa: "ゎ", lwa: "ゎ", xka: "ゕ", lka: "ゕ", xke: "ゖ", lke: "ゖ", xn: "ん", va: "ゔぁ",
  vi: "ゔぃ", vu: "ゔ", ve: "ゔぇ", vo: "ゔぉ", ye: "いぇ", wi: "うぃ", whi: "うぃ", we: "うぇ",
  whe: "うぇ", wyi: "ゐ", wye: "ゑ", thi: "てぃ", dhi: "でぃ", the: "てぇ", dhe: "でぇ", qi: "くぃ",
  qe: "くぇ", qo: "くぉ", ca: "か", ci: "し", cu: "く", co: "こ", cyi: "ちぃ", tyi: "ちぃ",
  kwa: "くぁ", gwa: "ぐぁ", tsa: "つぁ", tsi: "つぃ", tse: "つぇ", tso: "つぉ",
  nn: "ん", "n'": "ん",
  "-": "ー", ",": "、", ".": "。", "!": "！", "?": "？", "~": "〜", "[": "「", "]": "」", "(": "（", ")": "）",
};

const TABLE = new Map(Object.entries(BASE));
export const LONGEST = Math.max(...[...TABLE.keys()].map((key) => key.length));
// ある文字列が、まだどれかの綴りの途中かどうか
const PREFIXES = new Set([...TABLE.keys()].flatMap((key) => [...key].map((_, i) => key.slice(0, i + 1))));

const isConsonant = (c) => /[a-z]/.test(c) && !VOWELS.includes(c);

// 打ったローマ字をかなにする。kana は確定した部分、pending は綴りの途中で残っている部分。
// 打ち間違えた文字 (どの綴りにもならない文字) は、かなにせずそのまま kana に入れる。
// 打ったローマ字を、かなの 1 音ずつ (units: 綴り raw とかな kana の組) に分ける。pending は綴りの途中で残っている部分。
// 打ち間違えた文字 (どの綴りにもならない文字) は、その文字だけの 1 音にする
export function toUnits(romaji) {
  const raw = String(romaji);
  const text = raw.toLowerCase();
  const units = [];
  let i = 0;
  const push = (length, kana) => {
    units.push({ raw: raw.slice(i, i + length), kana });
    i += length;
  };
  while (i < text.length) {
    const c = text[i];
    const next = text[i + 1];
    // 子音を重ねたら「っ」(nn は「ん」なので除く)
    if (next !== undefined && c === next && isConsonant(c) && c !== "n") {
      push(1, "っ");
      continue;
    }
    // n の後に母音・y・n 以外が来たら「ん」
    if (c === "n" && next !== undefined && !VOWELS.includes(next) && next !== "y" && next !== "n" && next !== "'") {
      push(1, "ん");
      continue;
    }
    let matched = false;
    for (let length = Math.min(LONGEST, text.length - i); length > 0; length -= 1) {
      const value = TABLE.get(text.slice(i, i + length));
      if (value !== undefined) {
        push(length, value);
        matched = true;
        break;
      }
    }
    if (matched) continue;
    if (PREFIXES.has(text.slice(i))) return { units, pending: text.slice(i) };
    push(1, c);
  }
  return { units, pending: "" };
}

// 打ったローマ字をかなにする。kana は確定した部分、pending は綴りの途中で残っている部分。
// 打ち間違えた文字 (どの綴りにもならない文字) は、かなにせずそのまま kana に入れる。
export function toKana(romaji) {
  const { units, pending } = toUnits(romaji);
  return { kana: units.map((unit) => unit.kana).join(""), pending };
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
