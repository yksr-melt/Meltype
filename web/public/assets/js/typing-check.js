/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// タイピングの 1 文の打鍵から、打った文字を組み立てて採点し、お手本と関係ない入力 (名前など) が続いていないかを見る。
// ページとサーバー (src/typing.js) で同じものを使う。
// 決まり: 送るのは打鍵 (keys) だけにし、打った文字はここで打鍵から組み立てる (直したあとの文字列を、打った文字列と取り違えないように)。
import { align, finalKana, toRomaji, toUnits } from "./romaji.js";

// 打鍵から打った文字を組み立てる決まりの版。記録 (typing_trials.version) に残し、組み立てるときに使う。
// 決まり: 組み立て方を変えたら 1 つ上げ、前の版の組み立て方も残す (前に集めた打鍵は、そのとき画面に出ていた決まりで読むため)。
//   1: Backspace で 1 文字消す
//   2: Backspace で Meltype と同じく、日本語は 1 音 (ka なら 2 文字)、英語と打ちかけの綴りは 1 文字消す
export const TYPING_VERSION = 2;

// 表示と Backspace で同じ単位を使う。英単語はかなの境目 (残りが無いか n だけ) でだけ探し、1 文字を 1 単位にする。
// ワークアラウンド: 打った字を Meltype のエンジンで組み立てるようにしたら消す。
function typingUnits(typed, reading) {
  const words = (reading.match(/[a-z]+/g) ?? []).sort((a, b) => b.length - a.length);
  const units = [];
  let start = 0;
  let i = 0;
  while (i < typed.length) {
    const before = toUnits(typed.slice(start, i));
    const word = (before.pending === "" || before.pending === "n") && words.find((w) => typed.slice(i, i + w.length).toLowerCase() === w);
    if (word) {
      units.push(...before.units);
      if (before.pending) units.push({ raw: typed.slice(i - 1, i), kana: "ん" });
      for (const char of typed.slice(i, i + word.length)) units.push({ raw: char, kana: char });
      i += word.length;
      start = i;
    } else {
      i += 1;
    }
  }
  const rest = toUnits(typed.slice(start));
  return { units: [...units, ...rest.units], pending: rest.pending };
}

export function previewKana(typed, reading) {
  const { units, pending } = typingUnits(typed, reading);
  return { kana: units.map((unit) => unit.kana).join(""), pending };
}

function backspace(typed, reading) {
  const { units, pending } = typingUnits(typed, reading);
  if (pending || units.length === 0) return typed.slice(0, -1);
  return typed.slice(0, -units[units.length - 1].raw.length);
}

// 打鍵 ([code, key, 押した時刻, 離した時刻]) を頭から打ち直し、Enter を押したときに残っていた文字にする
export function replay(keys, reading, version = TYPING_VERSION) {
  let typed = "";
  for (const [, key] of keys) {
    if (key === "Backspace") typed = version >= 2 ? backspace(typed, reading) : typed.slice(0, -1);
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
