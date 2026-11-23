// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// Meltype をウェブページの入力欄 (<textarea>・<input>) で使う。
//   import { attach } from "./meltype-web.js";
//   const ime = await attach(document.querySelector("textarea"));
// Core (src/Meltype.Web) はブラウザーの .NET で動き、判断はすべて Core がする。ここは出来事 (ImeEvent) を送り、
// 答え (ImeOutput) を入力欄に書くだけ (src/Meltype.Core/Ime/ImeProtocol.cs)。
// 変換中の文字は入力欄に入れず、キャレットの下に重ねた箱に出す (入力欄の値は確定した文字だけにする)。
// Mozc は別の WebAssembly (mozc/) で動かす。読めなければ漢字変換はせず、かなのまま確定する。

const IME_VERSION = 1;
const base = new URL(".", import.meta.url);

// 文字の無いキーの Windows の仮想キーコード (ImeKey.Vk)
// 入力モードを切り替えるキー (半角/全角・英数・かな)。ブラウザー版は日本語のモードだけで使うので、Core に送らない
// (Meltype は日本語のモードのままで英語も打てる。英数のモードは要らない)
const MODE_KEYS = new Set(["Hankaku", "Zenkaku", "ZenkakuHankaku", "Eisu", "KanaMode", "Alphanumeric", "Hiragana", "Katakana", "HiraganaKatakana", "Romaji", "Lang1", "Lang2"]);

const VK = {
  Backspace: 0x08, Tab: 0x09, Enter: 0x0d, Escape: 0x1b, " ": 0x20,
  PageUp: 0x21, PageDown: 0x22, End: 0x23, Home: 0x24,
  ArrowLeft: 0x25, ArrowUp: 0x26, ArrowRight: 0x27, ArrowDown: 0x28, Delete: 0x2e,
  F6: 0x75, F7: 0x76, F8: 0x77, F9: 0x78, F10: 0x79,
  Convert: 0x1c, NonConvert: 0x1d,
};

let corePromise;

/** Core と Mozc を読み込む (ページで 1 回)。 */
function loadCore() {
  return corePromise ??= (async () => {
    const mozc = await loadMozc();
    const listeners = new Set();
    const { dotnet } = await import(new URL("_framework/dotnet.js", base).href);
    const runtime = await dotnet.create();
    runtime.setModuleImports("meltype", {
      mozcRequest: (line) => mozc ? mozc(line) : null,
      // 変換の結果は Core の裏の処理で届く。キーの処理の途中かもしれないので、後で送る
      conversionReady: () => setTimeout(() => listeners.forEach((f) => f())),
    });
    const exports = (await runtime.getAssemblyExports("MeltypeWeb")).Meltype.Web.WebExports;
    exports.Init(mozc !== null);
    return { exports, listeners, mozc: mozc !== null };
  })();
}

/** Mozc の WebAssembly (mozc/mozc.js)。無ければ null。 */
async function loadMozc() {
  try {
    const { default: create } = await import(new URL("mozc/mozc.js", base).href);
    const module = await create({ locateFile: (file) => new URL(`mozc/${file}`, base).href });
    // 辞書を読むのに時間がかかるので、最初のキーの前に済ませる
    if (!module.ccall("meltype_mozc_init", "number", [], [])) throw new Error("Mozc を開けませんでした");
    const request = module.cwrap("meltype_mozc_request", "number", ["string"]);
    // NULL は失敗 (Core は数回続くと Mozc を使うのをやめる)
    return (line) => {
      const answer = request(line);
      return answer ? module.UTF8ToString(answer) : null;
    };
  } catch (error) {
    console.warn("Meltype: Mozc を読めなかったので、かなのまま確定します", error);
    return null;
  }
}

/**
 * 入力欄に Meltype を付ける。返す物の detach() で外す。
 * @param {HTMLTextAreaElement | HTMLInputElement} field
 */
