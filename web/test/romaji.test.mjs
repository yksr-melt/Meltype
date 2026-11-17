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
  const { suspicious } = await import("../public/assets/js/typing-check.js");
  const reading = "きょうはあさからあめがふっている。";

  it("お手本の読みは、ローマ字にしてもとのかなに戻る (どの文も打てる)", () => {
    for (const sentence of SENTENCES) assert.equal(finalKana(toRomaji(sentence.reading)), sentence.reading, sentence.id);
  });

  it("押し間違い・綴りの違いは止めない", () => {
    for (const typed of ["kyouhaasakaraamegafutteiru.", "kyouhaasakaramegafuteiru.", "kixyouhaasakaraamegahutteiru.", "kyoyhaasaakaraamegafutteiru.", "kyouha"]) {
      assert.equal(suspicious(reading, typed, "qwerty"), false, typed);
    }
    assert.equal(suspicious("じゅうでん", "zyuudenn", "qwerty"), false);
    assert.equal(suspicious("ちょっと", "tyoltuto", "qwerty"), false);
  });

  it("お手本と関係ないキーが続いたら止める", () => {
    for (const typed of ["kyouhaasakaraamegafutteiru.tanaka", "yamada taro desu", "asdfasdf"]) {
      assert.equal(suspicious(reading, typed, "qwerty"), true, typed);
    }
    assert.equal(suspicious(reading, "きょうはあさからあめがふっている。たなかです", "jis-kana"), true);
    assert.equal(suspicious(reading, "きょうはあさかあめがふっている", "jis-kana"), false);
  });
});

describe("配列", async () => {
  const { applyStroke, createLayout } = await import("../public/assets/js/typing-layouts.js");
  const type = (id, keys) => {
    const layout = createLayout(id);
    let typed = "";
    let time = 0;
    for (const [code, options = {}] of keys) {
      time += options.gap ?? 200;
      const down = layout.down(code, options.key ?? "", Boolean(options.shift), time);
      if (down) typed = applyStroke(typed, down);
      if (!options.hold) {
        const up = layout.up(code);
        if (up) typed = applyStroke(typed, up);
      }
    }
    return typed;
  };

  it("JIS かなは、゛を前の字に付ける", () => {
    assert.equal(type("jis-kana", [["KeyT"], ["BracketLeft"], ["KeyQ"]]), "がた");
    assert.equal(type("jis-kana", [["KeyZ", { shift: true }], ["Digit7", { shift: true }]]), "っゃ");
  });

  it("月配列は D・K の後のキーをシフト面で読む", () => {
    // D・K は 1 のシフト。D + J → お、K + F → あ。シフトなしの J は う
    assert.equal(type("tsuki", [["KeyD"], ["KeyJ"], ["KeyK"], ["KeyF"], ["KeyJ"]]), "おあう");
  });

  it("NICOLA は親指キーを押したままの字をシフト面で読み、すぐ後に押した親指キーで打ち直す", () => {
    assert.equal(type("nicola", [["NonConvert", { hold: true }], ["KeyS"]]), "あ");
    assert.equal(type("nicola", [["KeyS", { hold: true }], ["Convert", { gap: 50 }]]), "じ");
    assert.equal(type("nicola", [["KeyS"], ["Convert", { gap: 300 }]]), "し");
  });

  it("SKY 配列はキーをローマ字の綴りにする", () => {
    assert.equal(type("sky", [["KeyF"], ["KeyJ"]]), "ka");
  });
});
