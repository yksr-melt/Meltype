/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピング (/typing/) で選べる配列。Meltype の配列 (src/Meltype.Core/Input/KanaLayout.cs) をブラウザーのキー (KeyboardEvent.code) で動かす。
// 決まり: 表と打ち方 (前置シフト・親指シフト・中指シフト) は KanaLayout.cs に合わせる。あちらを変えたら、こちらも直す。
// ローマ字の配列は打ったローマ字の綴りを、かなの配列は打ったかなを返す (送る typed もその形。romaji.js の finalKana で読みにするのはローマ字の配列だけ)。

export const LAYOUTS = [
  { id: "qwerty", label: "ローマ字打ち", romaji: true },
  { id: "jis-kana", label: "かな打ち (JIS かな)", romaji: false },
  { id: "sky", label: "SKY 配列", romaji: true },
  { id: "tsuki", label: "月配列", romaji: false },
  { id: "hana", label: "花配列", romaji: false },
  { id: "nicola", label: "NICOLA (親指シフト)", romaji: false },
  { id: "nakayubi-nicola", label: "中指NICOLA", romaji: false },
  { id: "tron", label: "TRON 配列", romaji: false },
];

export const LAYOUT_IDS = LAYOUTS.map((layout) => layout.id);
export const isRomajiLayout = (id) => LAYOUTS.some((layout) => layout.id === id && layout.romaji);

