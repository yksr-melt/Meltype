// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// ツールバーのボタンを押すと出る画面: 今のサイトで使うかと、覚えたことを消す。
// 設定は storage.local の disabledSites (使わないサイトのホスト名の一覧) にだけ置く。ページのスクリプト (content.js) はその変化を見て外す。

const api = globalThis.browser ?? globalThis.chrome;

(async () => {
  const [tab] = await api.tabs.query({ active: true, currentWindow: true });
  const url = URL.canParse?.(tab?.url ?? "") ? new URL(tab.url) : null;
  // 拡張機能はブラウザーの画面 (chrome:// など) やストアのページには入れない
  if (!url || !["http:", "https:", "file:"].includes(url.protocol)) {
    document.querySelector("[data-site-note]").hidden = false;
  } else {
    const site = url.hostname;
    const toggle = document.querySelector("[data-site-toggle]");
    document.querySelector("[data-site-name]").textContent = site || url.protocol.replace(":", "");
    document.querySelector("[data-site-row]").hidden = false;
    const { disabledSites = [] } = await api.storage.local.get("disabledSites");
    toggle.checked = !disabledSites.includes(site);
    // このページで、サイトの CSP のために動かせなかったか (ページのスクリプトがまだ無いページは答えない)
    api.tabs.sendMessage(tab.id, { type: "meltype:status" }).then((status) => {
      if (status?.blocked) document.querySelector("[data-blocked-note]").hidden = false;
    }, () => {});
    toggle.addEventListener("change", async () => {
      const { disabledSites: current = [] } = await api.storage.local.get("disabledSites");
      const next = toggle.checked ? current.filter((s) => s !== site) : [...new Set([...current, site])];
      await api.storage.local.set({ disabledSites: next });
      document.querySelector("[data-reload-note]").hidden = false;
    });
  }

  document.querySelector("[data-clear]").addEventListener("click", async () => {
    await api.storage.local.remove("learning");
    document.querySelector("[data-cleared]").hidden = false;
  });
})();
