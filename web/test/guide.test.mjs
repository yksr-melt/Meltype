/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { PAGES, render, rewriteUrl, slugger } from "../scripts/make-guide.mjs";

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

  it("見出しに GitHub と同じ id を付ける", () => {
    const slug = slugger();
    assert.equal(slug("英字を主に入力したいとき"), "英字を主に入力したいとき");
    assert.equal(slug("ATOK 風の操作で使いたいとき"), "atok-風の操作で使いたいとき");
    assert.equal(slug("/ $ @ で始まる語 (AI)"), "---で始まる語-ai");
    assert.equal(slug("ATOK 風の操作で使いたいとき"), "atok-風の操作で使いたいとき-1");
    assert.match(render("## データと辞書"), /<h2 id="データと辞書">データと辞書<\/h2>/);
  });

  it("ガイドの中のページ内リンク・ほかのページの見出しへのリンクは、すべて行き先がある", () => {
    const html = new Map(PAGES.map((p) => [p.slug ? `/guide/${p.slug}/` : "/guide/", render(readFileSync(new URL(`../../docs/${p.file}`, import.meta.url), "utf8"))]));
    const targets = (page) => new Set([...html.get(page).matchAll(/\s(?:id|name)="([^"]*)"/g)].map((m) => m[1]));
    for (const [page, body] of html) {
      for (const [, href] of body.matchAll(/href="([^"]*#[^"]*)"/g)) {
        const [path, hash] = href.split("#");
        const target = path === "" ? page : path;
        if (!html.has(target)) continue;
        assert.ok(targets(target).has(decodeURIComponent(hash)), `${page} → ${href}`);
      }
    }
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
