/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { PAGES, render, rewriteUrl } from "../scripts/make-guide.mjs";

describe("使い方ガイド", () => {
  it("ほかの文書へのリンクをガイドのページにする", () => {
    assert.equal(rewriteUrl("WINDOWS.md"), "/guide/windows/");
    assert.equal(rewriteUrl("USAGE.md#データと辞書"), "/guide/#データと辞書");
    assert.equal(rewriteUrl("MACOS.md#ユーザー辞書"), "/guide/macos/#ユーザー辞書");
  });

  it("見出しの画像はガイドに写した場所を使う", () => {
    assert.equal(rewriteUrl("images/headings/usage/01.svg"), "/guide/images/headings/usage/01.svg");
    assert.match(render('<img src="images/headings/usage/01.svg" alt="キー操作">'), /src="\/guide\/images\/headings\/usage\/01\.svg"/);
  });

  it("ガイドに無い文書・ソースは GitHub で開く", () => {
    assert.equal(rewriteUrl("../README.md#プライバシー"), "https://github.com/yksr-melt/Meltype/blob/main/README.md#プライバシー");
    assert.equal(rewriteUrl("../linux/build-deb.sh"), "https://github.com/yksr-melt/Meltype/blob/main/linux/build-deb.sh");
    assert.equal(rewriteUrl("romaji-azik-sample.txt"), "https://github.com/yksr-melt/Meltype/blob/main/docs/romaji-azik-sample.txt");
  });

  it("外のリンクとページの中のリンクはそのまま", () => {
    assert.equal(rewriteUrl("https://example.com/a.md"), "https://example.com/a.md");
    assert.equal(rewriteUrl("#キー操作"), "#キー操作");
  });

  it("docs/ の文書がすべてあり、ほかの文書へのリンクがガイドの中で開ける", () => {
    for (const page of PAGES) {
      const file = new URL(`../../docs/${page.file}`, import.meta.url);
      assert.ok(existsSync(file), page.file);
      const html = render(readFileSync(file, "utf8"));
      // 直し忘れの .md へのリンク (サイトでは開けない) が残っていない
      assert.doesNotMatch(html, /href="(?!https?:)[^"]*\.md(?:#[^"]*)?"/, page.file);
    }
  });
});
