// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import {validatePreferences} from "./site-profiles.mjs";
import {ShuntiPackages,modelOrigins} from "./shunti.mjs";
import {cachedUpdate,downloadUpdate,hasUpdatePermission,requestUpdatePermission,writeUpdate} from "./update.mjs";
import {acceptAnalytics,analyticsAccepted,track} from "./analytics.mjs";
let aiPackages = new ShuntiPackages();
const aiVersion = document.querySelector("#ai-version");
let aiVersions, aiCatalogPromise, aiRefreshEpoch = 0;
async function refreshAI(value) {
  const variant = value.ShuntiModelVariant ?? "shunti-v7-s8p", epoch = ++aiRefreshEpoch;
  if (!aiVersions) {
    aiVersions = await (aiCatalogPromise ??= fetch(new URL("./ai/model-catalog.json",import.meta.url)).then(response => response.json()).then(document => document.models.filter(m => m.family === "Shunti")));
    if (epoch !== aiRefreshEpoch) return;
    if (!aiVersion.options.length) for (const model of aiVersions) { const option = document.createElement("option"); option.value = model.id; option.textContent = model.label; aiVersion.append(option); }
  }
  if (epoch !== aiRefreshEpoch) return;
  aiVersion.value = variant;
  const packages = new ShuntiPackages({variant}); aiPackages = packages;
  const installed = await packages.installed();
  if (packages !== aiPackages) return;
  aiInstall.textContent = installed ? "インストール済み" : "このモデルをインストール";
  aiInstall.disabled = installed;
  aiStatus.textContent = installed ? "インストール済みです。保存すると反映します。" : "未導入です。インストールを押してください。";
  return installed;
}
aiVersion.addEventListener("change", async () => {
  try {
    const value = parseSettings(); value.ShuntiModelVariant = aiVersion.value; value.UseShunti = false; settings.value = JSON.stringify(value,null,2);
    const installed = await refreshAI(value); const latest = parseSettings();
    if (latest.ShuntiModelVariant === value.ShuntiModelVariant && latest.AiModel === "Shunti") { latest.UseShunti = installed; settings.value = JSON.stringify(latest,null,2); }
  } catch (error) { aiStatus.textContent = String(error); }
});
const aiChoice = document.querySelector("#ai-model"), aiDetails = document.querySelector("#ai-details");
const aiStatus = document.querySelector("#ai-status"), aiInstall = document.querySelector("#ai-install");
function aiDetailsFor(value) { aiDetails.hidden = value.AiModel !== "Shunti" && !(value.AiModel == null && value.UseShunti === true); }
const status = document.querySelector("#status"), settings = document.querySelector("#settings");
const currentVersion = document.querySelector("#current-version"), updateStatus = document.querySelector("#update-status"), updateCheck = document.querySelector("#update-check");
const updateZip = document.querySelector("#update-zip"), reloadExtension = document.querySelector("#reload-extension");
currentVersion.textContent = chrome.runtime.getManifest?.().version ?? "?";
function showUpdate(update) {
  updateStatus.replaceChildren();
  if (!update) { updateStatus.textContent = "まだ確認していません。"; return; }
  if (!update.update) { updateStatus.textContent = `最新版です（確認: ${new Date(update.checkedAt).toLocaleString()}）。`; return; }
  const link = document.createElement("a");
  link.href = update.zipUrl; link.textContent = `v${update.version} のZIPを取得`;
  updateStatus.append(`新しい版 v${update.version} があります。`, link);
  updateStatus.append("CRXで導入している場合は自動で更新されます。");
}
const siteStatus = document.querySelector("#site-status"), siteEnabled = document.querySelector("#site-enabled");
const rules = document.querySelector("#site-rules"), defaultMode = document.querySelector("#default-mode");
const saveSettings = document.querySelector("#save-settings"), reloadSettings = document.querySelector("#reload");
const contextHelp = document.querySelector("#context-help"), helpPanel = document.querySelector("#help-panel");
const showHelp = document.querySelector("#show-help"), pageBody = document.querySelector("#page-body");
const tabs = [...document.querySelectorAll('[role="tab"]')];
const panels = [...document.querySelectorAll('[role="tabpanel"]')];
const pageActions = [...document.querySelectorAll("[data-actions]")];
const hints = {
  "panel-settings": "変更した設定を保存して反映します。",
  "panel-sites": "サイト別設定はこの端末だけに保存します。",
  "panel-data": "書き出すファイルには個人の辞書・学習を含みます。",
  "panel-help": "ChromeOSの入力方法でMeltype-FLASHを選択してください。",
};
function selectPage(selected) {
  for (const tab of tabs) {
    const active = tab === selected;
    tab.setAttribute("aria-selected", String(active));
    tab.tabIndex = active ? 0 : -1;
  }
  for (const panel of panels) panel.hidden = panel.id !== selected.dataset.panel;
  for (const actions of pageActions) actions.hidden = actions.dataset.actions !== selected.dataset.panel;
  document.querySelector("#page-hint").textContent = hints[selected.dataset.panel];
  contextHelp.textContent = "項目を選ぶと、ここに説明を表示します。";
  pageBody.scrollTop = 0;
}
for (const [index, tab] of tabs.entries()) {
  tab.addEventListener("click", () => selectPage(tab));
  tab.addEventListener("keydown", event => {
    let next;
    if (event.key === "ArrowRight") next = (index + 1) % tabs.length;
    else if (event.key === "ArrowLeft") next = (index + tabs.length - 1) % tabs.length;
    else if (event.key === "Home") next = 0;
    else if (event.key === "End") next = tabs.length - 1;
    else return;
    event.preventDefault();
    selectPage(tabs[next]);
    tabs[next].focus();
  });
}
showHelp.addEventListener("change", () => { helpPanel.hidden = !showHelp.checked; });
for (const field of document.querySelectorAll("[data-help]")) {
  field.setAttribute("aria-describedby", "context-help");
  const explain = () => { contextHelp.textContent = field.dataset.help; };
  field.addEventListener("focus", explain);
  field.addEventListener("mouseenter", explain);
}
selectPage(tabs[0]);
const desktopOnly = new Set(["AppRules", "AppKinds", "AppProfile", "StopInGames"]);
const neuralSettings = ["UseZenzai", "ZenzaiServerPath", "ZenzaiModelPath", "ShuntiHelperPath"];
function withoutNeuralSettings(value) {
  for (const key of neuralSettings) delete value[key];
  if (["Zenz","Jinen","Kanakanji"].includes(value.AiModel)) value.AiModel = "None";
  if (Array.isArray(value.Profiles)) for (const profile of value.Profiles) {
    if (profile?.Values && typeof profile.Values === "object") {
      for (const key of neuralSettings) delete profile.Values[key];
    }
  }
  return value;
}
let savedSettings = {};
const coreControls = document.querySelector("#core-controls");
const fields = [...document.querySelectorAll("[data-setting]")];
function disableCore(disabled) {
  coreControls.disabled = disabled;
  saveSettings.disabled = disabled;
  reloadSettings.disabled = disabled;
}
function parseSettings() {
  const value = JSON.parse(settings.value);
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error("設定はJSONオブジェクトで入力してください。");
  return withoutNeuralSettings(value);
}
function displayFields(value) {
  aiDetailsFor(value);
  for (const field of fields) {
    const current = field.dataset.setting === "AiModel" ? (value.AiModel ?? (value.UseShunti ? "Shunti" : "None")) : value[field.dataset.setting];
    if (field.type === "checkbox") field.checked = current === true;
    else {
      // 未知の選択肢も保持し、他の項目を変更しただけで既存設定を失わない。
      if (typeof current === "string" && ![...field.options].some(option => option.value === current)) {
        const option = document.createElement("option"); option.value = current; option.textContent = current; field.append(option);
      }
      field.value = current ?? "";
    }
  }
}
for (const field of fields) field.addEventListener("change", async () => {
  try {
    const value = parseSettings();
    const raw = field.type === "checkbox" ? field.checked : field.value;
    // 小数の桁は数として持つ設定 (文字列で入れると保存時に型が合わない)
    value[field.dataset.setting] = field.type === "number" ? Number(raw) || 0 : raw;
    if (field.dataset.setting === "AiModel") {
      value.UseShunti = false; aiDetailsFor(value);
    }
    settings.value = JSON.stringify(value, null, 2);
    if (field.dataset.setting === "AiModel" && value.AiModel === "Shunti") {
      const installed = await refreshAI(value);
      const latest = parseSettings();
      if (latest.AiModel === "Shunti") { latest.UseShunti = installed; settings.value = JSON.stringify(latest,null,2); }
      aiStatus.textContent = installed ? "導入済みです。保存すると反映します。" : "未導入です。インストールを押してください。";
    }
    status.textContent = "入力設定を変更しました。「入力設定を保存」で反映します。";
  } catch (error) {
    displayFields(savedSettings);
    status.textContent = `詳細設定のJSONを修正するか、保存済み設定へ戻してください。${error}`;
  }
});
settings.addEventListener("input", () => {
  try { displayFields(parseSettings()); } catch { /* 編集途中のJSONは保存時に検証する。 */ }
});
async function loadSettings() {
  const {snapshot} = await request({op: "snapshot"});
  savedSettings = withoutNeuralSettings(JSON.parse(snapshot).settings);
  settings.value = JSON.stringify(Object.fromEntries(Object.entries(savedSettings).filter(([key]) => !desktopOnly.has(key))), null, 2);
  displayFields(savedSettings);
  if (savedSettings.AiModel === "Shunti" || savedSettings.UseShunti === true) await refreshAI(savedSettings);
  disableCore(false);
}
function addRule(rule = {host: "", mode: "code"}) {
  const row = document.createElement("div"); row.className = "site-rule";
  const host = document.createElement("input");
  host.type = "text"; host.value = rule.host; host.placeholder = "例: vscode.dev";
  host.setAttribute("aria-label", "サイトのホスト名またはURL"); host.required = true;
  const mode = document.createElement("select"); mode.setAttribute("aria-label", "このサイトの入力モード");
  for (const [value, label] of [["normal", "通常入力"], ["code", "コード入力"]]) {
    const option = document.createElement("option"); option.value = value; option.textContent = label; mode.append(option);
  }
  mode.value = rule.mode;
  const remove = document.createElement("button"); remove.type = "button"; remove.textContent = "削除";
  remove.addEventListener("click", () => row.remove());
  row.append(host, mode, remove); rules.append(row);
}
function displayPreferences(preferences) {
  siteEnabled.checked = preferences.enabled; defaultMode.value = preferences.defaultMode;
  rules.replaceChildren(); preferences.rules.forEach(addRule);
}
document.querySelector("#site-add").addEventListener("click", () => {
  addRule(); rules.lastElementChild.querySelector("input").focus();
});
siteEnabled.addEventListener("change", async () => {
  if (!siteEnabled.checked) return;
  try {
    // Keep request() in the user gesture; do not await a background message first.
    if (!await chrome.permissions.request({permissions: ["tabs"]})) {
      siteEnabled.checked = false;
      siteStatus.textContent = "許可されませんでした。通常使うモードは権限なしでも利用できます。";
    } else siteStatus.textContent = "タブの参照を許可しました。「入力モードを保存」で反映します。";
  } catch (error) { siteEnabled.checked = false; siteStatus.textContent = String(error); }
});
document.querySelector("#site-form").addEventListener("submit", async event => {
  event.preventDefault();
  try {
    const preferences = validatePreferences({enabled: siteEnabled.checked, defaultMode: defaultMode.value,
      rules: [...rules.children].map(row => ({host: row.querySelector("input").value, mode: row.querySelector("select").value}))});
    const reply = await request({op: "save_site_preferences", preferences});
    displayPreferences(reply.preferences);
    siteStatus.textContent = "入力モードを保存しました。入力中の文字列がある場合は、確定後に切り替わります。";
  } catch (error) { siteStatus.textContent = String(error); }
});
async function request(message) {
  const reply = await chrome.runtime.sendMessage(message);
  if (reply?.error) throw new Error(reply.error);
  return reply;
}
async function show(work) {
  try { await work(); } catch (error) { status.textContent = String(error); }
}
async function reload() {
  const {preferences} = await request({op: "site_preferences"});
  displayPreferences(preferences);
  const state = await request({op: "status"});
  document.querySelector("#diagnostic").textContent = state.diagnostic;
  if (!state.available) {
    status.textContent = "この環境ではMeltypeを入力方法として利用できません。ChromeOSの入力方法・管理設定を確認してください。";
    disableCore(true);
    document.querySelectorAll("#export, #clear").forEach(element => { element.disabled = true; });
    return;
  }
  await loadSettings();
  showUpdate(state.update ?? await cachedUpdate());
  status.textContent = "入力設定を読み込みました。";
}
document.querySelector("#reload").addEventListener("click", () => show(async () => {
  await loadSettings(); status.textContent = "保存済みの入力設定へ戻しました。";
}));
document.querySelector("#settings-form").addEventListener("submit", event => {
  event.preventDefault();
  return show(async () => {
    const value = parseSettings();
    disableCore(true);
    try {
      const {snapshot} = await request({op: "snapshot"});
      const latest = JSON.parse(snapshot).settings;
      const hidden = Object.fromEntries(Object.entries(latest).filter(([key]) => desktopOnly.has(key)));
      await request({op: "configure", settings: JSON.stringify({...value, ...hidden})});
      await loadSettings();
    } finally { disableCore(false); }
    status.textContent = "設定を保存しました。";
  });
});
document.querySelector("#clear").addEventListener("click", () => show(async () => {
  if (!confirm("学習履歴を削除します。ユーザー辞書と設定は保持します。")) return;
  await request({op: "clear_learning"});
  status.textContent = "学習を削除しました。";
}));
document.querySelector("#export").addEventListener("click", () => show(async () => {
  const {snapshot} = await request({op: "snapshot"});
  const url = URL.createObjectURL(new Blob([snapshot], {type: "application/json"}));
  const link = document.createElement("a");
  link.href = url; link.download = "meltype-chromeos-snapshot.json"; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  status.textContent = "書き出しには個人の辞書・学習を含みます。";
}));
const consentGate = document.querySelector("#consent-gate");
document.querySelector("#consent-accept").addEventListener("click", async () => {
  await acceptAnalytics();
  consentGate.close();
  await track("options_open");
  show(reload);
});
document.querySelector("#consent-decline").addEventListener("click", () => window.close());
if (!globalThis.chrome?.storage?.local || await analyticsAccepted()) {
  if (globalThis.chrome?.storage?.local) await track("options_open");
  show(reload);
} else {
  consentGate.showModal();
}
updateCheck.addEventListener("click", () => show(async () => {
  if (!await hasUpdatePermission() && !await requestUpdatePermission()) throw new Error("更新の確認にはGitHubへの接続の許可が必要です。");
  updateCheck.disabled = true;
  updateStatus.textContent = "更新を確認しています…";
  try { showUpdate(await request({op: "update_check"})); }
  finally { updateCheck.disabled = false; }
}));

