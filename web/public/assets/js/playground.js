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

// 画面のキーボードの並び。値は KeyboardEvent.key、[表示, key, 幅] は文字の無いキー
const VKEY_ROWS = [
  [..."1234567890"],
  [..."qwertyuiop"],
  [..."asdfghjkl"],
  [..."zxcvbnm", ["⌫", "Backspace", 1.5]],
  [["←", "ArrowLeft"], ["→", "ArrowRight"], "-", ",", ".", ["space", " ", 4], ["⏎", "Enter", 1.5]],
];

/**
 * キーを 1 つ押す。合成したキーではブラウザーが何もしないので、Meltype が受け取らなかったキーはここで反映する。
 * (Meltype は通したキーを、反映の後に CaretMoved で受け取る)
 */
function pressKey(key) {
  if (!field.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true }))) return;
  const { selectionStart: start, selectionEnd: end, value } = field;
  if ([...key].length === 1) {
    field.setRangeText(key, start, end, "end");
  } else if (key === "Enter") {
    field.setRangeText("\n", start, end, "end");
  } else if (key === "Backspace") {
    if (start !== end) field.setRangeText("", start, end, "end");
    // 絵文字などのサロゲートペアを半分だけ消さない
    else if (start > 0) field.setRangeText("", start - ([...value.slice(0, start)].at(-1).length), start, "end");
  } else if (key === "ArrowLeft" || key === "ArrowRight") {
    const caret = key === "ArrowLeft" ? Math.max(0, start - 1) : Math.min(value.length, end + 1);
    field.setSelectionRange(caret, caret);
  }
}

async function typeSample(sample) {
  field.focus();
  const [, text, last] = sample.match(/^(.*?)(?:<(\w+)>)?$/);
  for (const key of [...text, ...(last ? [last === "Space" ? " " : last] : [])]) {
    pressKey(key);
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

// 指で操作する端末: 端末のキーボード (の IME) が打鍵を変換してしまうので出さず、画面のキーボードで打つ
if (matchMedia("(pointer: coarse)").matches && !matchMedia("(any-pointer: fine)").matches) {
  field.inputMode = "none";
  document.querySelector("[data-touch-note]").hidden = false;
  document.querySelector("[data-desktop-hint]").hidden = true;
  const keys = document.querySelector("[data-vkeys]");
  for (const row of VKEY_ROWS) {
    const line = document.createElement("div");
    line.className = "vkeys-row";
    for (const item of row) {
      const [label, key, width = 1] = Array.isArray(item) ? item : [item, item];
      const button = Object.assign(document.createElement("button"), { type: "button", textContent: label });
      button.dataset.key = key;
      button.style.flexGrow = String(width);
      if (Array.isArray(item)) button.classList.add("is-func");
      line.append(button);
    }
    keys.append(line);
  }
  // 押しても入力欄からフォーカスを外さない (外すと変換中の文字が確定する)
  keys.addEventListener("pointerdown", (e) => e.preventDefault());
  keys.addEventListener("click", (e) => {
    const button = e.target.closest("button[data-key]");
    if (!button) return;
    field.focus();
    pressKey(button.dataset.key);
  });
  keys.hidden = false;
  document.body.classList.add("has-vkeys");
  // 候補はキーボードのすぐ上に重ねて出すので、打っている入力欄を画面の上へ送る
  field.addEventListener("focus", () => field.scrollIntoView({ block: "start", behavior: "smooth" }));
}

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
