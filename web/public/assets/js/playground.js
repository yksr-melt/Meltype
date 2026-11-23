// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

// /playground/: ブラウザー版の Meltype (web-ime/) を入力欄に付ける。
// 本体 (meltype-web.js・Core・Mozc) は npm run playground が public/playground/engine/ に作る (コミットしない)。

import { initHeader } from "./common.js";

initHeader();

const field = document.querySelector("[data-field]");
const status = document.querySelector("[data-status]");

// 末尾の <Key> は最後に押すキー。判定が分かれやすい入力を選んでいる (AGENTS.md の「曖昧なテキスト」)
const SAMPLES = [
  "kyouhagooglede kensakusita<Enter>",
  "nijigadeta<Space>",
  "bottoshuuseisita<Space>",
  "issueni kakikonda<Enter>",
  "dottonetto<Space>",
  "soretone<Space>",
];

if (matchMedia("(pointer: coarse)").matches && !matchMedia("(pointer: fine)").matches) {
  document.querySelector("[data-mobile-note]").hidden = false;
}

// 例を打つ。合成したキーではブラウザーが文字を入れないので、Meltype が受け取らなかった文字はここで入れる
async function typeSample(sample) {
  field.focus();
  const [, text, last] = sample.match(/^(.*?)(?:<(\w+)>)?$/);
  for (const key of [...text, ...(last ? [last === "Space" ? " " : last] : [])]) {
    if (field.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true })) && [...key].length === 1) {
      field.setRangeText(key, field.selectionStart, field.selectionEnd, "end");
    }
    await new Promise((resolve) => setTimeout(resolve, 40));
  }
}

const list = document.querySelector("[data-samples]");
for (const sample of SAMPLES) {
  const button = Object.assign(document.createElement("button"), { type: "button", textContent: sample.replace(/<(\w+)>$/, " + $1") });
  button.addEventListener("click", () => typeSample(sample));
  const item = document.createElement("li");
  item.append(button);
  list.append(item);
}
document.querySelector("[data-clear]").addEventListener("click", () => {
  field.value = "";
  field.focus();
});

try {
  const { attach } = await import("/playground/engine/meltype-web.js");
  const ime = await attach(field);
  field.disabled = false;
  status.textContent = ime.mozc ? "準備できました。" : "準備できました (漢字変換の辞書を読めなかったので、かなのまま確定します)。";
  status.classList.add("is-ready");
} catch (error) {
  status.textContent = "読み込めませんでした。ページを読み込み直してください。";
  status.classList.add("is-error");
  throw error;
}
