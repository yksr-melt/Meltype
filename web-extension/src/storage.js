// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// 覚えたことの置き場。Web エンジンの storage (get・set・onChange) の形で、拡張機能の storage.local に置く
// (サイトの IndexedDB に置くと、そのサイトから読めてしまう)。
// エンジンをページの中で動かすとき (content.js) と、拡張機能の中で動かすとき (frame.js) の両方で使う。
// ほかのタブが保存したときと、ポップアップの「覚えたことを消す」が消したとき (null) に onChange で知らせる。

globalThis.meltypeLearningStorage = (() => {
  const api = globalThis.browser ?? globalThis.chrome;
  return {
    get: async () => (await api.storage.local.get("learning")).learning ?? null,
    set: (json) => api.storage.local.set({ learning: json }),
    onChange(listener) {
      api.storage.onChanged.addListener((changes, area) => {
        if (area === "local" && changes.learning) listener(changes.learning.newValue ?? null);
      });
    },
  };
})();
