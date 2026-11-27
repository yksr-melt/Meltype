/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Meltype Contributors */

import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { parse, readSentences, sentenceId } from "../scripts/make-sentences.mjs";

describe("お手本の txt", () => {
  it("今のお手本は読める", () => {
    const { kinds, sentences } = readSentences();
    assert.deepEqual(kinds.map((kind) => kind.id), ["plain", "formal", "chat", "mixed"]);
    assert.ok(sentences.length > 0);
  });

  it("文と読みから id を作るので、直すと別の文になる", () => {
    const [a] = parse([["1-plain.txt", "種類: ふつう\nあめ。|あめ。\n"]]).sentences;
    const [b] = parse([["1-plain.txt", "種類: ふつう\n雨。|あめ。\n"]]).sentences;
    assert.equal(a.id, sentenceId("あめ。", "あめ。"));
    assert.notEqual(a.id, b.id);
  });

  it("「！？〜…」を読みに書ける", () => {
    assert.equal(parse([["9-x.txt", "種類: x\nまじ！？|まじ！？\nまあいいか…|まあいいか…\nえ〜、ほんとに？|えー、ほんとに？\n"]]).sentences.length, 3);
  });

  it("「出さない:」を付けた文も id を引けるように残す", () => {
    const { sentences } = parse([["1-plain.txt", "種類: ふつう\n出さない:雨。|あめ。\n雪。|ゆき。\n"]]);
    assert.deepEqual(sentences.map((s) => [s.text, Boolean(s.retired)]), [["雨。", true], ["雪。", false]]);
    assert.throws(() => parse([["1-plain.txt", "種類: ふつう\n出さない:雨。|あめ。\n"]]), /出す文が 1 つもありません/);
  });

  it("間違いは行番号付きでまとめて知らせる", () => {
    assert.throws(
      () => parse([["1-plain.txt", "種類: ふつう\n# コメント\n雨。|あめ\n雨|Ame\n雨\n雨。|あめ。\n雪。|あめ。\n"]]),
      (e) =>
        e.message.includes("1-plain.txt:3: 文と読みで「。」の数が違います") &&
        e.message.includes("1-plain.txt:4: 読みは、ひらがな") &&
        e.message.includes("1-plain.txt:5: 「表示する文|読み」の形") &&
        e.message.includes("1-plain.txt:7: 同じ読みの文が 1-plain.txt:6 にあります"),
    );
    assert.throws(() => parse([["plain.txt", "種類: ふつう\nあ|あ\n"]]), /ファイル名/);
    assert.throws(() => parse([["1-plain.txt", "あ|あ\n"]]), /種類: 表示名/);
  });
});
