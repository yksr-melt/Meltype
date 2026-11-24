// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// どのページにも入れる小さなスクリプト。入力欄に初めてフォーカスしたときだけ、拡張機能の中の Web エンジン (engine/) を読み、
// その入力欄に付ける (重いので、入力欄を使わないページでは読まない)。
// エンジンはこのコンテンツスクリプトの中で動く。キーをページに通すかをその場で決める必要があるので、裏のページとのやり取りにはできない。
// 覚えたことは拡張機能の storage に置く (ページの IndexedDB に置くと、そのサイトから読めてしまう)。

(() => {
  "use strict";

  const api = globalThis.browser ?? globalThis.chrome;
  // 付ける <input> の種類。メールアドレス・数字・パスワードなどは Meltype の出番がない
  const TEXT_INPUT_TYPES = new Set(["text", "search", ""]);

  let enginePromise = null;
  // 覚えたことを消したら、このタブを読み込み直すまで残さない (エンジンはまだ前のことを覚えているので)
  let cleared = false;
  const attachments = new Map();
  let disabledSites = [];
  // 一時的にオフ (ショートカットキー・ポップアップ。background.js がブラウザーの起動でオンに戻す)
  let paused = false;

  const storage = {
    get: async () => (await api.storage.local.get("learning")).learning ?? null,
    set: async (json) => {
      if (!cleared) await api.storage.local.set({ learning: json });
    },
    clear: () => api.storage.local.remove("learning"),
  };

  // 読めなかったら、このページでは読み直さない (入力欄を選ぶたびに 40MB を読み直さないように)。
  // ページの CSP が WebAssembly を許していない (script-src に 'wasm-unsafe-eval' が無い) と、拡張機能のスクリプトの中でも動かせない
  const loadEngine = () => enginePromise ??= (async () => {
    await import(api.runtime.getURL("engine/meltype.js"));
    return globalThis.Meltype.load({ baseUrl: api.runtime.getURL("engine/"), storage });
  })().catch((error) => {
    blocked = true;
    throw error;
  });
  let blocked = false;

  const siteEnabled = () => !paused && !disabledSites.includes(location.hostname);

  /** Meltype を付ける要素。contenteditable は、編集できる範囲のいちばん外側 */
  function target(element) {
    if (element instanceof HTMLTextAreaElement) return element.readOnly || element.disabled ? null : element;
    if (element instanceof HTMLInputElement) return TEXT_INPUT_TYPES.has(element.getAttribute("type")?.toLowerCase() ?? "") && !element.readOnly && !element.disabled ? element : null;
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
    if (changes.learning && changes.learning.newValue === undefined) cleared = true;
  });
  document.addEventListener("focusin", onFocusIn, true);
  // ポップアップが、このページで動いているかを聞く (一番上のフレームだけが答える)
  if (window === window.top) {
    api.runtime.onMessage.addListener((message, _sender, sendResponse) => {
      if (message?.type !== "meltype:status") return;
      sendResponse({ blocked, loaded: enginePromise !== null && !blocked });
    });
  }
})();