updateZip.addEventListener("click", () => show(async () => {
  if (!globalThis.showDirectoryPicker) throw new Error("このChromeではフォルダーを選べません。ZIPを展開して同じフォルダーへ置き換えてください。");
  // Pick the folder while the click's user activation is still valid.
  const directory = await globalThis.showDirectoryPicker({mode: "readwrite", id: "meltype-extension"});
  if (!await hasUpdatePermission()) throw new Error("先に「更新を確認」を押して、GitHubへの接続を許可してください。");
  updateZip.disabled = true;
  updateStatus.textContent = "更新ファイルを確認しています…";
  try {
    const update = await request({op: "update_check"});
    if (!update) throw new Error("更新を確認できませんでした。先に「更新を確認」を押してください。");
    if (!update.update) { updateStatus.textContent = "最新版です。"; return; }
    const files = await downloadUpdate(update);
    updateStatus.textContent = `v${update.version} を書き込んでいます…`;
    await writeUpdate(files, directory);
    updateStatus.textContent = `v${update.version} を書き込みました。「拡張機能を再読み込み」を押してください。`;
    reloadExtension.hidden = false;
  } finally { updateZip.disabled = false; }
}));
reloadExtension.addEventListener("click", () => chrome.runtime.reload());

aiInstall.addEventListener("click", async () => {
  if (!globalThis.confirm("shuntiの環境を作成してよいですか？ 辞書（CC BY-SA 4.0）・モデル（CC BY 4.0）を別ダウンロードします。入力内容は送信しません。")) return;
  // Request optional host access while the user's button gesture is active.
  const variant = parseSettings().ShuntiModelVariant ?? "shunti-v7-s8p";
  const packages = new ShuntiPackages({variant});
  const permission = chrome.permissions.request({origins:modelOrigins,permissions:["unlimitedStorage"]});
  disableCore(true); aiInstall.disabled = true; aiVersion.disabled = true;
  try {
    if (!await permission) throw new Error("ダウンロードが許可されませんでした");
    aiStatus.textContent = "AI環境を準備しています…";
    await packages.install();
    const value = parseSettings(); value.AiModel = "Shunti"; value.ShuntiModelVariant = variant; value.UseShunti = true;
    settings.value = JSON.stringify(value,null,2); displayFields(value);
    aiStatus.textContent = "インストールしました。「入力設定を保存」で反映します。";
  } catch (error) { aiStatus.textContent = String(error); }
  finally { disableCore(false); aiVersion.disabled = false; await refreshAI(parseSettings()); }
});
