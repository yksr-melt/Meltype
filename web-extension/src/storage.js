// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 覚えたことの置き場。Web エンジンの storage (get・set・clear) の形で、拡張機能の storage.local に置く
// (サイトの IndexedDB に置くと、そのサイトから読めてしまう)。
// エンジンをページの中で動かすとき (content.js) と、拡張機能の中で動かすとき (frame.js) の両方で使う。

globalThis.meltypeLearningStorage = (() => {
  const api = globalThis.browser ?? globalThis.chrome;
  // ポップアップで消したら、読み込み直すまで残さない (エンジンはまだ前のことを覚えているので)
  let cleared = false;
  api.storage.onChanged.addListener((changes, area) => {
    if (area === "local" && changes.learning && changes.learning.newValue === undefined) cleared = true;
  });
  return {
    get: async () => (await api.storage.local.get("learning")).learning ?? null,
    set: async (json) => {
      if (!cleared) await api.storage.local.set({ learning: json });
    },
    clear: () => api.storage.local.remove("learning"),
  };
})();
