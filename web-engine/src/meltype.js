// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// Meltype Web Engine: ウェブページの入力欄で Meltype を使う。
//   <script src="https://…/meltype.js"></script>
//   <script>Meltype.attach(document.querySelector("textarea"));</script>
// ES モジュールでは index.mjs から読む (README.md)。
//
// 判断はすべて Core (src/Meltype.Web をブラウザーの .NET で動かしたもの) がする。ここは出来事 (ImeEvent) を送り、
// 答え (ImeOutput) を入力欄に書くだけ (src/Meltype.Core/Ime/ImeProtocol.cs)。漢字変換は Mozc の WebAssembly (mozc/)。
// 変換中の文字は入力欄に入れず、キャレットの下に重ねた箱に出す (入力欄の値は確定した文字だけにする)。
// 箱・画面のキーボード・カーソルは Shadow DOM の中に描く (どのサイトの CSS とも混ざらないように。拡張機能 web-extension/ も使う)。
// 入力モードは日本語だけ。Meltype は日本語のモードのままで英語も打てるので、英数のモードは出さない。
// <script> でも読めるように、ES モジュールの構文 (import・export・import.meta) は使わない。

(() => {
  "use strict";

  const IME_VERSION = 1;

  // 文字の無いキーの Windows の仮想キーコード (ImeKey.Vk)
  const VK = {
    Backspace: 0x08, Tab: 0x09, Enter: 0x0d, Escape: 0x1b, " ": 0x20,
    PageUp: 0x21, PageDown: 0x22, End: 0x23, Home: 0x24,
    ArrowLeft: 0x25, ArrowUp: 0x26, ArrowRight: 0x27, ArrowDown: 0x28, Delete: 0x2e,
    F6: 0x75, F7: 0x76, F8: 0x77, F9: 0x78, F10: 0x79,
    Convert: 0x1c, NonConvert: 0x1d,
  };

  // 入力モードを切り替えるキー (半角/全角・英数・かな)。日本語のモードだけで使うので、Core に送らない
  const MODE_KEYS = new Set(["Hankaku", "Zenkaku", "ZenkakuHankaku", "Eisu", "KanaMode", "Alphanumeric", "Hiragana", "Katakana", "HiraganaKatakana", "Romaji", "Lang1", "Lang2"]);
  const MODIFIER_KEYS = new Set(["Shift", "Control", "Alt", "Meta", "CapsLock"]);

  // 画面のキーボードの並び。値は KeyboardEvent.key、[表示, key, 幅] は文字の無いキー
  const VKEY_ROWS = [
    [..."1234567890"],
    [..."qwertyuiop"],
    [..."asdfghjkl"],
    [..."zxcvbnm", ["⌫", "Backspace", 1.5]],
    [["←", "ArrowLeft"], ["→", "ArrowRight"], "-", ",", ".", ["space", " ", 4], ["⏎", "Enter", 1.5]],
  ];
  // 長押しで繰り返すキーと、その間合い (ふつうのキーボードに合わせた)
  const REPEAT_KEYS = new Set(["Backspace", "ArrowLeft", "ArrowRight"]);
  const REPEAT_DELAY_MS = 400;
  const REPEAT_INTERVAL_MS = 60;

  // 学習データを IndexedDB に写すまでの間 (Core はファイルを少し遅れて書くので、確定のたびには写さない)
  const SAVE_DELAY_MS = 3000;
  const DB_NAME = "meltype-web-engine";
  const DB_STORE = "data";

  // <script> で読んだときは、そのファイルの場所から本体 (_framework/・mozc/) を探す
  let baseUrl = document.currentScript?.src ? new URL(".", document.currentScript.src) : null;
  let enginePromise = null;
  // 箱・キーボード・カーソルを入れる Shadow DOM (load で作る)
  let ui = null;

  /**
   * 本体 (Core と Mozc) を読み込む。2 回目からは同じものを返す。
   * options.baseUrl: 本体を置いた場所。options.persist: 学習データを残すか (既定は true)。
   * options.storage: 学習データの置き場 ({ get(): Promise<string | null>, set(json), clear() })。既定はそのページの IndexedDB。
   * options.onProgress({ phase, loaded, total }): 読み込みの進み具合 (phase は "core"・"dictionary"・"ready")。
   */
  function load(options = {}) {
    return enginePromise ??= start(options).catch((error) => {
      enginePromise = null;
      throw error;
    });
  }

  async function start(options) {
    if (options.baseUrl) baseUrl = new URL(options.baseUrl, location.href);
    if (!baseUrl) throw new Error("Meltype: 本体の場所が分かりません。load({ baseUrl }) で指定してください");
    const progress = options.onProgress ?? (() => {});
    const persist = options.persist !== false;
    const css = await fetch(new URL("meltype.css", baseUrl)).then((response) => {
      if (!response.ok) throw new Error(`meltype.css: ${response.status}`);
      return response.text();
    });

    progress({ phase: "core" });
    const { dotnet } = await import(new URL("_framework/dotnet.js", baseUrl).href);
    const runtime = await dotnet.create();
    const listeners = new Set();
    let mozc = null;
    runtime.setModuleImports("meltype", {
      mozcRequest: (line) => mozc ? mozc(line) : null,
      // 変換の結果は Core の裏の処理で届く。キーの処理の途中かもしれないので、後で送る
      conversionReady: () => setTimeout(() => listeners.forEach((f) => f())),
    });
    const core = (await runtime.getAssemblyExports("MeltypeWeb")).Meltype.Web.WebExports;

    mozc = await loadMozc((loaded, total) => progress({ phase: "dictionary", loaded, total }));
    const store = persist ? options.storage ?? await openStore() : null;
    const saved = store ? await store.get() : null;
    if (saved) core.ImportData(saved);
    core.Init(mozc !== null);
    ui = createUi(css);

    let saveTimer = 0;
    const save = () => {
      clearTimeout(saveTimer);
      saveTimer = 0;
      store?.set(core.ExportData())?.catch?.((error) => console.warn("Meltype: 学習データを残せませんでした", error));
    };
    const scheduleSave = () => {
      if (store && !saveTimer) saveTimer = setTimeout(save, SAVE_DELAY_MS);
    };
    // ページを閉じる・隠すときは待たずに写す
    addEventListener("pagehide", () => saveTimer && save());
    document.addEventListener("visibilitychange", () => document.hidden && saveTimer && save());

    progress({ phase: "ready" });
    return {
      version: core.Version(),
      mozc: mozc !== null,
      attach: (element, attachOptions) => attachTo({ core, listeners, scheduleSave }, element, attachOptions),
      /** 覚えたことを消す (次に読み込んだときから、覚える前に戻る)。 */
      async clearLearning() {
        clearTimeout(saveTimer);
        saveTimer = 0;
        await store?.clear();
      },
    };
  }

  /** 入力欄に付ける。本体をまだ読んでいなければ読む。返す物の detach() で外す。 */
  async function attach(element, options = {}) {
    const engine = await load(options);
    return engine.attach(element, options);
  }

  /** Mozc の WebAssembly (mozc/mozc.js)。読めなければ null で、かなのまま確定する。 */
  async function loadMozc(onProgress) {
    try {
      const { default: create } = await import(new URL("mozc/mozc.js", baseUrl).href);
      const wasm = await fetchWithProgress(new URL("mozc/mozc.wasm", baseUrl), onProgress);
      const module = await create({
        locateFile: (file) => new URL(`mozc/${file}`, baseUrl).href,
        // 進み具合を出すために自分で取ったものを使う
        instantiateWasm: (imports, receive) => {
          WebAssembly.instantiate(wasm, imports).then(({ instance, module }) => receive(instance, module));
          return {};
        },
      });
      // 辞書を読むのに時間がかかるので、最初のキーの前に済ませる
      if (!module.ccall("meltype_mozc_init", "number", [], [])) throw new Error("Mozc を開けませんでした");
      const request = module.cwrap("meltype_mozc_request", "number", ["string"]);
      // NULL は失敗 (Core は数回続くと Mozc を使うのをやめる)
      return (line) => {
        const answer = request(line);
        return answer ? module.UTF8ToString(answer) : null;
      };
    } catch (error) {
      console.warn("Meltype: Mozc を読めなかったので、漢字変換はせず、かなのまま確定します", error);
      return null;
    }
  }

  async function fetchWithProgress(url, onProgress) {
    const response = await fetch(url);
    if (!response.ok) throw new Error(`${url}: ${response.status}`);
    const total = Number(response.headers.get("Content-Length")) || 0;
    if (!response.body) return response.arrayBuffer();
    const reader = response.body.getReader();
    const chunks = [];
    let loaded = 0;
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      chunks.push(value);
      loaded += value.length;
      onProgress(loaded, total);
    }
    const bytes = new Uint8Array(loaded);
    chunks.reduce((offset, chunk) => (bytes.set(chunk, offset), offset + chunk.length), 0);
    return bytes;
  }

  /** 学習データの置き場 (IndexedDB)。開けなければ (プライベートブラウズなど) null で、残さずに動く。 */
  async function openStore() {
    try {
      const db = await new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, 1);
        request.onupgradeneeded = () => request.result.createObjectStore(DB_STORE);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
      const run = (mode, action) => new Promise((resolve, reject) => {
        const request = action(db.transaction(DB_STORE, mode).objectStore(DB_STORE));
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
      return {
        get: () => run("readonly", (s) => s.get("files")),
        set: (json) => run("readwrite", (s) => s.put(json, "files")),
        clear: () => run("readwrite", (s) => s.delete("files")),
      };
    } catch (error) {
      console.warn("Meltype: 学習データの置き場を開けなかったので、ページを閉じると忘れます", error);
      return null;
    }
  }

  // ---- 入力欄 ----

  /**
   * options.keyboard: 画面のキーボードを出すか。"auto" (既定) は指で操作する端末だけ、"always"・"never"。
   * options.onCommit(text): 確定した文字を入れたとき。
   */
  function attachTo(engine, element, options = {}) {
    const field = element.isContentEditable ? editableField(element) : textField(element);
    const host = engine.core.Create();
    const box = createBox();
    let view = null;
    let focused = false;

    const send = (type, extra = {}) => {
      const event = {
        version: IME_VERSION, type, instance: "web",
        field: { ...field.context(), composing: view !== null, app: location.hostname || null, title: document.title || null },
        ...extra,
      };
      const output = JSON.parse(engine.core.Handle(host, JSON.stringify(event)));
      apply(output.result);
      // 日本語のモードだけで使う (MODE_KEYS)。Core の設定のショートカットなどで英数を選んだら、日本語に戻す。
      // コードの行は日本語を選んだままでも英数になる (languageMode) ので、選んでいるモード (imeOpen) で見る
      if (!output.state.imeOpen && type !== "JapaneseModeSelected") send("JapaneseModeSelected");
      return output.result.consumed;
    };

    const apply = (result) => {
      for (const commit of result.commits) {
        field.insert(commit.text, commit.removeBefore ?? "", commit.moveCaretBack);
        options.onCommit?.(commit.text);
      }
      if (result.commits.length) engine.scheduleSave();
      view = result.view;
      drawBox(box, view, () => field.caretRect());
      keyboard?.drawCaret();
    };

    const onKeyDown = (e) => {
      // OS の IME で変換している間は、その IME に任せる
      if (e.isComposing || e.keyCode === 229) return;
      if (MODIFIER_KEYS.has(e.key)) return;
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
    const onPointerUp = () => send("CaretMoved", { caret: { moved: true, passedKey: 0 } });
    const onFocus = () => {
      focused = true;
      keyboard?.show();
      send("CaretMoved", { caret: { moved: true, passedKey: 0, focusEntered: true } });
    };
    const onBlur = () => {
      focused = false;
      keyboard?.hide();
      if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
      send("CaretMoved", { caret: { moved: true, passedKey: 0, focusLeft: true } });
    };
    const onReady = () => focused && send("ConversionFinished");
    // 候補を押しても入力欄からフォーカスを外さない (外すと変換中の文字が確定する)
    const onPointerDownBox = (e) => e.preventDefault();
    const onClickCandidate = (e) => {
      const item = e.target.closest("[data-index]");
      if (!item) return;
      send("CandidateClicked", { index: Number(item.dataset.index) });
      element.focus();
    };

    const useKeyboard = options.keyboard === "always" || (options.keyboard !== "never" && touchOnly());
    const keyboard = useKeyboard ? onScreenKeyboard(element, field) : null;

    element.addEventListener("keydown", onKeyDown);
    element.addEventListener("pointerup", onPointerUp);
    element.addEventListener("focus", onFocus);
    element.addEventListener("blur", onBlur);
    box.addEventListener("pointerdown", onPointerDownBox);
    box.addEventListener("click", onClickCandidate);
    engine.listeners.add(onReady);
    send("StateRequested");
    // フォーカスがあってから付けた (本体を読んでいる間に入力欄を選んだ・拡張機能が付けた) ときは、ここで入ったことにする
    if (element.getRootNode().activeElement === element) onFocus();

    return {
      detach() {
        if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
        element.removeEventListener("keydown", onKeyDown);
        element.removeEventListener("pointerup", onPointerUp);
        element.removeEventListener("focus", onFocus);
        element.removeEventListener("blur", onBlur);
        engine.listeners.delete(onReady);
        keyboard?.remove();
        box.remove();
      },
    };
  }

  /**
   * 指で操作する端末か (画面のキーボードを出す)。
   * ワークアラウンド: Android の Firefox はタッチだけの端末でも (any-pointer: fine) に当たることがあるので、ブラウザーの名前でも見る。
   * iPadOS の Safari は Mac と名乗るので、タッチの点の数で見る
   */
  function touchOnly() {
    return (matchMedia("(pointer: coarse)").matches && !matchMedia("(any-pointer: fine)").matches)
      || /Android|iPhone|iPad|iPod/.test(navigator.userAgent)
      || (/Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1);
  }

  /** <textarea>・<input> */
  function textField(element) {
    return {
      context: () => ({
        before: element.value.slice(0, element.selectionStart),
        after: element.value.slice(element.selectionEnd),
        selection: element.selectionStart === element.selectionEnd ? null : element.value.slice(element.selectionStart, element.selectionEnd),
        traits: element.type === "password" ? ["Password"] : [],
      }),
      insert(text, removeBefore, caretBack) {
        element.setRangeText(text, element.selectionStart - removeBefore.length, element.selectionEnd, "end");
        const caret = element.selectionStart - caretBack;
        element.setSelectionRange(caret, caret);
        element.dispatchEvent(new Event("input", { bubbles: true }));
      },
      /** 合成したキーでブラウザーが何もしないので、画面のキーボードのキーを反映する */
      passKey(key) {
        const { selectionStart: start, selectionEnd: end, value } = element;
        if ([...key].length === 1 || key === "Enter") {
          element.setRangeText(key === "Enter" ? "\n" : key, start, end, "end");
        } else if (key === "Backspace") {
          if (start !== end) element.setRangeText("", start, end, "end");
          // 絵文字などのサロゲートペアを半分だけ消さない
          else if (start > 0) element.setRangeText("", start - [...value.slice(0, start)].at(-1).length, start, "end");
        } else if (key === "ArrowLeft" || key === "ArrowRight") {
          const caret = key === "ArrowLeft" ? Math.max(0, start - 1) : Math.min(value.length, end + 1);
          element.setSelectionRange(caret, caret);
          return;
        }
        element.dispatchEvent(new Event("input", { bubbles: true }));
      },
      collapsed: () => element.selectionStart === element.selectionEnd,
      caretRect: () => textCaretRect(element),
      // ワークアラウンド: Android の Firefox は inputmode="none" でも端末のキーボードを出すことがあるので、readonly でも止める
      // (readonly でも、スクリプトからは書き込める)。readonly の欄はカーソルを出さないブラウザーが多いので、自分で描く。
      // 返す物の restore() で、外すときに元に戻す
      suppressSystemKeyboard() {
        const { inputMode, readOnly } = element;
        element.inputMode = "none";
        element.readOnly = true;
        return { drawsCaret: true, restore: () => Object.assign(element, { inputMode, readOnly }) };
      },
    };
  }

  /** contenteditable。書き換えは execCommand で行い、元に戻す (Ctrl+Z) の履歴に残す */
  function editableField(element) {
    const range = () => {
      const selection = getSelection();
      return selection.rangeCount && element.contains(selection.anchorNode) ? selection.getRangeAt(0) : null;
    };
    const textBefore = (r) => {
      const before = document.createRange();
      before.selectNodeContents(element);
      before.setEnd(r.startContainer, r.startOffset);
      return before.toString();
    };
    const moveCaret = (direction, count, alter = "move") => {
      for (let i = 0; i < count; i++) getSelection().modify(alter, direction, "character");
    };
    return {
      context() {
        const r = range();
        if (!r) return { before: null, after: null, selection: null, traits: [] };
        const after = document.createRange();
        after.selectNodeContents(element);
        after.setStart(r.endContainer, r.endOffset);
        return { before: textBefore(r), after: after.toString(), selection: r.collapsed ? null : r.toString(), traits: [] };
      },
      insert(text, removeBefore, caretBack) {
        // 消す文字は Core がキャレットの前と照合済み。文字 (コードポイント) の数だけ選んで置き換える
        if (removeBefore) moveCaret("backward", [...removeBefore].length, "extend");
        document.execCommand("insertText", false, text);
        moveCaret("backward", caretBack);
      },
      passKey(key) {
        if ([...key].length === 1) document.execCommand("insertText", false, key);
        else if (key === "Enter") document.execCommand("insertLineBreak");
        else if (key === "Backspace") document.execCommand("delete");
        else if (key === "ArrowLeft") moveCaret("backward", 1);
        else if (key === "ArrowRight") moveCaret("forward", 1);
      },
      collapsed: () => range()?.collapsed ?? true,
      caretRect() {
        const r = range();
        if (!r) return element.getBoundingClientRect();
        const caret = r.cloneRange();
        caret.collapse(true);
        const rect = caret.getClientRects()[0];
        if (rect) return rect;
        // 空の行では大きさが無いので、一時的に文字を置いて測る
        const marker = document.createTextNode("​");
        caret.insertNode(marker);
        const measured = marker.parentNode ? (() => { const m = document.createRange(); m.selectNodeContents(marker); return m.getBoundingClientRect(); })() : element.getBoundingClientRect();
        marker.remove();
        return measured;
      },
      // contenteditable は readonly にできない。inputmode="none" が効かないブラウザー (Android の Firefox) では端末のキーボードも出る
      suppressSystemKeyboard() {
        const { inputMode } = element;
        element.inputMode = "none";
        return { drawsCaret: false, restore: () => { element.inputMode = inputMode; } };
      },
    };
  }

  /**
   * <textarea>・<input> のキャレットの位置 (画面の座標)。入力欄と同じ見た目の写しを作って測る。
   * 写しはページの中に置くので、ページの CSS (div {…} など) が効かないように、独自のタグにして値を !important で決める
   */
  function textCaretRect(element) {
    const style = getComputedStyle(element);
    const mirror = document.createElement("meltype-mirror");
    const set = (target, name, value) => target.style.setProperty(name, value, "important");
    // 文字の幅に効くものはすべて写す。font (まとめた書き方) は、font-feature-settings などがあると空で返るので 1 つずつ写す
    for (const name of ["font-family", "font-size", "font-weight", "font-style", "font-stretch", "font-feature-settings", "font-kerning", "letter-spacing", "word-spacing",
      "text-transform", "text-indent", "tab-size", "word-break", "line-break", "padding-top", "padding-right", "padding-bottom", "padding-left",
      "border-top-width", "border-right-width", "border-bottom-width", "border-left-width", "box-sizing", "width", "line-height"]) {
      set(mirror, name, style.getPropertyValue(name));
    }
    for (const [name, value] of [["display", "block"], ["position", "absolute"], ["visibility", "hidden"], ["top", "0"], ["left", "0"], ["margin", "0"],
      ["border-style", "solid"], ["overflow", "hidden"], ["white-space", element.tagName === "TEXTAREA" ? "pre-wrap" : "pre"], ["overflow-wrap", "break-word"]]) {
      set(mirror, name, value);
    }
    mirror.textContent = element.value.slice(0, element.selectionStart);
    const marker = document.createElement("meltype-mirror");
    for (const [name, value] of [["display", "inline"], ["margin", "0"], ["padding", "0"], ["border", "0"], ["font", "inherit"], ["line-height", "inherit"]]) set(marker, name, value);
    marker.textContent = "\u200b";
    mirror.append(marker);
    document.documentElement.append(mirror);
    const rect = element.getBoundingClientRect();
    const mirrorRect = mirror.getBoundingClientRect();
    const markerRect = marker.getBoundingClientRect();
    const left = rect.left + markerRect.left - mirrorRect.left - element.scrollLeft;
    const top = rect.top + markerRect.top - mirrorRect.top - element.scrollTop;
    const height = markerRect.height;
    mirror.remove();
    return { left, top, bottom: Math.min(top + height, rect.bottom), height };
  }

  // ---- 変換中の文字と候補 ----

  /**
   * 箱・キーボード・カーソルを入れる Shadow DOM。ページの左上を原点にして置く (中の座標はページの座標)。
   * 見た目は meltype.css を Shadow DOM にだけ当てる。色と字は、ページの --meltype-* の変数を受け継いで変えられる。
   */
  function createUi(css) {
    const host = document.createElement("meltype-ui");
    Object.assign(host.style, { position: "absolute", top: "0", left: "0", width: "0", height: "0", zIndex: "2147483647" });
    const root = host.attachShadow({ mode: "open" });
    // style 要素はページの CSP (style-src) で止められることがあるので、止められない CSSStyleSheet で当てる
    const sheet = new CSSStyleSheet();
    sheet.replaceSync(css);
    root.adoptedStyleSheets = [sheet];
    let paddingBefore = null;
    return {
      root,
      /** 外されていたら (ページの作り直しなどで) 付け直す */
      ensure() {
        // <body> の中に置く (ページが <body> に決めた --meltype-* の変数を受け継ぐように)
        if (!host.isConnected) (document.body ?? document.documentElement).append(host);
      },
      get keyboardOpen() {
        return host.classList.contains("vkeys-open");
      },
      /** 画面のキーボードを出している間は、キーボードの高さだけページの下を空ける */
      setKeyboardOpen(open) {
        host.classList.toggle("vkeys-open", open);
        if (open && paddingBefore === null) {
          paddingBefore = document.body.style.paddingBottom;
          document.body.style.paddingBottom = `${root.querySelector(".meltype-vkeys")?.offsetHeight ?? 0}px`;
        } else if (!open && paddingBefore !== null) {
          document.body.style.paddingBottom = paddingBefore;
          paddingBefore = null;
        }
      },
    };
  }

  function createBox() {
    const box = document.createElement("div");
    box.className = "meltype-box";
    box.hidden = true;
    ui.ensure();
    ui.root.append(box);
    return box;
  }

  /** 変換中の文字 (文節ごと) と候補の一覧を、キャレットの下に出す。 */
  function drawBox(box, view, caretRect) {
    box.hidden = !view || (!view.text && !view.candidates);
    if (box.hidden) return;
    ui.ensure();
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
    if (view.hint) box.append(Object.assign(document.createElement("div"), { className: "meltype-hint", textContent: view.hint }));
    // 画面のキーボードがあるときは、キーボードのすぐ上に固定して出す (meltype.css の :host(.vkeys-open) が位置を決める)
    box.style.left = box.style.top = "";
    if (!ui.keyboardOpen) {
      const caret = caretRect();
      // 画面の右端からはみ出さないようにする
      box.style.left = `${Math.max(scrollX, Math.min(caret.left + scrollX, scrollX + document.documentElement.clientWidth - box.offsetWidth - 8))}px`;
      box.style.top = `${caret.bottom + scrollY}px`;
    }
    // 選んでいる候補が見えるように一覧を送る (scrollIntoView はページまで動かすので使わない)
    const list = box.querySelector(".meltype-candidates");
    const selected = list?.querySelector(".meltype-selected");
    if (selected) list.scrollTop = Math.max(0, selected.offsetTop - list.offsetTop - list.clientHeight / 2 + selected.offsetHeight / 2);
  }

  // ---- 画面のキーボード ----

  // 画面のキーボードとカーソルはページに 1 つだけ作り、フォーカスのある入力欄 (active) に打つ
  let vkeys = null;
  let active = null;

  function sharedKeyboard() {
    if (vkeys) return vkeys;
    const keys = document.createElement("div");
    keys.className = "meltype-vkeys";
    keys.hidden = true;
    keys.setAttribute("role", "group");
    keys.setAttribute("aria-label", "キーボード");
    for (const row of VKEY_ROWS) {
      const line = document.createElement("div");
      line.className = "meltype-vkeys-row";
      for (const item of row) {
        const [label, key, width = 1] = Array.isArray(item) ? item : [item, item];
        const button = Object.assign(document.createElement("button"), { type: "button", textContent: label });
        button.dataset.key = key;
        button.style.flexGrow = String(width);
        if (Array.isArray(item)) button.classList.add("meltype-func");
        line.append(button);
      }
      keys.append(line);
    }
    const caret = document.createElement("span");
    caret.className = "meltype-caret";
    caret.hidden = true;
    ui.ensure();
    ui.root.append(keys, caret);

    const drawCaret = () => {
      caret.hidden = !active?.drawsCaret || !active.field.collapsed();
      if (caret.hidden) return;
      const rect = active.field.caretRect();
      Object.assign(caret.style, { left: `${rect.left + scrollX}px`, top: `${rect.top + scrollY}px`, height: `${rect.height}px` });
      // 動かしたら点滅を最初からにする (動いている間は消えないように)
      caret.getAnimations().forEach((animation) => { animation.currentTime = 0; });
    };
    const pressKey = (key, repeat = false) => {
      if (!active) return;
      const { element, field } = active;
      if (element.dispatchEvent(new KeyboardEvent("keydown", { key, repeat, bubbles: true, cancelable: true }))) field.passKey(key);
      drawCaret();
    };
    let repeatTimer = 0;
    const press = (key) => {
      active?.element.focus();
      // 振動できる端末だけ、押したことを短い振動で知らせる (iPhone の Safari には無い)
      navigator.vibrate?.(8);
      pressKey(key);
    };
    keys.addEventListener("pointerdown", (e) => {
      // 押しても入力欄からフォーカスを外さない (外すと変換中の文字が確定する)
      e.preventDefault();
      const button = e.target.closest("button[data-key]");
      if (!button) return;
      const { key } = button.dataset;
      button.classList.add("meltype-pressed");
      press(key);
      if (!REPEAT_KEYS.has(key)) return;
      const repeat = () => {
        pressKey(key, true);
        repeatTimer = setTimeout(repeat, REPEAT_INTERVAL_MS);
      };
      repeatTimer = setTimeout(repeat, REPEAT_DELAY_MS);
    });
    for (const type of ["pointerup", "pointercancel", "pointerleave"]) {
      keys.addEventListener(type, (e) => {
        clearTimeout(repeatTimer);
        e.target.closest?.("button")?.classList.remove("meltype-pressed");
      }, true);
    }
    // 指でもマウスでもなく押した (スクリーンリーダー・キーボードの Enter)。detail が 0 になる
    keys.addEventListener("click", (e) => {
      const button = e.target.closest("button[data-key]");
      if (button && e.detail === 0) press(button.dataset.key);
    });
    document.addEventListener("selectionchange", drawCaret);
    addEventListener("resize", drawCaret);

    vkeys = {
      drawCaret,
      show(target) {
        active = target;
        keys.hidden = false;
        ui.ensure();
        ui.setKeyboardOpen(true);
        drawCaret();
      },
      hide(target) {
        if (active !== target) return;
        clearTimeout(repeatTimer);
        active = null;
        keys.hidden = true;
        ui.setKeyboardOpen(false);
        drawCaret();
      },
    };
    return vkeys;
  }

  /** 指で操作する端末で、端末のキーボード (の IME) の代わりに、入力欄にフォーカスがある間だけ画面のキーボードを出す。 */
  function onScreenKeyboard(element, field) {
    const suppressed = field.suppressSystemKeyboard();
    const target = { element, field, drawsCaret: suppressed.drawsCaret };
    const keys = sharedKeyboard();
    const redraw = () => active === target && keys.drawCaret();
    element.addEventListener("input", redraw);
    element.addEventListener("scroll", redraw);
    return {
      drawCaret: redraw,
      show() {
        keys.show(target);
        // 候補はキーボードのすぐ上に重ねて出すので、打っている入力欄を画面の上へ送る
        element.scrollIntoView({ block: "start", behavior: "smooth" });
      },
      hide: () => keys.hide(target),
      remove() {
        keys.hide(target);
        suppressed.restore();
        element.removeEventListener("input", redraw);
        element.removeEventListener("scroll", redraw);
      },
    };
  }

  globalThis.Meltype = {
    load,
    attach,
    /** ES モジュール (index.mjs) が、自分の場所を本体の場所として渡す */
    setBaseUrl(url) {
      baseUrl ??= new URL(url, location.href);
    },
  };
})();
