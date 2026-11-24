// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi

// /playground/ と /web-engine/ のデモが使う Web エンジン (web-engine/)。
// エンジン (web-engine/dist/) は npm run web-engine が public/web-engine/engine/ に写す (コミットしない)。

/** エンジンを読み込む。.mjs を JavaScript として配らないサーバーもあるので、<script> 向けの meltype.js を読む (globalThis.Meltype になる) */
export async function loadEngine(onProgress) {
  await import("/web-engine/engine/meltype.js");
  return globalThis.Meltype.load({ baseUrl: "/web-engine/engine/", onProgress });
}

/** 例のボタンの文字。末尾の <Key> は最後に押すキー */
export const sampleLabel = (sample) => sample.replace(/<(\w+)>$/, " + $1");

/** 例を打つ。合成したキーではブラウザーが文字を入れないので、Meltype が受け取らなかった文字はここで入れる */
export async function typeSample(field, sample) {
  field.focus();
  const [, text, last] = sample.match(/^(.*?)(?:<(\w+)>)?$/);
  for (const key of [...text, ...(last ? [last === "Space" ? " " : last] : [])]) {
    if (field.dispatchEvent(new KeyboardEvent("keydown", { key, bubbles: true, cancelable: true })) && [...key].length === 1) {
      field.setRangeText(key, field.selectionStart, field.selectionEnd, "end");
    }
    await new Promise((resolve) => setTimeout(resolve, 60));
  }
}