// 表の行 (数字の段・Q の段・A の段・Z の段)。日本語 106/109 キーボードのキーを、ブラウザーの code で書く
const ROWS = [
  ["Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal", "IntlYen"],
  ["KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "BracketLeft", "BracketRight"],
  ["KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Backslash"],
  ["KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM", "Comma", "Period", "Slash", "IntlRo"],
];

// 親指キー。Windows・Linux の無変換・変換と、Mac の英数・かな
const LEFT_THUMB = new Set(["NonConvert", "Lang2"]);
const RIGHT_THUMB = new Set(["Convert", "Lang1"]);
// 文字キーの後に親指キーを押したとき、同時打鍵とみなす時間 (KanaLayout.cs の ChordWindowMs)
const CHORD_MS = 100;

// JIS かな: [シフトなし, Shift]
const JIS = [
  ["ぬ,ふ,あ,う,え,お,や,ゆ,よ,わ,ほ,へ,ー", ",,ぁ,ぅ,ぇ,ぉ,ゃ,ゅ,ょ,を,,,"],
  ["た,て,い,す,か,ん,な,に,ら,せ,゛,゜", ",,ぃ,,,,,,,,,「"],
  ["ち,と,し,は,き,く,ま,の,り,れ,け,む", ",,,,,,,,,,,」"],
  ["つ,さ,そ,ひ,こ,み,も,ね,る,め,ろ", "っ,,,,,,,、,。,・,"],
];

// KanaLayout.cs の Tables と同じ。前置シフトの配列は [シフトなし, 1 のシフト, 2 のシフト]、親指シフトの配列は [シフトなし, 左, 右]
const NICOLA = [
  [",,,,,,,,,,ー,,", "。,か,た,こ,さ,ら,ち,く,つ,、,、,゛", "う,し,て,け,せ,は,と,き,い,ん,,", "。,ひ,す,ふ,へ,め,そ,ね,ほ,・,"],
  [",・,,,,,,,,,,,", "ぁ,え,り,ゃ,れ,ぱ,ぢ,ぐ,づ,ぴ,,゜", "を,あ,な,ゅ,も,ば,ど,ぎ,ぽ,,,", "ぅ,ー,ろ,や,ぃ,ぷ,ぞ,ぺ,ぼ,,"],
  [",・,,,,,,,,,,,", ",が,だ,ご,ざ,よ,に,る,ま,ぇ,,゜", "ゔ,じ,で,げ,ぜ,み,お,の,ょ,っ,,", ",び,ず,ぶ,べ,ぬ,ゆ,む,わ,ぉ,"],
];
const TABLES = {
  nicola: NICOLA,
  "nakayubi-nicola": NICOLA,
  tron: [
    [",,,,,,,,,,ー,,", "ら,る,こ,は,ょ,き,の,く,あ,れ,ー,", "た,と,か,て,も,を,い,う,し,ん,,", "ま,り,に,さ,な,す,つ,、,。,っ,"],
    [",・,,,,,,,,,,,", "ひ,そ,・,ゃ,ほ,ぎ,げ,ぐ,,ゐ,,", "ぬ,ね,ゅ,よ,ふ,゛,ぢ,ゔ,じ,ゑ,,", "ぇ,ぉ,せ,ゆ,へ,ず,づ,、,。,ゎ,"],
    [",・,,,,,,,,,,,", "び,ぞ,ご,ば,ぼ,え,け,め,む,ろ,,", "だ,ど,が,で,ぶ,お,ち,ー,み,や,,", "ゕ,ゖ,ぜ,ざ,べ,わ,ぃ,ぁ,゜,ぅ,"],
  ],
  tsuki: [
    [",,,,,,,,,,,,", "ゅ,と,し,こ,ょ,つ,ん,い,か,り,ー,", "は,2,1,て,た,く,う,1,2,き,ち,", "す,を,に,な,さ,っ,る,の,も,へ,・"],
    [",,,,,,,,,,,,", "ぁ,ゃ,ね,ら,め,ぬ,む,み,え,ぇ,,", "ぃ,け,、,あ,れ,ま,お,ほ,そ,ろ,,", "ぅ,や,せ,よ,ゆ,ひ,わ,ふ,ぃ,ぉ,"],
    [",,,,,,,,,,,,", ",ど,じ,ご,,づ,ぴ,ぽ,が,,,", "ば,げ,。,で,だ,ぐ,ゔ,ぼ,ぞ,ぎ,ぢ,", "ず,ぱ,ぜ,,ざ,び,ぷ,ぶ,ぺ,べ,"],
  ],
  hana: [
    [",,,,,,,,,,,,ぇ", "ょ,て,と,こ,は,っ,く,う,る,ら,ー,", "す,か,1,き,た,ん,れ,1,゛,ろ,り,", "さ,し,な,の,に,い,つ,。,、,め,え"],
    [",,,,,,,,,,,,え", "ひ,け,ぇ,ほ,へ,ゃ,ま,そ,も,ぃ,,", "ぁ,よ,ゅ,や,゜,・,ふ,ち,む,ぉ,,", "ぅ,せ,あ,わ,ゆ,ね,み,を,お,ぬ,"],
  ],
};

// SKY 配列: QWERTY の位置のキーを、ローマ字の綴りにする (KanaLayout.cs の SkyLayout)
const SKY = {
  KeyQ: ",", KeyW: "w", KeyE: "r", KeyR: "m", KeyT: "h", KeyY: "uu", KeyU: "ai", KeyI: "ou", KeyO: ".", KeyP: "ei",
  KeyA: "n", KeyS: "t", KeyD: "s", KeyF: "k", KeyG: "y", KeyH: "u", KeyJ: "a", KeyK: "o", KeyL: "i", Semicolon: "e",
  KeyZ: "p", KeyX: "d", KeyC: "z", KeyV: "g", KeyB: "b", KeyN: "unn", KeyM: "ann", Comma: "onn", Period: "inn", Slash: "enn",
};

function face(rows) {
  const map = new Map();
  rows.forEach((row, r) => {
    row.split(",").forEach((cell, c) => {
      if (cell && ROWS[r][c]) map.set(ROWS[r][c], cell);
    });
  });
  return map;
}

const isLeftHand = (code) => ROWS.some((row) => row.indexOf(code) >= 0 && row.indexOf(code) < 5);

// 1 打鍵の結果: { remove: 先に消す字の数, text: 足す字 }。null はこの配列のキーではない (ふつうのキーとして扱う)
const stroke = (text, remove = 0) => ({ remove, text });

// 打ったものに 1 打鍵の結果を足す。゛・゜は前の字に付ける (付けられなければ、そのまま残す)
export function applyStroke(typed, { remove, text }) {
  let out = remove ? [...typed].slice(0, -remove).join("") : typed;
  for (const char of text) {
    const mark = char === "゛" ? "゙" : char === "゜" ? "゚" : "";
    const last = [...out].pop();
    const joined = mark && last ? (last + mark).normalize("NFC") : "";
    out = joined.length === 1 ? out.slice(0, -last.length) + joined : out + char;
  }
  return out;
}

// 配列ごとの打ち方。down(code, key, shift, time) と up(code) が 1 打鍵の結果か null を返す
export function createLayout(id) {
  if (id === "qwerty") {
    return {
      down: (code, key) => (key.length === 1 && key >= " " && key <= "~" ? stroke(key) : null),
      up: () => null,
    };
  }
  if (id === "sky") {
    return {
      down: (code, key, shift) => (!shift && SKY[code] ? stroke(SKY[code]) : key.length === 1 && key >= " " && key <= "~" ? stroke(key) : null),
      up: () => null,
    };
  }
  if (id === "jis-kana") {
    const plain = face(JIS.map((row) => row[0]));
    const shifted = face(JIS.map((row) => row[1]));
    return {
      down: (code, key, shift) => {
        const kana = (shift ? shifted : plain).get(code);
        return kana ? stroke(kana) : null;
      },
      up: () => null,
    };
  }
  return layoutEngine(id);
}

// 月・花 (前置シフト)、NICOLA・TRON (親指シフト)、中指NICOLA。KanaLayoutEngine (KanaLayout.cs) を写したもの
function layoutEngine(id) {
  const faces = TABLES[id].map(face);
  const prefixShift = id === "tsuki" || id === "hana";
  const nakayubi = id === "nakayubi-nicola";
  let prefix = 0;
  let last = { code: "", time: -Infinity, shifted: true, released: false };
  let thumb = "";
  let thumbUsed = false;
  let middle = "";
  let middleUsed = false;
  let middlePending = "";
  let middlePendingKana = "";

  const emit = (code, kana, shifted, time = -Infinity) => {
    last = { code, time, shifted, released: false };
    return stroke(kana);
  };

  return {
    down(code, key, shift, time) {
      const thumbFace = LEFT_THUMB.has(code) ? 1 : RIGHT_THUMB.has(code) ? 2 : 0;
      if (!prefixShift && thumbFace) {
        if (code === thumb) return stroke("");
        // 文字キーを押している間 (押してから CHORD_MS 以内) に押した親指キー: その文字をシフト面で打ち直す
        const again = faces[thumbFace].get(last.code);
        thumb = code;
        if (!last.shifted && !last.released && time - last.time <= CHORD_MS && again) {
          last.shifted = true;
          thumbUsed = true;
          return stroke(again, 1);
        }
        thumbUsed = false;
        return stroke("");
      }
      // Shift (小指シフト) を押しているキーは、ふつうのキー (英字・記号) として扱う
      if (shift) return null;
      const plain = faces[0].get(code);
      if (plain === undefined && !(prefix > 0 && faces[prefix].has(code))) return null;

      if (prefixShift) {
        if (prefix > 0) {
          const kana = faces[prefix].get(code);
          prefix = 0;
          return kana && kana !== "1" && kana !== "2" ? emit(code, kana, true) : stroke("");
        }
        if (plain === "1" || plain === "2") {
          prefix = Number(plain);
          return stroke("");
        }
        return emit(code, plain, true);
      }

      if (thumb) {
        thumbUsed = true;
        const kana = faces[LEFT_THUMB.has(thumb) ? 1 : 2].get(code);
        return kana ? emit(code, kana, true) : stroke("");
      }
      // 中指NICOLA: 押したままの K + 左手のキー → 左シフト、D + 右手のキー → 右シフト (先に文字キーを離したら打ち直す)
      if (nakayubi && code === middle) return stroke("");
      middlePending = "";
      if (nakayubi && middle && isLeftHand(code) === (middle === "KeyK")) {
        const kana = faces[middle === "KeyK" ? 1 : 2].get(code);
        if (kana) {
          middlePending = code;
          middlePendingKana = kana;
        }
      }
      if (nakayubi && (code === "KeyD" || code === "KeyK") && !middle) {
        middle = code;
        middleUsed = false;
      }
      return plain === undefined ? null : emit(code, plain, false, time);
    },
    up(code) {
      if (code === last.code) last.released = true;
      if (code === middlePending && middle) {
        middlePending = "";
        const remove = middleUsed ? 1 : 2;
        middleUsed = true;
        emit(code, middlePendingKana, true);
        return stroke(middlePendingKana, remove);
      }
      if (code === middle) {
        middle = "";
        middlePending = "";
      }
      if (code === thumb) thumb = "";
      return null;
    },
  };
}
