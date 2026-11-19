// SPDX-License-Identifier: GPL-3.0-or-later
import init, {BrowserSession} from "./pkg/meltype_browser.js";
import {ImeController} from "./controller.mjs";
import {SiteProfiles} from "./site-profiles.mjs";
import {ShuntiProxy} from "./shunti.mjs";
import {cachedUpdate,checkForUpdate,hasUpdatePermission} from "./update.mjs";
import {track} from "./analytics.mjs";
let shunti, aiEpoch = 0, aiLifecycle = Promise.resolve();
function prepareAI(core) {
  const epoch=++aiEpoch, previous=shunti;
  const settings = JSON.parse(core.snapshot()).settings;
  const selected=settings.UseShunti === true && settings.AiModel !== "None";
  shunti=selected ? new ShuntiProxy(settings.ShuntiModelVariant ?? "shunti-v7-s8p") : null;
  const next=shunti;
  aiLifecycle=aiLifecycle.catch(()=>{}).then(async()=>{
    await previous?.dispose();
    if (epoch !== aiEpoch || !next) return;
    try { await next.prepare(); } catch (error) { if (epoch === aiEpoch) report(`shunti: ${error}`); }
  });
}
const profiles = new SiteProfiles(chrome);
const available = Boolean(chrome.input?.ime?.onKeyEvent);
let diagnostic = available ? "Rust Coreの起動待ち" : "この環境ではchrome.input.imeを利用できません。ChromeOSの対応状況・管理ポリシーを確認してください。";
const report = message => { diagnostic = message; console.warn("Meltype:", message); };
const controller = available ? new ImeController({
  ime: chrome.input.ime, report, incognito: chrome.extension?.inIncognitoContext === true,
  resolveMode: context => profiles.resolve(context),
  convert: async ([reading, context]) => {
    const model = shunti;
    if (!model?.ready) return [];
    const candidates = await model.convert(reading, context);
    return model === shunti ? candidates : [];
  },
  loadDictionary: async () => {
    const dictionary = await fetch(chrome.runtime.getURL("pkg/kanji.bin"), {integrity: BrowserSession.dictionary_integrity()});
    if (!dictionary.ok) throw new Error("漢字辞書を読み込めません。拡張機能のフォルダーを全体更新してください。");
    BrowserSession.load_verified_dictionary(new Uint8Array(await dictionary.arrayBuffer()));
  },
  load: async () => {
    const [saved] = await Promise.all([
      chrome.storage.local.get("snapshot"),
      init({module_or_path: chrome.runtime.getURL("pkg/meltype_browser_bg.wasm")}),
    ]);
    // Invalid data is preserved. Never silently replace it with defaults.
    const core = new BrowserSession(saved.snapshot ?? "");
    prepareAI(core);
    let focusedGeneration = -1;
    core.focusIfNeeded = (generation, learning) => {
      if (focusedGeneration !== generation) { core.focus(learning); focusedGeneration = generation; }
    };
    diagnostic = "Rust Core起動済み。ChromeOSの入力方法にMeltype-FLASH（日本語配列）またはMeltype-FLASH（US配列）を追加してください。";
    return core;
  },
  persist: snapshot => chrome.storage.local.set({snapshot}),
}) : null;
const refreshMode = () => controller?.refreshMode();
chrome.tabs?.onActivated?.addListener(refreshMode);
chrome.tabs?.onRemoved?.addListener(refreshMode);
chrome.tabs?.onUpdated?.addListener((_id, change) => {
  if (change.url !== undefined || change.status !== undefined) refreshMode();
});
chrome.windows?.onFocusChanged?.addListener(refreshMode);
chrome.permissions?.onAdded?.addListener(refreshMode);
chrome.permissions?.onRemoved?.addListener(refreshMode);
chrome.storage.onChanged?.addListener((changes, area) => {
  if (area === "local" && changes.sitePreferences) { profiles.invalidate(); refreshMode(); }
});
if (controller) {
  const ime = chrome.input.ime;
  let trackedReady = false;
  ime.onActivate.addListener(engine => {
    controller.activate(engine);
    // 入力方法を選んだ時点で準備し、最初の文字に辞書の読み込みを集中させない。
    controller.ready().then(() => {
      if (!trackedReady) {
        trackedReady = true;
        track("keyboard_ready");
      }
    }).catch(error => report(String(error)));
  });
  ime.onDeactivated.addListener(engine => controller.deactivate(engine));
  ime.onFocus.addListener(context => controller.focus(context));
  ime.onInputContextUpdate.addListener(context => controller.updateContext(context));
  ime.onBlur.addListener(id => controller.blur(id));
  ime.onReset.addListener(engine => { if (controller.engine === engine) controller.invalidate(); });
  ime.onKeyEvent.addListener((engine, event, requestId) => controller.key(engine, event, requestId));
  ime.onCandidateClicked.addListener((engine, id, button) => controller.candidate(engine, id, button));
  ime.onSurroundingTextChanged.addListener((engine, info) => {
    const generation = controller.generation, id = controller.context?.contextID;
    controller.enqueue(async () => {
      const core = await controller.ready();
      if (!controller.valid(engine, id, generation) || id === undefined) return;
      if (!Number.isInteger(info.focus) || info.focus !== info.anchor || info.focus < 0 || info.focus > info.text.length) return;
      core.focusIfNeeded(generation, controller.learning());
      core.context(info.text.slice(0, info.focus), info.text.slice(info.focus));
    });
  });
}
chrome.action.onClicked.addListener(() => chrome.runtime.openOptionsPage());
const UPDATE_ALARM = "meltype-update-check";
async function checkUpdate() {
  if (!await hasUpdatePermission()) return null;
  try {
    const update = await checkForUpdate();
    await chrome.storage.local.set({updateCheck: update});
    await chrome.action.setBadgeText({text: update.update ? "新" : ""});
    await chrome.action.setBadgeBackgroundColor({color: "#d97706"});
    return update;
  } catch (error) {
    report(`更新の確認: ${error}`);
    return null;
  }
}
async function scheduleUpdateCheck() {
  if (!chrome.alarms) return;
  const existing = await chrome.alarms.get(UPDATE_ALARM);
  if (!existing) await chrome.alarms.create(UPDATE_ALARM, {periodInMinutes: 24 * 60, delayInMinutes: 1});
  await checkUpdate();
}
chrome.runtime.onInstalled?.addListener(() => { scheduleUpdateCheck().catch(error => report(String(error))); });
chrome.runtime.onStartup?.addListener(() => { scheduleUpdateCheck().catch(error => report(String(error))); });
chrome.alarms?.onAlarm.addListener(alarm => { if (alarm.name === UPDATE_ALARM) checkUpdate(); });
chrome.permissions?.onAdded?.addListener(details => {
  if ((details.origins ?? []).some(origin => origin.startsWith("https://api.github.com"))) checkUpdate();
});
chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== chrome.runtime.id || sender.url !== chrome.runtime.getURL("options.html")) return false;
  (async () => {
    if (message.op === "status") return {available, diagnostic, update: await cachedUpdate()};
    if (message.op === "update_check") return await checkUpdate();
    if (message.op === "update_cached") return await cachedUpdate();
    if (message.op === "site_preferences") return {preferences: await profiles.preferences()};
    if (message.op === "save_site_preferences") {
      const preferences = await profiles.save(message.preferences);
      refreshMode();
      return {preferences};
    }
    if (!controller) throw new Error(diagnostic);
    return controller.enqueue(async () => {
      const core = await controller.ready();
      if (message.op === "snapshot") return {snapshot: core.snapshot()};
      if (message.op === "configure") {
        core.configure(message.settings);
        prepareAI(core);
        controller.invalidate();
      } else if (message.op === "clear_learning") core.clear_learning();
      else throw new Error("Unknown command");
      await chrome.storage.local.set({snapshot: core.snapshot()});
      return {saved: true};
    });
  })().then(respond, error => respond({error: String(error)}));
  return true;
});
