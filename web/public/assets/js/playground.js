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
function pressKey(key, repeat = false) {
  if (!field.dispatchEvent(new KeyboardEvent("keydown", { key, repeat, bubbles: true, cancelable: true }))) return;
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

// 指で操作する端末: 端末のキーボード (の IME) が打鍵を変換してしまうので出さず、画面のキーボードで打つ。
// ワークアラウンド: Android の Firefox はタッチだけの端末でも (any-pointer: fine) に当たることがあるので、ブラウザーの名前でも見る。
// iPadOS の Safari は Mac と名乗るので、タッチの点の数で見る
const touchOnly = (matchMedia("(pointer: coarse)").matches && !matchMedia("(any-pointer: fine)").matches)
  || /Android|iPhone|iPad|iPod/.test(navigator.userAgent)
  || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
if (touchOnly) {
  // ワークアラウンド: Android の Firefox は inputmode="none" でも端末のキーボードを出すことがあるので、readonly でも止める
  // (readonly でも、画面のキーボードと Meltype はスクリプトで書き込める)
  field.inputMode = "none";
  field.readOnly = true;
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
  // 押したら (指を離すのを待たずに) 打つ。長押しで続けて打つキーは、普通のキーボードと同じく少し待ってから繰り返す
  const REPEAT_KEYS = new Set(["Backspace", "ArrowLeft", "ArrowRight"]);
  const REPEAT_DELAY_MS = 400;
  const REPEAT_INTERVAL_MS = 60;
  let repeatTimer = 0;
  const stopRepeat = () => clearTimeout(repeatTimer);
  const press = (key) => {
    field.focus();
    // 振動できる端末だけ、押したことを短い振動で知らせる (iPhone の Safari には無い)
    navigator.vibrate?.(8);
    pressKey(key);
    drawCaret();
  };
  keys.addEventListener("pointerdown", (e) => {
    // 押しても入力欄からフォーカスを外さない (外すと変換中の文字が確定する)
    e.preventDefault();
    const button = e.target.closest("button[data-key]");
    if (!button) return;
    const { key } = button.dataset;
    button.classList.add("is-pressed");
    press(key);
    if (!REPEAT_KEYS.has(key)) return;
    const repeat = () => {
      pressKey(key, true);
      drawCaret();
      repeatTimer = setTimeout(repeat, REPEAT_INTERVAL_MS);
    };
    repeatTimer = setTimeout(repeat, REPEAT_DELAY_MS);
  });
  for (const type of ["pointerup", "pointercancel", "pointerleave"]) {
    keys.addEventListener(type, (e) => {
      stopRepeat();
      e.target.closest?.("button")?.classList.remove("is-pressed");
    }, true);
  }
  // 指でもマウスでもなく押した (スクリーンリーダー・キーボードの Enter)。detail が 0 になる
  keys.addEventListener("click", (e) => {
    const button = e.target.closest("button[data-key]");
    if (button && e.detail === 0) press(button.dataset.key);
  });

  // readonly の入力欄はカーソルを出さないブラウザーが多いので、キャレットの位置に描く
  const caret = document.createElement("span");
  caret.className = "vkeys-caret";
  caret.hidden = true;
  field.insertAdjacentElement("afterend", caret);
  const drawCaret = () => {
    caret.hidden = document.activeElement !== field || field.selectionStart !== field.selectionEnd;
    if (caret.hidden) return;
    const { left, top, height } = caretOffset(field);
    caret.style.left = `${left}px`;
    caret.style.top = `${top}px`;
    caret.style.height = `${height}px`;
    // 動かしたら点滅を最初からにする (動いている間は消えないように)
    caret.getAnimations().forEach((animation) => { animation.currentTime = 0; });
  };
  for (const type of ["focus", "blur", "input", "scroll"]) field.addEventListener(type, drawCaret);
  // Meltype が確定した文字を入れたとき・タップでキャレットを動かしたとき
  document.addEventListener("selectionchange", drawCaret);
  addEventListener("resize", drawCaret);

  keys.hidden = false;
  document.body.classList.add("has-vkeys");
  // 候補はキーボードのすぐ上に重ねて出すので、打っている入力欄を画面の上へ送る
  field.addEventListener("focus", () => field.scrollIntoView({ block: "start", behavior: "smooth" }));
}

/** 入力欄のキャレットの位置 (入力欄の親から見た座標) と行の高さ。入力欄と同じ見た目の写しを作って測る */
function caretOffset(textarea) {
  const style = getComputedStyle(textarea);
  const mirror = document.createElement("div");
  // 文字の幅に効くものはすべて写す。font (まとめた書き方) は、font-feature-settings などがあると空で返るので 1 つずつ写す
  for (const name of ["fontFamily", "fontSize", "fontWeight", "fontStyle", "fontStretch", "fontFeatureSettings", "fontKerning", "fontVariantLigatures", "letterSpacing", "wordSpacing", "textTransform", "textIndent", "tabSize",
    "wordBreak", "lineBreak", "padding", "border", "boxSizing", "width", "lineHeight"]) {
    mirror.style[name] = style[name];
  }
  Object.assign(mirror.style, { position: "absolute", visibility: "hidden", top: "0", left: "0", whiteSpace: "pre-wrap", overflowWrap: "break-word" });
  mirror.textContent = textarea.value.slice(0, textarea.selectionStart);
  const marker = document.createElement("span");
  marker.textContent = "\u200b";
  mirror.append(marker);
  document.body.append(mirror);
  const result = {
    left: textarea.offsetLeft + marker.offsetLeft - textarea.scrollLeft,
    top: textarea.offsetTop + marker.offsetTop - textarea.scrollTop,
    height: marker.offsetHeight,
  };
  mirror.remove();
  return result;
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