export async function attach(field) {
  const core = await loadCore();
  const host = core.exports.Create();
  const box = createBox();
  let view = null;

  const send = (type, extra = {}) => {
    const event = {
      version: IME_VERSION, type, instance: "web",
      field: {
        before: field.value.slice(0, field.selectionStart),
        after: field.value.slice(field.selectionEnd),
        selection: field.selectionStart === field.selectionEnd ? null : field.value.slice(field.selectionStart, field.selectionEnd),
        composing: view !== null,
        app: location.hostname || null,
        title: document.title || null,
        traits: field.type === "password" ? ["Password"] : [],
      },
      ...extra,
    };
    const output = JSON.parse(core.exports.Handle(host, JSON.stringify(event)));
    apply(output.result);
    // 日本語のモードだけで使う (MODE_KEYS)。Core の設定のショートカットなどで英数を選んだら、日本語に戻す。
    // コードの行は日本語を選んだままでも英数になる (languageMode) ので、選んでいるモード (imeOpen) で見る
    if (!output.state.imeOpen && type !== "JapaneseModeSelected") send("JapaneseModeSelected");
    return output.result.consumed;
  };

  const apply = (result) => {
    for (const commit of result.commits) {
      let start = field.selectionStart;
      if (commit.removeBefore) start -= commit.removeBefore.length;
      field.setRangeText(commit.text, start, field.selectionEnd, "end");
      const caret = field.selectionStart - commit.moveCaretBack;
      field.setSelectionRange(caret, caret);
    }
    if (result.commits.length) field.dispatchEvent(new Event("input", { bubbles: true }));
    view = result.view;
    drawBox(box, field, view);
  };

  const onKeyDown = (e) => {
    // OS の IME で変換している間は、その IME に任せる
    if (e.isComposing || e.keyCode === 229) return;
    if (["Shift", "Control", "Alt", "Meta", "CapsLock"].includes(e.key)) return;
    if (MODE_KEYS.has(e.key)) {
      e.preventDefault();
      return;
    }
    const printable = [...e.key].length === 1;
    const key = {
      vk: VK[e.key] ?? (printable ? 0 : -1),
      ch: printable ? e.key.codePointAt(0) : 0,
      shift: e.shiftKey, ctrl: e.ctrlKey, alt: e.altKey, command: e.metaKey, repeat: e.repeat,
    };
    if (key.vk < 0) {
      // Core が知らないキー (F1 など) は、変換中の文字を確定してから通す
      if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
      return;
    }
    if (send("KeyPressed", { key })) {
      e.preventDefault();
    } else if (key.vk) {
      // Core が通したキー (Backspace・矢印など) で入力欄が変わったことを知らせる。ブラウザーが反映した後に送る
      setTimeout(() => send("CaretMoved", { caret: { moved: true, passedKey: key.vk, ctrl: key.ctrl } }));
    }
  };
  const onMoved = () => send("CaretMoved", { caret: { moved: true, passedKey: 0 } });
  const onFocus = () => send("CaretMoved", { caret: { moved: true, passedKey: 0, focusEntered: true } });
  const onBlur = () => {
    if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
    send("CaretMoved", { caret: { moved: true, passedKey: 0, focusLeft: true } });
  };
  const onReady = () => { if (document.activeElement === field) send("ConversionFinished"); };
  const onClickCandidate = (e) => {
    const item = e.target.closest("[data-index]");
    if (!item) return;
    e.preventDefault();
    send("CandidateClicked", { index: Number(item.dataset.index) });
    field.focus();
  };

  field.addEventListener("keydown", onKeyDown);
  field.addEventListener("mouseup", onMoved);
  field.addEventListener("focus", onFocus);
  field.addEventListener("blur", onBlur);
  box.addEventListener("mousedown", onClickCandidate);
  core.listeners.add(onReady);
  send("StateRequested");

  return {
    mozc: core.mozc,
    detach() {
      if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
      field.removeEventListener("keydown", onKeyDown);
      field.removeEventListener("mouseup", onMoved);
      field.removeEventListener("focus", onFocus);
      field.removeEventListener("blur", onBlur);
      core.listeners.delete(onReady);
      box.remove();
    },
  };
}

function createBox() {
  const box = document.createElement("div");
  box.className = "meltype-box";
  box.hidden = true;
  document.body.append(box);
  return box;
}

/** 変換中の文字 (文節ごと) と候補の一覧を、キャレットの下に出す。 */
function drawBox(box, field, view) {
  box.hidden = !view || (!view.text && !view.candidates);
  if (box.hidden) return;
  box.replaceChildren();
  const line = document.createElement("div");
  line.className = "meltype-text";
  const clauses = view.clauses?.length ? view.clauses : [view.text];
  clauses.forEach((text, i) => {
    const span = document.createElement("span");
    span.textContent = text;
    if (view.converting && i === view.selectedClause) span.className = "meltype-selected-clause";
    line.append(span);
  });
  box.append(line);
  if (view.typedKeys) box.append(Object.assign(document.createElement("div"), { className: "meltype-typed", textContent: view.typedKeys }));
  if (view.candidates) {
    const list = document.createElement("ol");
    list.className = "meltype-candidates";
    view.candidates.items.forEach((text, i) => {
      const item = document.createElement("li");
      item.dataset.index = String(i);
      item.textContent = (view.candidates.numbered ? `${i + 1}. ` : "") + text + (view.candidates.notes[i] ? `  ${view.candidates.notes[i]}` : "");
      if (i === view.candidates.selected) item.className = "meltype-selected";
      list.append(item);
    });
    box.append(list);
  }
  box.style.left = "0px";
  if (view.hint) box.append(Object.assign(document.createElement("div"), { className: "meltype-hint", textContent: view.hint }));
  const { left, top } = caretPosition(field);
  // 画面の右端からはみ出さないようにする
  box.style.left = `${Math.max(window.scrollX, Math.min(left, window.scrollX + document.documentElement.clientWidth - box.offsetWidth - 8))}px`;
  box.style.top = `${top}px`;
  // 選んでいる候補が見えるように一覧を送る (scrollIntoView はページまで動かすので使わない)
  const list = box.querySelector(".meltype-candidates");
  const selected = list?.querySelector(".meltype-selected");
  if (selected) list.scrollTop = Math.max(0, selected.offsetTop - list.offsetTop - list.clientHeight / 2 + selected.offsetHeight / 2);
}

/** キャレットの下の位置 (ページの座標)。入力欄と同じ見た目の写しを作って測る。 */
function caretPosition(field) {
  const style = getComputedStyle(field);
  const mirror = document.createElement("div");
  for (const name of ["font", "letterSpacing", "padding", "border", "boxSizing", "width", "lineHeight", "textIndent", "wordSpacing"]) {
    mirror.style[name] = style[name];
  }
  Object.assign(mirror.style, {
    position: "absolute", visibility: "hidden", top: "0", left: "0", overflow: "hidden",
    whiteSpace: field.tagName === "TEXTAREA" ? "pre-wrap" : "pre", overflowWrap: "break-word",
  });
  mirror.textContent = field.value.slice(0, field.selectionStart);
  const marker = document.createElement("span");
  marker.textContent = "​";
  mirror.append(marker);
  document.body.append(mirror);
  const rect = field.getBoundingClientRect();
  const left = rect.left + window.scrollX + marker.offsetLeft - field.scrollLeft;
  const top = rect.top + window.scrollY + marker.offsetTop + marker.offsetHeight - field.scrollTop;
  mirror.remove();
  return { left, top: Math.min(top, rect.bottom + window.scrollY) };
}
