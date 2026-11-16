// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
// docs/ の利用者向けの文書 (使い方・OS ごとの説明) から、公式サイトの使い方ガイド (public/guide/) を作る。
// 文書は docs/ の Markdown だけを直す (public/guide/ は作るたびに消して作り直すので、コミットしない)。
//   node scripts/make-guide.mjs
// デプロイ (npm run deploy) の前にも実行する。
import { cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, posix, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { Marked } from "marked";

const web = join(dirname(fileURLToPath(import.meta.url)), "..");
const docs = join(web, "..", "docs");
const out = join(web, "public", "guide");
const repo = "https://github.com/yksr-melt/Meltype/blob/main/";

// 文書とページの場所。題名はナビゲーションに出す名前
export const PAGES = [
  { file: "USAGE.md", slug: "", title: "使い方" },
  { file: "WINDOWS.md", slug: "windows", title: "Windows" },
  { file: "MACOS.md", slug: "macos", title: "Mac" },
  { file: "LINUX.md", slug: "linux", title: "Linux" },
];

const pageUrl = (slug) => (slug ? `/guide/${slug}/` : "/guide/");

/** 文書の中のリンク・画像の行き先を、サイトの中の場所に直す。外のリンクと # だけのリンクはそのまま。 */
export function rewriteUrl(url) {
  if (/^(?:[a-z]+:|#|\/)/i.test(url)) return url;
  const [path, hash] = url.split("#", 2);
  const suffix = hash === undefined ? "" : `#${hash}`;
  const page = PAGES.find((p) => p.file === path);
  if (page) return pageUrl(page.slug) + suffix;
  if (path.startsWith("images/")) return `/guide/${path}`;
  // ほかの文書・ソース (../README.md・../linux/build-deb.sh など) は GitHub で開く
  return repo + posix.normalize(posix.join("docs", path)) + suffix;
}

const escapeHtml = (s) => s.replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" })[c]);

/**
 * GitHub と同じ見出しのアンカー (小文字にし、英数字・かな漢字・空白・- _ 以外を消し、空白を - にする。同じものは -1, -2 …)。
 * 文書の中のリンク (USAGE.md#英字を主に入力したいとき) は GitHub の見出しのアンカーを指すので、ガイドでも同じ id にする。
 */
export function slugger() {
  const seen = new Map();
  return (text) => {
    const base = text.trim().toLowerCase().replace(/[^\p{L}\p{M}\p{N}\p{Pc} -]/gu, "").replace(/ /g, "-");
    const count = seen.get(base) ?? 0;
    seen.set(base, count + 1);
    return count === 0 ? base : `${base}-${count}`;
  };
}

/** Markdown を HTML にする。Markdown の中に直接書いた <img src> と <a href> も直す。見出しには GitHub と同じ id を付ける。 */
export function render(markdown) {
  const marked = new Marked({ gfm: true });
  const slug = slugger();
  marked.use({
    walkTokens(token) { if (token.type === "link" || token.type === "image") token.href = rewriteUrl(token.href); },
    renderer: {
      heading({ tokens, depth }) {
        // 見出しの文字だけ (リンク・強調・コードの印を除いたもの) で id を作る
        const plain = this.parser.parseInline(tokens, this.parser.textRenderer);
        return `<h${depth} id="${escapeHtml(slug(plain))}">${this.parser.parseInline(tokens)}</h${depth}>\n`;
      },
    },
  });
  return marked.parse(markdown).replace(/(<(?:img|a)\b[^>]*?\s(?:src|href)=")([^"]+)(")/g, (_, a, url, b) => a + rewriteUrl(url) + b);
}

function page({ title, slug }, body) {
  const nav = PAGES.map((p) => `<a href="${pageUrl(p.slug)}"${p.slug === slug ? ' aria-current="page"' : ""}>${escapeHtml(p.title)}</a>`).join("\n        ");
  const source = PAGES.find((p) => p.slug === slug).file;
  return `<!doctype html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(title)} - Meltype 使い方ガイド</title>
  <meta name="description" content="Meltype の${escapeHtml(title)}のガイド。キー操作・設定・困ったとき。">
  <meta name="theme-color" content="#3aaee8">
  <link rel="canonical" href="${pageUrl(slug)}">
  <link rel="icon" href="/favicon.svg" type="image/svg+xml">
  <link rel="apple-touch-icon" href="/apple-touch-icon.png">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Zen+Maru+Gothic:wght@500;700;900&display=swap">
  <link rel="stylesheet" href="/assets/css/site.css">
  <link rel="stylesheet" href="/assets/css/guide.css">
</head>
<body>
  <a class="skip" href="#main">本文へ移動</a>
  <header class="site-header">
    <div class="header-inner">
      <a class="brand" href="/" aria-label="Meltypeのトップ">
        <img src="/assets/img/logo-small.png" alt="" width="120" height="60">
      </a>
      <nav class="guide-nav" aria-label="使い方ガイド">
        ${nav}
      </nav>
    </div>
  </header>
  <main id="main" class="guide">
    <article class="guide-body">
${body}
    </article>
    <p class="guide-source">この内容は <a href="${repo}docs/${source}">docs/${source}</a> から作っています。間違いを見つけたら <a href="/report/">問題を報告する</a> から教えてください。</p>
  </main>
  <footer class="site-footer">
    <div class="wrap footer-inner">
      <p class="footer-copy">Copyright (C) 2026 雪代 / Yukishiro・GPL-3.0</p>
    </div>
  </footer>
</body>
</html>
`;
}

// node scripts/make-guide.mjs で実行したときだけ作る (テストから読み込んだときは作らない)。
// パスの書き方 (Windows のドライブ・空白・日本語) によらず、ファイルの場所で比べる
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  rmSync(out, { recursive: true, force: true });
  for (const p of PAGES) {
    const dir = p.slug ? join(out, p.slug) : out;
    mkdirSync(dir, { recursive: true });
    writeFileSync(join(dir, "index.html"), page(p, render(readFileSync(join(docs, p.file), "utf8"))));
  }
  cpSync(join(docs, "images"), join(out, "images"), { recursive: true });
  console.log(`使い方ガイドを作りました: ${PAGES.length} ページ`);
}
