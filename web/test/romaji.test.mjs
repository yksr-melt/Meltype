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

describe("お手本のローマ字と、関係ない入力", async () => {
  const { toRomaji } = await import("../public/assets/js/romaji.js");
  const { accuracyOf, replay, suspicious } = await import("../public/assets/js/typing-check.js");
  const reading = "きょうはあさからあめがふっている。";

  it("「！？〜「」（）…」は ! ? ~ [ ] ( ) と . 3 回で打つ", () => {
    assert.equal(accuracyOf("まじ！？それな〜", "maji!?sorena~"), 1);
    assert.equal(accuracyOf("まあいいか…", "maaiika..."), 1);
    assert.ok(accuracyOf("まあいいか…", "maaiika.") < 1);
    assert.equal(accuracyOf("「ええ」（こんわく）", "[ee](konwaku)"), 1);
  });

  it("お手本どおりに打てば、英語が混ざった文も一致率は 1", () => {
    for (const sentence of SENTENCES) assert.equal(accuracyOf(sentence.reading, toRomaji(sentence.reading)), 1, sentence.id);
    assert.equal(accuracyOf("きょうはgoogleでしらべた。", "KyouhaGoogledesirabeta."), 1);
  });

  it("打鍵から打った文字を組み立てる (Backspace の押しっぱなしも 1 回ずつ消す)", () => {
    const keys = [["KeyK", "k", 0, 10], ["KeyY", "y", 20, 30], ["KeyP", "p", 40, 50], ["Backspace", "Backspace", 60, null], ["Backspace", "Backspace", 90, null], ["ShiftLeft", "Shift", 100, 120], ["KeyO", "O", 110, 130]];
    assert.equal(replay(keys), "kO");
  });

  it("押し間違い・綴りの違いは止めない", () => {
    for (const typed of ["kyouhaasakaraamegafutteiru.", "kyouhaasakaramegafuteiru.", "kixyouhaasakaraamegahutteiru.", "kyoyhaasaakaraamegafutteiru.", "kyouha"]) {
      assert.equal(suspicious(reading, typed), false, typed);
    }
    assert.equal(suspicious("じゅうでん", "zyuudenn"), false);
    assert.equal(suspicious("ちょっと", "tyoltuto"), false);
  });

  it("お手本と関係ないキーが続いたら止める", () => {
    for (const typed of ["kyouhaasakaraamegafutteiru.tanaka", "yamada taro desu", "asdfasdf"]) {
      assert.equal(suspicious(reading, typed), true, typed);
    }
    assert.equal(suspicious("このbugはまだfixされていない。", "konobughamadafixsareteinai."), false);
    assert.equal(suspicious("このbugはまだfixされていない。", "konobughamadafixsareteinai.yamadadesu"), true);
  });
});
