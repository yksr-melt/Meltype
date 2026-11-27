/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Meltype Contributors */

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

  it("Meltype 本体で打てる綴り (c 行・q・wh など) でも打てる", () => {
    for (const [typed, kana] of [
      ["caga", "かが"],
      ["cocoro", "こころ"],
      ["cyotto", "ちょっと"],
      ["qo-to", "くぉーと"],
      ["whi-ru", "うぃーる"],
      ["kwa", "くぁ"],
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
  const { accuracyOf, previewKana, replay, suspicious } = await import("../public/assets/js/typing-check.js");
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

  const keysOf = (text) => [...text].map((char) => [char === "<" ? "Backspace" : "Key", char === "<" ? "Backspace" : char, 0, 0]);

  it("Backspace は Meltype と同じく、日本語は 1 音・打ちかけと英語は 1 文字消す", () => {
    const reading = "きょうはgoogleでしらべた。";
    assert.equal(replay(keysOf("kyouha<"), reading), "kyou");
    assert.equal(replay(keysOf("kyo<"), reading), "");
    assert.equal(replay(keysOf("ky<"), reading), "k");
    assert.equal(replay(keysOf("kyouhagoog<"), reading), "kyouhagoo");
    assert.equal(replay(keysOf("kyouhagoogle<<"), reading), "kyouhagoog");
    assert.equal(replay(keysOf("atta<"), "あった"), "at");
    // お手本の英語の語の頭と同じ綴りの日本語の 1 音は、日本語として 1 音消す
    assert.equal(replay(keysOf("kono<"), "このnoteにはれんしゅうのきろくがある。"), "ko");
    assert.equal(replay(keysOf("game<"), "がめんにならぶmenuからのみものをえらんだ。"), "ga");
    assert.equal(replay(keysOf("ekima<"), "えきまでのるーとをmapでしらべた。"), "eki");
    assert.equal(replay(keysOf("kononote<"), "このnoteにはれんしゅうのきろくがある。"), "kononot");
  });

  it("版 1 の記録は、Backspace で 1 文字消していたときの決まりで組み立てる", () => {
    const keys = [["KeyK", "k", 0, 10], ["KeyY", "y", 20, 30], ["KeyP", "p", 40, 50], ["Backspace", "Backspace", 60, null], ["Backspace", "Backspace", 90, null], ["ShiftLeft", "Shift", 100, 120], ["KeyO", "O", 110, 130]];
    assert.equal(replay(keys, "きょう", 1), "kO");
  });

  it("表示と削除は同じかな・英字の境目を使う", () => {
    for (const [typed, reading, kana, erased] of [
      ["kaigi", "aiのかいぎ", "かいぎ", "kai"],
      ["kashi", "hiといった", "かし", "ka"],
      ["kononote", "このnote", "このnote", "kononot"],
      ["konnote", "こんnote", "こんnote", "konnot"],
      ["apple", "appとapple", "apple", "appl"],
      ["googleka", "googleか", "googleか", "google"],
    ]) {
      assert.deepEqual(previewKana(typed, reading), { kana, pending: "" }, typed);
      assert.equal(replay(keysOf(typed + "<"), reading), erased, typed);
    }
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
