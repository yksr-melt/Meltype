/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピングの 1 文を採点し、お手本と関係ない入力 (名前など) が続いていないかを見る。ページ・CLI・サーバー (src/typing.js) で同じものを使う。
import { align, finalKana, toRomaji } from "./romaji.js";
import { isRomajiLayout } from "./typing-layouts.js";

// 打ったもの (typed) を読みにする。ローマ字の配列はローマ字の綴りなので、かなにする
export function kanaOf(layout, typed) {
  return isRomajiLayout(layout) ? finalKana(typed) : typed;
}

// かなにしたときの、お手本の読みとの一致率 (0〜1)
export function accuracyOf(reading, kana) {
  const wrong = align(reading, kana).filter((op) => op.op !== "same").length;
  return Math.max(0, 1 - wrong / [...reading].length);
}

// QWERTY のキーの位置。段ごとに右へずれている分を足し、縦横 1 キー以内を「隣のキー」とする
const QWERTY = ["1234567890-", "qwertyuiop", "asdfghjkl;", "zxcvbnm,./"];
const OFFSET = [0, 0.5, 0.75, 1.25];
const POSITION = new Map(QWERTY.flatMap((row, r) => [...row].map((char, c) => [char, [r, c + OFFSET[r]]])));

function near(a, b) {
  if (!a || !b) return false;
  if (a === b) return true;
  const p = POSITION.get(a);
  const q = POSITION.get(b);
  return Boolean(p && q && Math.abs(p[0] - q[0]) <= 1 && Math.abs(p[1] - q[1]) <= 1.1);
}

// 押し間違いとは思えない字がこの数だけ続いたら、お手本と関係ない入力とみなす
export const SUSPICIOUS_RUN = { keys: 3, kana: 4 };

// お手本と関係ない入力が続いているか。
// QWERTY のローマ字打ちは、お手本のローマ字と打ったキーを並べ、お手本の字の隣のキーでもない字が続いているかを見る
// (押し間違いはたいてい隣のキーになる。si/shi のような綴りの違いは、1 字ずつ途切れるので続かない)。
// どの配列でも、かなにしてお手本と違う字が続いているかも見る (キーの並びが違う配列は、こちらだけで見る)
export function suspicious(reading, typed, layout) {
  let run = 0;
  if (layout === "qwerty") {
    const want = toRomaji(reading);
    const ops = align(want, typed.toLowerCase());
    for (let i = 0; i < ops.length; i += 1) {
      const op = ops[i];
      if (op.op === "omit") continue;
      let odd = false;
      if (op.op === "sub") odd = !near(op.got, op.want);
      if (op.op === "extra") {
        // 余計な字は、前後のお手本の字のどちらかの隣なら押し間違い
        const before = ops.slice(0, i).reverse().find((o) => o.want);
        const after = ops.slice(i + 1).find((o) => o.want);
        odd = !near(op.got, before && before.want) && !near(op.got, after && after.want);
      }
      run = odd ? run + 1 : 0;
      if (run >= SUSPICIOUS_RUN.keys) return true;
    }
    run = 0;
  }
  for (const op of align(reading, kanaOf(layout, typed))) {
    if (op.op === "omit") continue;
    run = op.op === "same" ? 0 : run + 1;
    if (run >= SUSPICIOUS_RUN.kana) return true;
  }
  return false;
}
