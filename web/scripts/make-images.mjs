// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Konayukiw

// リンクを貼ったときの画像 (public/og.png) を scripts/og.html から作る。
// Windows 以外でも作れるように、PowerShell ではなくブラウザーで描いて撮る。
//   npm i --no-save playwright-core
//   node scripts/make-images.mjs            (Chrome / Edge / Chromium の場所は CHROMIUM_PATH で指定できる)
// logo-small.png は今までどおり make-images.ps1 で、apple-touch-icon.png とアプリのアイコンは make-icons.mjs で作る。

import { existsSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const { chromium } = await import("playwright-core");

const candidates = [
  process.env.CHROMIUM_PATH,
  "C:/Program Files/Google/Chrome/Application/chrome.exe",
  "C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe",
  "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
  "/usr/bin/chromium",
  "/usr/bin/google-chrome",
].filter(Boolean);
const executablePath = candidates.find((path) => existsSync(path));

const browser = await chromium.launch(executablePath ? { executablePath } : {});
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.goto(pathToFileURL(join(here, "og.html")).href, { waitUntil: "networkidle" });
// Google Fonts の丸ゴシックが読み込まれてから撮る
await page.evaluate(() => document.fonts.ready);
const loaded = await page.evaluate(() => document.fonts.check('900 38px "Zen Maru Gothic"', "半角"));
if (!loaded) throw new Error("Zen Maru Gothic を読み込めませんでした (ネットワークを確かめてください)");
const out = join(here, "..", "public", "og.png");
await page.screenshot({ path: out });
await browser.close();
console.log(`wrote ${out}`);
