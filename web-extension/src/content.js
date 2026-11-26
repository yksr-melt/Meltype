// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// どのページにも入れる小さなスクリプト。入力欄に初めてフォーカスしたときだけ、拡張機能の中の Web エンジン (engine/) を読み、
// その入力欄に付ける (重いので、入力欄を使わないページでは読まない)。
// エンジンは、ふだんはこのコンテンツスクリプトの中で動く (キーをページに通すかを、その場で Core に聞ける)。
// ページの CSP が WebAssembly を止めている (script-src に 'wasm-unsafe-eval' が無い) と、コンテンツスクリプトの中でも動かせないので、
// Core だけを拡張機能のページ (frame.html) で動かし、見えない iframe でページに置いてやり取りする (remoteCore)。
// 覚えたことは拡張機能の storage に置く (storage.js)。

(() => {
  "use strict";

  const api = globalThis.browser ?? globalThis.chrome;
  // 付ける <input> の種類 (Web エンジンが付ける種類と同じ。type が無いものは text)。
  // エンジンも同じ種類にだけ付けるが、ほかの種類の入力欄ではエンジンを読み込まないように、ここでも分ける
  const TEXT_INPUT_TYPES = new Set(["text", "search", "url", "tel"]);

  let enginePromise = null;
  const attachments = new Map();
  let disabledSites = [];
  // 一時的にオフ (ショートカットキー・ポップアップ。background.js がブラウザーの起動でオンに戻す)
  let paused = false;

  // 読めなかったら、このページでは読み直さない (入力欄を選ぶたびに読み直さないように)
  const loadEngine = () => enginePromise ??= (async () => {
    await import(api.runtime.getURL("engine/meltype.js"));
    const baseUrl = api.runtime.getURL("engine/");
    const core = await wasmAllowed() ? undefined : await remoteCore();
    return globalThis.Meltype.load({ baseUrl, storage: globalThis.meltypeLearningStorage, core });
  })().catch((error) => {
    blocked = true;
    throw error;
  });
  let blocked = false;
  // Core を拡張機能のページで動かしているか (ポップアップが見せる)
  let remote = false;

  /** このページの CSP の下で、WebAssembly を作れるか (いちばん小さいモジュールで試す) */
  async function wasmAllowed() {
    try {
      await WebAssembly.compile(new Uint8Array([0x00, 0x61, 0x73, 0x6d, 0x01, 0x00, 0x00, 0x00]));
      return true;
    } catch {
      return false;
    }
  }

  /**
   * 拡張機能のページ (frame.html) で動かす Core。ページに見えない iframe で置き、runtime.connect でやり取りする。
   * iframe は閉じた Shadow DOM の中に置き、サイトのスクリプトから触れないようにする。
   */
  async function remoteCore() {
    remote = true;
    const token = crypto.randomUUID();
    const holder = document.createElement("meltype-engine");
    Object.assign(holder.style, { position: "fixed", width: "0", height: "0", overflow: "hidden", visibility: "hidden" });
    const frame = document.createElement("iframe");
    frame.src = api.runtime.getURL(`frame.html?token=${token}`);
    frame.setAttribute("aria-hidden", "true");
    frame.tabIndex = -1;
    holder.attachShadow({ mode: "closed" }).append(frame);
    const loaded = new Promise((resolve) => frame.addEventListener("load", resolve, { once: true }));
    document.documentElement.append(holder);
    await loaded;

    const port = api.runtime.connect({ name: `meltype:${token}` });
    const calls = new Map();
    const listeners = new Set();
    let nextId = 0;
    port.onMessage.addListener(({ id, op, result, error }) => {
      if (op === "ready") return listeners.forEach((f) => f());
      const call = calls.get(id);
      calls.delete(id);
      if (error) call?.reject(new Error(error));
      else call?.resolve(result);
    });
    // 拡張機能を更新・再読み込みすると切れ、つなぎ直せない (このスクリプトも古いまま残る)。
    // 入力欄から外し、このページを読み直すまでキーを受け取らない
    port.onDisconnect.addListener(() => {
      for (const call of calls.values()) call.reject(new Error("Meltype の本体とのつながりが切れました"));
      calls.clear();
      blocked = true;
      detachAll();
    });
    const call = (op, args = {}) => new Promise((resolve, reject) => {
      const id = nextId++;
      calls.set(id, { resolve, reject });
      port.postMessage({ id, op, ...args });
    });
    const { version, mozc } = await call("info");
    return {
      version,
      mozc,
      create: () => call("create"),
      destroy: (host) => call("destroy", { host }),
      handle: (host, json) => call("handle", { host, json }),
      onReady(listener) {
        listeners.add(listener);
        return () => listeners.delete(listener);
      },
    };
  }

  const siteEnabled = () => !paused && !disabledSites.includes(location.hostname);

  /** Meltype を付ける要素。contenteditable は、編集できる範囲のいちばん外側 */
  function target(element) {
    if (element instanceof HTMLTextAreaElement) return element.readOnly || element.disabled ? null : element;
    if (element instanceof HTMLInputElement) return TEXT_INPUT_TYPES.has(element.type) && !element.readOnly && !element.disabled ? element : null;
    if (!element?.isContentEditable) return null;
    let host = element;
    while (host.parentElement?.isContentEditable) host = host.parentElement;
    return host;
  }

  async function onFocusIn(event) {
    // Shadow DOM の中の入力欄もある
    await attachTo(event.composedPath()[0]);
  }

  /** フォーカスのある要素 (Shadow DOM の中まで) */
  function focusedElement() {
    let element = document.activeElement;
    while (element?.shadowRoot?.activeElement) element = element.shadowRoot.activeElement;
    return element;
  }

  async function attachTo(candidate) {
    if (!siteEnabled() || blocked) return;
    const element = target(candidate);
    if (!element || attachments.has(element)) return;
    attachments.set(element, null);
    try {
      const engine = await loadEngine();
      if (siteEnabled() && attachments.has(element)) attachments.set(element, engine.attach(element));
    } catch (error) {
      attachments.delete(element);
      console.warn("Meltype: 読み込めませんでした", error);
    }
  }

  function detachAll() {
    for (const attachment of attachments.values()) attachment?.detach();
    attachments.clear();
  }

  api.storage.local.get(["disabledSites", "paused"]).then((value) => {
    disabledSites = value.disabledSites ?? [];
    paused = value.paused === true;
    // このスクリプトが入る前にフォーカスがあった入力欄 (開くと自動でフォーカスするページ) にも付ける
    attachTo(focusedElement());
  });
  api.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.disabledSites || changes.paused) {
      if (changes.disabledSites) disabledSites = changes.disabledSites.newValue ?? [];
      if (changes.paused) paused = changes.paused.newValue === true;
      // オフにしたらすぐ外し、オンにしたら今選んでいる入力欄にすぐ付ける
      if (siteEnabled()) attachTo(focusedElement());
      else detachAll();
    }
  });
  document.addEventListener("focusin", onFocusIn, true);
  // ポップアップが、このページで動いているかを聞く (一番上のフレームだけが答える)
  if (window === window.top) {
    api.runtime.onMessage.addListener((message, _sender, sendResponse) => {
      if (message?.type !== "meltype:status") return;
      sendResponse({ blocked, remote, loaded: enginePromise !== null && !blocked });
    });
  }
})();
