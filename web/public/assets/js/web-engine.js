// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

// /web-engine/: Web エンジンの紹介とデモ。
// デモのエンジン (web-engine/dist/) は npm run web-engine が public/web-engine/engine/ に写す (コミットしない)。
// 20 MB ほどあるので、開いただけでは読まず、「デモを読み込む」を押したときに読む。

import { initCopyButtons, initHeader } from "./common.js";

initHeader();
initCopyButtons();

// ---- インストールのタブ (矢印キーでも移れる) ----
const tabs = [...document.querySelectorAll("[data-tabs] [role=tab]")];
const select = (tab, focus) => {
  for (const other of tabs) {
    const on = other === tab;
    other.setAttribute("aria-selected", String(on));
    other.tabIndex = on ? 0 : -1;
    document.getElementById(other.getAttribute("aria-controls")).hidden = !on;
  }
  if (focus) tab.focus();
};
tabs.forEach((tab, index) => {
  tab.addEventListener("click", () => select(tab, false));
  tab.addEventListener("keydown", (event) => {
    const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
    if (!step) return;
    event.preventDefault();
    select(tabs[(index + step + tabs.length) % tabs.length], true);
  });
});

// ---- デモ ----
const field = document.getElementById("engine-text");
const start = document.querySelector("[data-demo-start]");
const status = document.querySelector("[data-status]");
const hint = document.querySelector("[data-demo-hint]");
const progress = document.querySelector("[data-progress]");
const bar = document.querySelector("[data-progress-bar]");

// 末尾の <Key> は最後に押すキー。日本語と英語の区切りが分かれやすい入力を選んでいる
const SAMPLES = [
  "kyouhagooglede kensakusita<Enter>",
  "nijigadeta<Space>",
  "bottoshuuseisita<Space>",
  "dottonetto<Space>",
];
const sampleButtons = SAMPLES.map((sample) => {
  const button = Object.assign(document.createElement("button"), { type: "button", textContent: sample.replace(/<(\w+)>$/, " + $1"), disabled: true });
  button.addEventListener("click", () => typeSample(sample));
  const item = document.createElement("li");
  item.append(button);
  document.querySelector("[data-samples]").append(item);
  return button;
});

// 例を打つ。合成したキーではブラウザーが文字を入れないので、Meltype が受け取らなかった文字はここで入れる
async function typeSample(sample) {
  field.focus();
  const [, text, last] = sample.match(/^(.*?)(?:<(\w+)>)?$/);
  for (const key of [...text, ...(last ? [last === "Space" ? " " : last] : [])]) {
    if (field.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true })) && [...key].length === 1) {
      field.setRangeText(key, field.selectionStart, field.selectionEnd, "end");
    }
    await new Promise((resolve) => setTimeout(resolve, 60));
  }
}

start.addEventListener("click", async () => {
  start.disabled = true;
  progress.hidden = false;
  status.textContent = "Meltype を読み込み中…";
  try {
    // .mjs を JavaScript として配らないサーバーもあるので、<script> 向けの meltype.js を読む (globalThis.Meltype になる)
    await import("/web-engine/engine/meltype.js");
    const engine = await globalThis.Meltype.load({
      baseUrl: "/web-engine/engine/",
      onProgress: ({ phase, loaded, total }) => {
        if (phase !== "dictionary" || !total) return;
        bar.style.width = `${Math.round(loaded / total * 100)}%`;
        status.textContent = `漢字変換の辞書を読み込み中… ${(loaded / 1e6).toFixed(1)} / ${(total / 1e6).toFixed(1)} MB`;
      },
    });
    engine.attach(field);
    field.disabled = false;
    for (const button of sampleButtons) button.disabled = false;
    progress.hidden = true;
    start.hidden = true;
    hint.textContent = "IME をオフ (半角英数) にして、ローマ字のまま打ってください。";
    status.textContent = engine.mozc ? "準備できました。" : "準備できました (漢字変換の辞書を読めなかったので、かなのまま確定します)。";
    status.classList.add("is-ready");
    field.focus();
  } catch (error) {
    start.disabled = false;
    progress.hidden = true;
    status.textContent = "読み込めませんでした。もう一度押してください。";
    status.classList.add("is-error");
    throw error;
  }
});
