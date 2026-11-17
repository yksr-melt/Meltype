/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { align, finalKana, toKana } from "../public/assets/js/romaji.js";
import { SENTENCES } from "../public/assets/js/typing-sentences.js";

describe("ローマ字→かな", () => {
  it("ヘボン式・訓令式・小さい字の打ち方のどれでも同じかなになる", () => {
    for (const [typed, kana] of [
      ["shinkansen", "しんかんせん"],
      ["sinkansenn", "しんかんせん"],
      ["kyouhaasa", "きょうはあさ"],
      ["kixyouhaasa", "きょうはあさ"],
      ["chotto", "ちょっと"],
      ["tyoltuto", "ちょっと"],
      ["kin'youbi", "きんようび"],
      ["ko-hi-", "こーひー"],
      ["tsukue,", "つくえ、"],
      ["sumaatofon", "すまあとふぉん"],
    ]) {
      assert.equal(finalKana(typed), kana, typed);
    }
  });

  it("綴りの途中は pending に残す", () => {
    assert.deepEqual(toKana("kyo"), { kana: "きょ", pending: "" });
    assert.deepEqual(toKana("ky"), { kana: "", pending: "ky" });
    assert.deepEqual(toKana("kon"), { kana: "こ", pending: "n" });
    assert.deepEqual(toKana("att"), { kana: "あっ", pending: "t" });
  });

  it("打ち間違えた文字はそのまま残す", () => {
    assert.equal(finalKana("kqa"), "kqあ");
  });

  it("お手本の読みはすべてかなと「ー、。」だけ", () => {
    const ids = new Set();
    for (const sentence of SENTENCES) {
      assert.match(sentence.reading, /^[ぁ-ゖー、。]+$/, sentence.id);
      assert.ok(!ids.has(sentence.id), sentence.id);
      ids.add(sentence.id);
    }
  });
});

describe("お手本と打ったものの並べ方", () => {
  it("抜け・置き換え・余計な字を分ける", () => {
    const kinds = (a, b) => align(a, b).map((op) => op.op + (op.want || op.got));
    assert.deepEqual(kinds("きょうは", "きうは"), ["sameき", "omitょ", "sameう", "sameは"]);
    assert.deepEqual(kinds("あめ", "あね"), ["sameあ", "subめ"]);
    assert.deepEqual(kinds("あめ", "あめい"), ["sameあ", "sameめ", "extraい"]);
  });

  it("同じなら全部 same", () => {
    assert.ok(align("あめ", "あめ").every((op) => op.op === "same"));
  });
});
