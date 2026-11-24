// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// 拡張機能の裏のスクリプト。ショートカットキー (commands の toggle-meltype) で一時的にオン・オフし、オフの間はボタンに「OFF」と出す。
// 一時的なオフ (storage.local の paused) は、ブラウザーを起動し直すとオンに戻す (戻し忘れて、ずっと使えないままにならないように)。
// ページのスクリプト (content.js) とポップアップは storage.local の変化を見るので、ここから知らせる必要はない。

const api = globalThis.browser ?? globalThis.chrome;

async function showPaused(paused) {
  await api.action.setBadgeText({ text: paused ? "OFF" : "" });
  await api.action.setBadgeBackgroundColor({ color: "#5d6984" });
}

api.commands.onCommand.addListener(async (command) => {
  if (command !== "toggle-meltype") return;
  const { paused = false } = await api.storage.local.get("paused");
  await api.storage.local.set({ paused: !paused });
});

api.storage.onChanged.addListener((changes, area) => {
  if (area === "local" && changes.paused) showPaused(changes.paused.newValue === true);
});

api.runtime.onStartup.addListener(() => api.storage.local.set({ paused: false }));
api.runtime.onInstalled.addListener(async () => {
  const { paused = false } = await api.storage.local.get("paused");
  await showPaused(paused);
});
