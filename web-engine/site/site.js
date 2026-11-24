// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// 紹介ページの動き: インストールのタブ・コピー・デモ。
// デモのエンジン (engine/) は 20MB ほどあるので、開いただけでは読まず、「デモを読み込む」を押したときに読む。

// ---- インストールのタブ (左右の矢印キーでも移れる) ----
const tabs = [...document.querySelectorAll("[data-tabs] [role=tab]")];
const select = (tab) => {
  for (const other of tabs) {
    const selected = other === tab;
    other.setAttribute("aria-selected", String(selected));
    other.tabIndex = selected ? 0 : -1;
    document.getElementById(other.getAttribute("aria-controls")).hidden = !selected;
  }
  tab.focus();
};
for (const tab of tabs) {
  tab.addEventListener("click", () => select(tab));
  tab.addEventListener("keydown", (e) => {
    const step = { ArrowRight: 1, ArrowLeft: -1 }[e.key];
    if (step) select(tabs[(tabs.indexOf(tab) + step + tabs.length) % tabs.length]);
  });
}

// ---- コピー ----
for (const button of document.querySelectorAll("[data-copy]")) {
  button.addEventListener("click", async () => {
    const text = button.previousElementSibling.textContent;
    try {
      await navigator.clipboard.writeText(text);
      button.textContent = "コピーしました";
      button.classList.add("is-done");
      setTimeout(() => {
        button.textContent = "コピー";
        button.classList.remove("is-done");
      }, 1600);
    } catch {
      // クリップボードに書けないときは、コードを選んで自分でコピーしてもらう
      getSelection().selectAllChildren(button.previousElementSibling);
    }
  });
}

// ---- デモ ----
const field = document.getElementById("demo-text");
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
    const { load } = await import(new URL("engine/index.mjs", import.meta.url).href);
    const engine = await load({
      onProgress: ({ phase, loaded, total }) => {
        if (phase === "dictionary" && total) {
          bar.style.width = `${Math.round(loaded / total * 100)}%`;
          status.textContent = `漢字変換の辞書を読み込み中… ${(loaded / 1e6).toFixed(1)} / ${(total / 1e6).toFixed(1)} MB`;
        }
      },
    });
    engine.attach(field);
    field.disabled = false;
    for (const button of sampleButtons) button.disabled = false;
    progress.hidden = true;
    start.hidden = true;
    hint.textContent = "IME をオフ (半角英数) にして、ローマ字のまま打ってください。";
    status.textContent = engine.mozc ? `準備できました (Meltype ${engine.version})` : "準備できました (漢字変換の辞書を読めなかったので、かなのまま確定します)";
    status.classList.add("is-ready");
    field.focus();
  } catch (error) {
    start.disabled = false;
    progress.hidden = true;
    status.textContent = "読み込めませんでした。もう一度押してください。";
    console.error(error);
  }
});
