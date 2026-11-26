// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// サイトの CSP が WebAssembly を止めているとき、Web エンジンの Core をこの拡張機能のページ (frame.html) で動かす。
// 拡張機能のページはサイトの CSP を受けない。content.js がこのページを見えない iframe でページに置き、runtime.connect でつなぐ。
// runtime.connect は拡張機能の中からしかつなげないので、サイトのスクリプトは Core (と、覚えたこと) に触れない。
// iframe の URL の token と、つなぐときの名前が同じものだけに答える (ほかのタブの iframe も同じ onConnect を受けるため)。
//
// 受ける要求 ({ id, op, … }) と答え ({ id, result } か { id, error }):
//   info               → { version, mozc }
//   create             → 入力欄 1 つの本体の番号
//   destroy(host)      → 入力欄の本体を捨てる
//   handle(host, json) → 出来事 (ImeEvent の JSON) への答え (ImeOutput の JSON)
// こちらから送るもの: { op: "ready" } (変換の結果が後から届いた)

(() => {
  "use strict";

  const api = globalThis.browser ?? globalThis.chrome;
  const token = new URLSearchParams(location.search).get("token");
  const corePromise = globalThis.Meltype.loadCore({ baseUrl: api.runtime.getURL("engine/"), storage: globalThis.meltypeLearningStorage });

  function answer(core, op, host, json) {
    switch (op) {
      case "info": return { version: core.version, mozc: core.mozc };
      case "create": return core.create();
      case "destroy": return core.destroy(host);
      case "handle": return core.handle(host, json);
      default: throw new Error(`知らない要求です: ${op}`);
    }
  }

  api.runtime.onConnect.addListener((port) => {
    if (!token || port.name !== `meltype:${token}`) return;
    corePromise.then((core) => core.onReady(() => port.postMessage({ op: "ready" })), () => {});
    port.onMessage.addListener(async ({ id, op, host, json }) => {
      try {
        const core = await corePromise;
        port.postMessage({ id, result: answer(core, op, host, json) });
      } catch (error) {
        port.postMessage({ id, error: String(error?.message ?? error) });
      }
    });
  });
})();
