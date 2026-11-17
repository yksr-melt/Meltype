/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピングの 1 文の打鍵から、打った文字を組み立てて採点し、お手本と関係ない入力 (名前など) が続いていないかを見る。
// ページとサーバー (src/typing.js) で同じものを使う。
// 決まり: 送るのは打鍵 (keys) だけにし、打った文字はここで打鍵から組み立てる (直したあとの文字列を、打った文字列と取り違えないように)。
import { align, finalKana, toRomaji } from "./romaji.js";

// 打鍵 ([code, key, 押した時刻, 離した時刻]) を頭から打ち直し、Enter を押したときに残っていた文字にする
export function replay(keys) {
  let typed = "";
  for (const [, key] of keys) {
    if (key === "Backspace") typed = typed.slice(0, -1);
    else if (key.length === 1 && key >= " " && key <= "~") typed += key;
  }
  return typed;
}

// 打った文字とお手本を、かなにして比べる。お手本に英字 (日本語と英語が混ざった文) があれば、英字の部分も両方を同じ規則でかなにするので、
// 正しく打てば一致する (si と shi のような綴りの違いも、かなにすれば同じになる)
export function compare(reading, typed) {
  return align(finalKana(toRomaji(reading)), finalKana(typed.toLowerCase()));
}

// かなにしたときの、お手本との一致率 (0〜1)
export function accuracyOf(reading, typed) {
  const ops = compare(reading, typed);
  const want = ops.filter((op) => op.want).length;
  return want ? Math.max(0, 1 - ops.filter((op) => op.op !== "same").length / want) : 0;
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
// お手本のローマ字と打った文字を並べ、お手本の字の隣のキーでもない字が続いているかを見る
// (押し間違いはたいてい隣のキーになる。si/shi のような綴りの違いは、1 字ずつ途切れるので続かない)。
// かなにしてお手本と違う字が続いているかも見る
export function suspicious(reading, typed) {
  let run = 0;
  const ops = align(toRomaji(reading), typed.toLowerCase());
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
  for (const op of compare(reading, typed)) {
    if (op.op === "omit") continue;
    run = op.op === "same" ? 0 : run + 1;
    if (run >= SUSPICIOUS_RUN.kana) return true;
  }
  return false;
}
