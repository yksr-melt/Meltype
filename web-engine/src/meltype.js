// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
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
  // 付ける <input> の種類 (文字を打つもの。キャレットを読めて、画面のキーボードのために readonly にしても困らない)
  const TEXT_INPUT_TYPES = new Set(["text", "search", "url", "tel"]);

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

  const DB_NAME = "meltype-web-engine";
  const DB_STORE = "data";

  // <script> で読んだときは、そのファイルの場所から本体 (_framework/・mozc/) を探す
  let baseUrl = document.currentScript?.src ? new URL(".", document.currentScript.src) : null;
  let enginePromise = null;
  // 箱・キーボード・カーソルを入れる Shadow DOM (load で作る)
  let ui = null;

  /**
   * 本体 (Core と Mozc) を読み込み、入力欄に付けられるようにする。2 回目からは同じものを返す。
   * options.baseUrl: 本体を置いた場所。options.persist: 学習データを残すか (既定は true)。
   * options.storage: 学習データの置き場 ({ get(): Promise<string | null>, set(json): Promise, onChange(listener(json | null))? })。
   *   onChange はほかのタブなどが保存した・消した (null) ときに呼ぶ。既定はそのページの IndexedDB。
   * options.onProgress({ phase, loaded, total }): 読み込みの進み具合 (phase は "core"・"dictionary"・"ready")。
   * options.core: ほかの場所で動かしている Core (loadCore の返す物と同じ形で、create・handle が Promise を返す)。
   *   拡張機能 (web-extension/) が、ページの CSP が WebAssembly を止めているときに、拡張機能の中で動かす Core を渡す。
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
    const css = await fetch(new URL("meltype.css", baseUrl)).then((response) => {
      if (!response.ok) throw new Error(`meltype.css: ${response.status}`);
      return response.text();
    });
    const core = options.core ?? await loadCore(options);
    ui = createUi(css);
    return {
      version: core.version,
      mozc: core.mozc,
      attach: (element, attachOptions) => attachTo(core, element, attachOptions),
      /** 覚えたことを消す (次に読み込んだときから、覚える前に戻る)。 */
      clearLearning: () => core.clearLearning(),
    };
  }

  /**
   * Core (判定・変換・学習) だけを読み込む。入力欄の画面は持たない。
   * 返す物: { version, mozc, create(), handle(host, json), onReady(listener) → 外す関数, clearLearning() }。
   * create は入力欄 1 つの本体の番号を、handle は出来事 (ImeEvent の JSON) への答え (ImeOutput の JSON) を返す。
   */
  async function loadCore(options = {}) {
    if (options.baseUrl) baseUrl = new URL(options.baseUrl, location.href);
    if (!baseUrl) throw new Error("Meltype: 本体の場所が分かりません。loadCore({ baseUrl }) で指定してください");
    const progress = options.onProgress ?? (() => {});
    const persist = options.persist !== false;

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

    const save = () => {
      store?.set(core.ExportData()).catch((error) => console.warn("Meltype: 学習データを残せませんでした", error));
    };
    // ほかのタブ (と拡張機能のポップアップ) が保存した・消した学習データを、この Core のファイルに書いて読み直させる
    // (デスクトップ版で、入力と設定画面が学習データを共有するのと同じ)。
    // このタブが覚えたことは確定したときに保存し終えている (handle) ので、読み直しても無くならない。
    // 無くなるのは、2 つのタブでほぼ同時に (保存が届くまでの間に) 確定したときだけ
    store?.onChange?.((json) => {
      if (json) core.ImportData(json);
      else {
        // 消された (拡張機能のポップアップの「覚えたことを消す」)
        core.ResetLearning();
        save();
      }
    });

    progress({ phase: "ready" });
    return {
      version: core.Version(),
      mozc: mozc !== null,
      create: () => core.ImeCreate(),
      destroy: (host) => core.ImeDestroy(host),
      handle(host, json) {
        const output = core.ImeHandle(host, json);
        // 確定したら、すぐに学習データを写す (Core は確定の処理の中で学習データのファイルを書く)。
        // 遅らせると、その間にほかのタブの保存を読み直して、覚えたことが無くなる
        if (!output.includes('"commits":[]')) save();
        return output;
      },
      onReady(listener) {
        listeners.add(listener);
        return () => listeners.delete(listener);
      },
      /** 学習データをすべて消し、すぐに保存する (ほかのタブも、保存されたものを読み直して消える) */
      async clearLearning() {
        core.ResetLearning();
        await store?.set(core.ExportData());
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

  /**
   * 学習データの置き場 (IndexedDB)。開けなければ (プライベートブラウズなど) null で、残さずに動く。
   * 同じサイトのほかのタブには、保存したことを BroadcastChannel で知らせる (onChange)。
   */
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
      const channel = new BroadcastChannel(DB_NAME);
      return {
        get: () => run("readonly", (s) => s.get("files")),
        async set(json) {
          await run("readwrite", (s) => s.put(json, "files"));
          channel.postMessage(json);
        },
        onChange(listener) {
          channel.addEventListener("message", (event) => listener(event.data));
        },
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
   *
   * Core がこのページで動いているときは、答えをその場で受け取り、キーを入力欄に通すかを Core が決める。
   * ほかの場所で動いている (core.create が Promise を返す。拡張機能) ときは答えを待てないので、
   * Core が前の答えで知らせた「受け取るキー」(ImeState.Capture。Windows の TSF 版と同じ) でその場で決め、
   * 外れたら (受け取ると決めたのに Core が通したら) 、そのキーの動きを代わりに入力欄に書く。
   */
  function attachTo(core, element, options = {}) {
    // 文字の <input> 以外 (パスワード・メールアドレス・数字など) には付けない。
    // パスワードに付けると、画面のキーボードのために readonly にして打てなくなり、メールアドレスなどはキャレットを読めない (setRangeText が例外を投げる)
    if (element instanceof HTMLInputElement && !TEXT_INPUT_TYPES.has(element.type)) return { detach() {} };
    const field = element.isContentEditable ? editableField(element) : textField(element);
    const created = core.create();
    const remote = created instanceof Promise;
    let host = remote ? null : created;
    const box = createBox();
    let view = null;
    let state = null;
    let focused = false;
    // 答えを待っている出来事の数と、その順番 (ほかの場所の Core に、送った順に答えさせる)
    let pending = 0;
    // 変換中に入力欄を押したときの、押す前のキャレット (field.selection())。押して確定し終わるまで、確定した文字はここに書く
    let anchor = null;
    // 受け取ったキー (KeyboardEvent.code)。離したとき (keyup) もページに届けない
    const consumedKeys = new Set();
    let queue = remote ? created.then((h) => { host = h; }) : null;

    /** 出来事を送る。Core がこのページにあれば受け取ったか (consumed) を、ほかの場所にあればそれを返す Promise を返す */
    const send = (type, extra = {}) => {
      // 入力欄の様子は、前の出来事への答えを入力欄に書いてから読む (ほかの場所の Core なら、順番が来たときに読む)。
      // 押したときに読むと、速く打ったときに、まだ書いていない答えの前の様子を送り、Core が変換中の文字を捨てる
      const json = () => JSON.stringify({
        version: IME_VERSION, type, instance: "web",
        field: { ...field.context(), composing: view !== null, app: location.hostname || null, title: document.title || null },
        ...extra,
      });
      if (!remote) return finish(type, core.handle(host, json()));
      pending++;
      const answer = queue.then(() => core.handle(host, json())).then((output) => {
        pending--;
        return finish(type, output);
      }, (error) => {
        pending--;
        throw error;
      });
      queue = answer.catch((error) => console.warn("Meltype: 答えを受け取れませんでした", error));
      return answer;
    };

    const finish = (type, text) => {
      const output = JSON.parse(text);
      state = output.state;
      apply(output.result);
      // 日本語のモードだけで使う (MODE_KEYS)。Core の設定のショートカットなどで英数を選んだら、日本語に戻す。
      // コードの行は日本語を選んだままでも英数になる (languageMode) ので、選んでいるモード (imeOpen) で見る
      if (!output.state.imeOpen && type !== "JapaneseModeSelected") send("JapaneseModeSelected");
      return output.result.consumed;
    };

    /**
     * ほかの場所の Core が、このキーを受け取るか (答えを待たずに決める)。変換中・答えを待っている間は、修飾キーの無いキーはすべて受け取る。
     * 変換していなければ、Core が前の答えで知らせた受け取るキー (印字できる ASCII と、Space などのショートカット) だけ。
     */
    const predictConsumed = (e, key) => {
      if (state && !state.handlesInput) return false;
      const capture = state?.capture;
      const modifiers = (e.shiftKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.altKey ? 4 : 0) | (e.metaKey ? 8 : 0);
      const vk = key.vk || (/^[a-z]$/i.test(e.key) ? e.key.toUpperCase().charCodeAt(0) : /^\d$/.test(e.key) ? e.key.charCodeAt(0) : 0);
      const matches = (list) => (list ?? []).some((s) => s.vk === vk && s.modifiers === modifiers);
      if (view || pending > 0) return !(e.ctrlKey || e.altKey || e.metaKey) || matches(capture?.ctrlKeysWhileComposing);
      if (key.ch >= 0x21 && key.ch <= 0x7e && !(e.ctrlKey || e.altKey || e.metaKey)) {
        return (capture?.printableAscii ?? true) && !(capture?.printableAsciiExcept ?? "").includes(e.key);
      }
      return matches(capture?.shortcuts);
    };

    const apply = (result) => {
      for (const commit of result.commits) {
        if (anchor) anchor = field.insertAt(anchor, commit.text, commit.removeBefore ?? "");
        else field.insert(commit.text, commit.removeBefore ?? "", commit.moveCaretBack);
        options.onCommit?.(commit.text);
      }
      view = result.view;
      drawBox(box, view, () => field.caretRect());
      keyboard?.drawCaret();
    };

    /**
     * 受け取ったキーを、ブラウザーの動き (文字を入れる・フォームを送る) からも、ページのほかのリスナーからも隠す。
     * 確定の Enter で、Enter で送信するページが送信しないように。リスナーは window の capture で付けるので、ページのリスナーより先に動く
     */
    const consume = (e) => {
      e.preventDefault();
      e.stopImmediatePropagation();
      consumedKeys.add(e.code || e.key);
    };
    const forThisField = (e) => e.composedPath().includes(element);
    const onKeyUp = (e) => {
      if (!forThisField(e) || !consumedKeys.delete(e.code || e.key)) return;
      e.preventDefault();
      e.stopImmediatePropagation();
    };
    const onKeyPress = (e) => {
      if (forThisField(e) && consumedKeys.has(e.code || e.key)) consume(e);
    };
    const onKeyDown = (e) => {
      if (!forThisField(e)) return;
      // OS の IME で変換している間は、その IME に任せる
      if (e.isComposing || e.keyCode === 229) return;
      if (MODIFIER_KEYS.has(e.key)) return;
      if (MODE_KEYS.has(e.key)) {
        consume(e);
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
      if (remote) {
        if (predictConsumed(e, key)) {
          consume(e);
          // 外れた (Core が通した)・答えが来なかったら、ブラウザーの代わりにキーの動きを入力欄に書く (キーを失わないように)
          const pass = () => {
            field.passKey(e.key, e.shiftKey);
            if (key.vk) send("CaretMoved", { caret: { moved: true, passedKey: key.vk, ctrl: key.ctrl } });
          };
          send("KeyPressed", { key }).then((consumed) => consumed || pass(), (error) => {
            console.warn("Meltype: 答えを受け取れなかったので、キーをそのまま入れます", error);
            field.passKey(e.key, e.shiftKey);
          });
        } else {
          // 変換中に Ctrl などと一緒に押したキーは、変換中の文字を確定してから通す (確定はブラウザーが反映した後に届く)
          if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
          if (key.vk) setTimeout(() => send("CaretMoved", { caret: { moved: true, passedKey: key.vk, ctrl: key.ctrl } }));
        }
        return;
      }
      if (send("KeyPressed", { key })) {
        consume(e);
      } else if (key.vk) {
        // Core が通したキー (Backspace・矢印など) で入力欄が変わったことを知らせる。ブラウザーが反映した後に送る
        setTimeout(() => send("CaretMoved", { caret: { moved: true, passedKey: key.vk, ctrl: key.ctrl } }));
      }
    };
    /**
     * 変換中に入力欄を押したら、選択が動く前の位置に確定する (TSF 版と同じ)。押した後に確定すると、確定した文字が押した場所に入り、範囲を選ぶとその範囲を置き換える。
     * ほかの場所の Core の答えは選択が動いた後に届くので、押す前の位置を覚えて書く。押して選んだ位置・範囲は動かさない
     */
    const onPointerDown = () => {
      if (!view && pending === 0) return;
      anchor = field.selection();
      const done = () => { anchor = null; };
      const committed = send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
      if (remote) committed.then(done, done);
      else done();
    };
    // 押して動いたキャレットを知らせる (確定より後に届く)
    const onPointerUp = () => send("CaretMoved", { caret: { moved: true, passedKey: 0 } });
    const onFocus = () => {
      focused = true;
      keyboard?.show();
      send("CaretMoved", { caret: { moved: true, passedKey: 0, focusEntered: true } });
    };
    const onBlur = () => {
      focused = false;
      consumedKeys.clear();
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

    addEventListener("keydown", onKeyDown, true);
    addEventListener("keypress", onKeyPress, true);
    addEventListener("keyup", onKeyUp, true);
    element.addEventListener("pointerdown", onPointerDown);
    element.addEventListener("pointerup", onPointerUp);
    element.addEventListener("focus", onFocus);
    element.addEventListener("blur", onBlur);
    box.addEventListener("pointerdown", onPointerDownBox);
    box.addEventListener("click", onClickCandidate);
    const offReady = core.onReady(onReady);
    send("StateRequested");
    // フォーカスがあってから付けた (本体を読んでいる間に入力欄を選んだ・拡張機能が付けた) ときは、ここで入ったことにする
    if (element.getRootNode().activeElement === element) onFocus();

    return {
      detach() {
        if (view) send("CommitRequested", { caret: { moved: true, passedKey: 0 } });
        removeEventListener("keydown", onKeyDown, true);
        removeEventListener("keypress", onKeyPress, true);
        removeEventListener("keyup", onKeyUp, true);
        element.removeEventListener("pointerdown", onPointerDown);
        element.removeEventListener("pointerup", onPointerUp);
        element.removeEventListener("focus", onFocus);
        element.removeEventListener("blur", onBlur);
        offReady();
        keyboard?.remove();
        box.remove();
        // 入力欄の本体を消す (送った出来事への答えを受け取ってから)
        if (remote) queue.then(() => core.destroy?.(host));
        else core.destroy?.(host);
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
      selection: () => ({ start: element.selectionStart, end: element.selectionEnd }),
      /**
       * selection() で覚えた位置に書き、書いた後の位置を返す。今の選択は、書いた所より後ろなら書いた分だけずらす。
       * 書いた所と同じ位置 (変換していた所を押した) なら、書いた文字の後ろにする ("preserve" は前に残すので自分でずらす)
       */
      insertAt(at, text, removeBefore) {
        const start = at.start - removeBefore.length;
        const { selectionStart, selectionEnd, selectionDirection } = element;
        const shift = (i) => (i >= at.end ? i + text.length - (at.end - start) : Math.min(i, start));
        element.setRangeText(text, start, at.end);
        element.setSelectionRange(shift(selectionStart), shift(selectionEnd), selectionDirection);
        element.dispatchEvent(new Event("input", { bubbles: true }));
        return { start: start + text.length, end: start + text.length };
      },
      /**
       * ブラウザーの代わりにキーの動きを入力欄に書く。合成したキー (画面のキーボード) と、ほかの場所の Core が通したキー (止めた後なので、ブラウザーは何もしない) のため。
       * 書けるのは文字・Backspace・Enter・Tab・← → だけ
       */
      passKey(key, shift = false) {
        const { selectionStart: start, selectionEnd: end, value } = element;
        if (key === "Tab") {
          moveFocus(element, shift);
          return;
        }
        if (key === "Enter" && element instanceof HTMLInputElement) {
          // 1 行の入力欄の Enter はフォームを送る (ブラウザーと同じ。送るボタンのないフォームでも送る)
          element.form?.requestSubmit();
          return;
        }
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
      selection: () => range()?.cloneRange() ?? null,
      /**
       * selection() で覚えた位置 (Range) に書き、書いた後の位置を返す。今の選択は書いた後に戻す
       * (Range は DOM の書き換えに合わせて動くので、書いた分だけずれる)
       */
      insertAt(at, text, removeBefore) {
        const selection = getSelection();
        const current = selection.rangeCount ? selection.getRangeAt(0).cloneRange() : null;
        selection.removeAllRanges();
        if (at) selection.addRange(at);
        this.insert(text, removeBefore, 0);
        const next = selection.rangeCount ? selection.getRangeAt(0).cloneRange() : null;
        selection.removeAllRanges();
        if (current) selection.addRange(current);
        return next;
      },
      passKey(key, shift = false) {
        if (key === "Tab") moveFocus(element, shift);
        else if ([...key].length === 1) document.execCommand("insertText", false, key);
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

  /** Tab の代わりに、フォーカスを次 (shift なら前) の入力欄・ボタン・リンクに移す (tabindex の順は見ず、文書の順で) */
  function moveFocus(element, backward) {
    const focusable = [...document.querySelectorAll('input, textarea, select, button, a[href], [contenteditable=""], [contenteditable="true"], [tabindex]')]
      .filter((e) => !e.disabled && e.tabIndex >= 0 && e.getClientRects().length > 0);
    const index = focusable.indexOf(element);
    focusable.at((index + (backward ? -1 : 1)) % focusable.length)?.focus();
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
    loadCore,
    attach,
    /** ES モジュール (index.mjs) が、自分の場所を本体の場所として渡す */
    setBaseUrl(url) {
      baseUrl ??= new URL(url, location.href);
    },
  };
})();
