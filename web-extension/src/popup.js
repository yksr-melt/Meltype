// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 hisiragi
//
// ツールバーのボタンを押すと出る画面: 一時的なオン・オフ、このサイトで使うか、除外サイトの一覧、ショートカットキー、覚えたことを消す。
// 設定は storage.local の paused (一時的にオフ) と disabledSites (使わないサイトのホスト名の一覧) にだけ置く。
// ページのスクリプト (content.js) と background.js は storage の変化を見るので、ここから知らせる必要はない。

const api = globalThis.browser ?? globalThis.chrome;
const COMMAND = "toggle-meltype";
const $ = (selector) => document.querySelector(selector);

// ---- 一時的なオン・オフ ----

const enabled = $("[data-enabled]");
const enabledNote = $("[data-enabled-note]");
const showEnabled = (paused) => {
  enabled.checked = !paused;
  enabledNote.textContent = paused
    ? "一時的にオフです。ブラウザーを起動し直すとオンに戻ります。"
    : "入力欄を選ぶと、ローマ字のまま日本語と英語を分けて打てます。";
};
enabled.addEventListener("change", () => api.storage.local.set({ paused: !enabled.checked }));

// ---- 除外サイト ----

/** 入力をホスト名にする (https://example.com/path や example.com:8080 も受け付ける)。ホスト名でなければ null */
function hostOf(text) {
  const trimmed = text.trim().toLowerCase();
  if (!trimmed) return null;
  try {
    const { hostname } = new URL(/^[a-z][a-z0-9+.-]*:\/\//.test(trimmed) ? trimmed : `https://${trimmed}`);
    return /^[a-z0-9.-]+$/.test(hostname) && hostname.includes(".") || hostname === "localhost" ? hostname : null;
  } catch {
    return null;
  }
}

async function setExcluded(site, excluded) {
  const { disabledSites = [] } = await api.storage.local.get("disabledSites");
  const next = excluded ? [...new Set([...disabledSites, site])].sort() : disabledSites.filter((s) => s !== site);
  await api.storage.local.set({ disabledSites: next });
}

let currentSite = null;

function showSites(disabledSites) {
  const list = $("[data-sites]");
  list.replaceChildren(...disabledSites.map((site) => {
    const item = document.createElement("li");
    const name = Object.assign(document.createElement("span"), { textContent: site });
    const remove = Object.assign(document.createElement("button"), { type: "button", textContent: "外す" });
    remove.setAttribute("aria-label", `${site} を除外サイトから外す`);
    remove.addEventListener("click", () => setExcluded(site, false));
    item.append(name, remove);
    return item;
  }));
  $("[data-sites-empty]").hidden = disabledSites.length > 0;
  if (currentSite !== null) $("[data-site-toggle]").checked = !disabledSites.includes(currentSite);
}

$("[data-add]").addEventListener("submit", async (event) => {
  event.preventDefault();
  const input = $("[data-add-input]");
  const site = hostOf(input.value);
  const error = $("[data-add-error]");
  error.hidden = site !== null;
  if (site === null) {
    error.textContent = "サイトのアドレス (example.com など) を入れてください。";
    return;
  }
  await setExcluded(site, true);
  input.value = "";
});

// ---- このサイト ----

async function showCurrentSite() {
  const [tab] = await api.tabs?.query({ active: true, currentWindow: true }) ?? [];
  const url = URL.canParse?.(tab?.url ?? "") ? new URL(tab.url) : null;
  // 拡張機能はブラウザーの画面 (chrome:// など) やストアのページには入れない
  if (!url || !["http:", "https:", "file:"].includes(url.protocol) || !url.hostname) {
    $("[data-site-note]").hidden = false;
    return;
  }
  currentSite = url.hostname;
  $("[data-site-name]").textContent = currentSite;
  $("[data-site-row]").hidden = false;
  $("[data-site-toggle]").addEventListener("change", (event) => setExcluded(currentSite, !event.target.checked));
  // このページでどう動いているか (ページのスクリプトがまだ無いページは答えない)
  api.tabs.sendMessage(tab.id, { type: "meltype:status" }).then((status) => {
    if (status?.blocked) $("[data-blocked-note]").hidden = false;
    else if (status?.remote) $("[data-remote-note]").hidden = false;
  }, () => {});
}

// ---- ショートカットキー ----

const shortcutLabel = $("[data-shortcut]");
const shortcutError = $("[data-shortcut-error]");

async function showShortcut() {
  const command = (await api.commands.getAll()).find((c) => c.name === COMMAND);
  shortcutLabel.textContent = command?.shortcut || "なし";
}

/**
 * 押したキーを、commands.update の形 (Alt+Shift+M など) にする。足りなければ null。
 * Firefox の決まり: Ctrl か Alt (Mac は Command・MacCtrl) が要る (F1〜F12 だけは要らない)
 */
function shortcutOf(event) {
  const isMac = navigator.platform.startsWith("Mac");
  const named = {
    Comma: "Comma", Period: "Period", Space: "Space", Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
    Insert: "Insert", Delete: "Delete", ArrowUp: "Up", ArrowDown: "Down", ArrowLeft: "Left", ArrowRight: "Right",
  };
  const key = /^Key[A-Z]$/.test(event.code) ? event.code.slice(3)
    : /^Digit\d$/.test(event.code) ? event.code.slice(5)
      : /^F([1-9]|1[0-2])$/.test(event.code) ? event.code
        : named[event.code];
  if (!key) return null;
  const modifiers = [];
  // Firefox の "Ctrl" は Mac では Command キー、"MacCtrl" は Mac の Control キー
  if (isMac ? event.metaKey : event.ctrlKey) modifiers.push("Ctrl");
  if (isMac && event.ctrlKey) modifiers.push("MacCtrl");
  if (event.altKey) modifiers.push("Alt");
  if (event.shiftKey) modifiers.push("Shift");
  const functionKey = /^F\d+$/.test(key);
  if (!functionKey && !modifiers.some((m) => m !== "Shift")) return null;
  return [...modifiers, key].join("+");
}

function setUpShortcut() {
  // 拡張機能からショートカットキーを変えられるのは Firefox だけ。Chrome・Edge はブラウザーの設定の画面で変える
  if (typeof api.commands.update !== "function") {
    const open = $("[data-open-shortcuts]");
    open.hidden = false;
    open.addEventListener("click", () => {
      const page = navigator.userAgent.includes("Edg/") ? "edge://extensions/shortcuts" : "chrome://extensions/shortcuts";
      api.tabs.create({ url: page });
    });
    return;
  }
  const record = $("[data-record]");
  const reset = $("[data-reset]");
  const note = $("[data-record-note]");
  record.hidden = false;
  reset.hidden = false;
  const stop = () => {
    document.removeEventListener("keydown", onKey, true);
    shortcutLabel.classList.remove("is-recording");
    note.hidden = true;
    record.disabled = false;
  };
  const onKey = async (event) => {
    event.preventDefault();
    if (event.key === "Escape") {
      stop();
      return showShortcut();
    }
    if (["Control", "Alt", "Shift", "Meta"].includes(event.key)) return;
    const shortcut = shortcutOf(event);
    shortcutError.hidden = shortcut !== null;
    if (shortcut === null) {
      shortcutError.textContent = "Ctrl か Alt と一緒に、文字・数字・F1〜F12 などのキーを押してください。";
      return;
    }
    stop();
    try {
      await api.commands.update({ name: COMMAND, shortcut });
    } catch (error) {
      shortcutError.textContent = `このキーは使えません (${error.message})。`;
      shortcutError.hidden = false;
    }
    await showShortcut();
  };
  record.addEventListener("click", () => {
    record.disabled = true;
    shortcutError.hidden = true;
    note.hidden = false;
    shortcutLabel.textContent = "…";
    shortcutLabel.classList.add("is-recording");
    document.addEventListener("keydown", onKey, true);
  });
  reset.addEventListener("click", async () => {
    await api.commands.reset(COMMAND);
    shortcutError.hidden = true;
    await showShortcut();
  });
}

// ---- 覚えたこと ----

// 消したことは、Web エンジンを読み込んでいるタブに storage.onChanged で届き (storage.js)、それぞれが覚えたことを消す
$("[data-clear]").addEventListener("click", async () => {
  await api.storage.local.remove("learning");
  $("[data-cleared]").hidden = false;
});

// ---- 始める ----

(async () => {
  const { paused = false, disabledSites = [] } = await api.storage.local.get(["paused", "disabledSites"]);
  showEnabled(paused);
  await showCurrentSite();
  showSites(disabledSites);
  // ショートカットキーの機能 (commands) が無いところ (拡張機能のページを iframe で開いたときなど) では、その欄を出さない
  if (api.commands) {
    setUpShortcut();
    await showShortcut();
  } else {
    $("#shortcut-title").closest("section").hidden = true;
  }
  // ショートカットキーで切り替えた・ほかの画面で変えたときも、出している内容を合わせる
  api.storage.onChanged.addListener((changes, area) => {
    if (area !== "local") return;
    if (changes.paused) showEnabled(changes.paused.newValue === true);
    if (changes.disabledSites) showSites(changes.disabledSites.newValue ?? []);
  });
})();
