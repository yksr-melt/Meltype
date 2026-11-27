// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

// /playground/: Web エンジン (meltype-engine.js) を入力欄に付ける。
// 指で操作する端末では、エンジンが画面のキーボードを出す (端末のキーボードの IME が打鍵を変換してしまうので)。

import { initHeader } from "./common.js";
import { loadEngine, sampleLabel, typeSample } from "./meltype-engine.js";

initHeader();

const field = document.querySelector("[data-field]");
const status = document.querySelector("[data-status]");

// 判定が分かれやすい入力を選んでいる (AGENTS.md の「曖昧なテキスト」)
const SAMPLES = [
  "kyouhagooglede kensakusita<Enter>",
  "nijigadeta<Space>",
  "bottoshuuseisita<Space>",
  "issueni kakikonda<Enter>",
  "dottonetto<Space>",
  "soretone<Space>",
];

const list = document.querySelector("[data-samples]");
for (const sample of SAMPLES) {
  const button = Object.assign(document.createElement("button"), { type: "button", textContent: sampleLabel(sample) });
  button.addEventListener("click", () => typeSample(field, sample));
  const item = document.createElement("li");
  item.append(button);
  list.append(item);
}
document.querySelector("[data-clear]").addEventListener("click", () => {
  field.value = "";
  field.focus();
});

try {
  const engine = await loadEngine(({ phase, loaded, total }) => {
    if (phase === "dictionary" && total) status.textContent = `漢字変換の辞書を読み込み中… ${(loaded / 1e6).toFixed(1)} / ${(total / 1e6).toFixed(1)} MB`;
  });
  engine.attach(field);
  field.disabled = false;
  status.textContent = engine.mozc ? "準備できました。" : "準備できました (漢字変換の辞書を読めなかったので、かなのまま確定します)。";
  status.classList.add("is-ready");
} catch (error) {
  status.textContent = "読み込めませんでした。ページを読み込み直してください。";
  status.classList.add("is-error");
  throw error;
}
