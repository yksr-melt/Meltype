// SPDX-License-Identifier: GPL-3.0-or-later
import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
function event() {
  return {listeners: [], addListener(callback) {this.listeners.push(callback);}};
}
function baseChrome() {
  return {runtime: {id: "test-id", getURL: path => `chrome-extension://test-id/${path}`,
    onMessage: event(), openOptionsPage() {}}, action: {onClicked: event()},
    storage: {local: {get: async () => ({}), set: async () => {}}}};
}
test("real service-worker module exposes an actionable status when IME API is absent", async () => {
  globalThis.chrome = baseChrome();
  await import("../background.mjs?unavailable");
  assert.equal(chrome.action.onClicked.listeners.length, 1);
  const message = chrome.runtime.onMessage.listeners[0];
  assert.equal(message({op: "status"}, {id: "foreign", url: "https://example.com/"}, () => assert.fail()), false);
  const reply = await new Promise(resolve => {
    assert.equal(message({op: "status"}, {id: chrome.runtime.id, url: chrome.runtime.getURL("options.html")}, resolve), true);
  });
  assert.equal(reply.available, false);
  assert(reply.diagnostic.includes("chrome.input.ime"));
  const sender = {id: chrome.runtime.id, url: chrome.runtime.getURL("options.html")};
  const preferences = await new Promise(resolve => message({op: "site_preferences"}, sender, resolve));
  assert.equal(preferences.preferences.enabled, false);
  const saved = await new Promise(resolve => message({op: "save_site_preferences", preferences: {...preferences.preferences, defaultMode: "code"}}, sender, resolve));
  assert.equal(saved.preferences.defaultMode, "code");
  assert.equal(message({op: "save_site_preferences", preferences: saved.preferences}, {id: chrome.runtime.id, url: "https://vscode.dev/"}, () => assert.fail()), false);
});
test("real service-worker listeners register before hydration and route keys into actual WASM", {timeout: 10000}, async () => {
  globalThis.chrome = baseChrome();
  const ime = {};
  for (const name of ["onActivate", "onDeactivated", "onFocus", "onInputContextUpdate", "onBlur",
    "onReset", "onKeyEvent", "onCandidateClicked", "onSurroundingTextChanged"]) ime[name] = event();
  const calls = [];
  for (const name of ["setComposition", "clearComposition", "commitText", "setCandidates",
    "setCandidateWindowProperties", "setCursorPosition"]) ime[name] = async args => {calls.push([name, args]); return true;};
  chrome.input = {ime};
  chrome.permissions = {contains: async () => true, onAdded: event(), onRemoved: event()};
  chrome.windows = {getLastFocused: async () => ({id: 1, focused: true, type: "normal"}), onFocusChanged: event()};
  let activeUrl = "https://vscode.dev/project";
  chrome.tabs = {query: async () => [{active: true, url: activeUrl}], onActivated: event(), onUpdated: event(), onRemoved: event()};
  const reads = [];
  const stored = {};
  chrome.storage.local.get = async key => {reads.push(key); return {[key]: stored[key]};};
  chrome.storage.local.set = async value => {Object.assign(stored, value);};
  const originalFetch = globalThis.fetch;
  let rateQueries = 0;
  globalThis.fetch = async url => {
    if (url === "https://api.coinbase.com/v2/exchange-rates?currency=EUR") {
      rateQueries++;
      return Response.json({data:{currency:"EUR",rates:{USD:"1.2",JPY:"180"}}});
    }
    const file = url === chrome.runtime.getURL("pkg/kanji.bin") ? "kanji.bin" : "meltype_browser_bg.wasm";
    assert.equal(url, chrome.runtime.getURL(`pkg/${file}`));
    return new Response(readFileSync(new URL(`../pkg/${file}`, import.meta.url)), {headers: {"Content-Type": file.endsWith("wasm") ? "application/wasm" : "application/octet-stream"}});
  };
  try {
    await import("../background.mjs?available");
    assert.deepEqual(reads, []);
    assert.equal(ime.onKeyEvent.listeners.length, 1);
    ime.onActivate.listeners[0]("meltype-rust");
    ime.onFocus.listeners[0]({contextID: 5, type: "text", shouldDoLearning: false});
    const acknowledged = new Promise(resolve => {ime.keyEventHandled = (id, handled) => resolve({id, handled});});
    const result = ime.onKeyEvent.listeners[0]("meltype-rust", {type: "keydown", key: "a", code: "KeyA"}, "first-key");
    assert.equal(result, undefined);
    assert.deepEqual(await acknowledged, {id: "first-key", handled: true});
    assert.deepEqual(reads.slice().sort(), ["analyticsConsentVersion", "sitePreferences", "snapshot"]);
    assert.equal(calls.find(([name]) => name === "setComposition")[1].text, "あ");
    // Legacy ChromeOS places requestId in keyData, with no third argument.
    // Exercise the complete real-WASM -> candidate window -> commit route.
    ime.onReset.listeners[0]("meltype-rust");
    async function legacyKey(key, code, id) {
      const ack = new Promise(resolve => {ime.keyEventHandled = (request, handled) => resolve({request, handled});});
      assert.equal(ime.onKeyEvent.listeners[0]("meltype-rust", {type: "keydown", key, code, requestId: id}), undefined);
      assert.deepEqual(await ack, {request: id, handled: true});
    }
    for (const [index, character] of [..."hashi"].entries()) {
      await legacyKey(character, `Key${character.toUpperCase()}`, `legacy-${index}`);
      ime.onInputContextUpdate.listeners[0]({contextID: 5, type: "text", shouldDoLearning: false});
    }
    await legacyKey(" ", "Space", "legacy-convert");
    const candidates = calls.filter(([name]) => name === "setCandidates").at(-1)[1].candidates;
    assert(candidates.some(row => row.candidate === "橋"));
    const chopsticks = candidates.find(row => row.candidate === "箸");
    assert(chopsticks);
    const committed = new Promise(resolve => {ime.commitText = async args => {resolve(args); return true;};});
    ime.onCandidateClicked.listeners[0]("meltype-rust", chopsticks.id, "left");
    assert.deepEqual(await committed, {contextID: 5, text: "箸"});
    // 基本語は補助辞書にない。実際の同梱辞書を通して確定まで検査する。
    ime.onReset.listeners[0]("meltype-rust");
    for (const [index, character] of [..."nihongo"].entries()) {
      await legacyKey(character, `Key${character.toUpperCase()}`, `kanji-${index}`);
    }
    await legacyKey(" ", "Space", "kanji-space");
    assert.equal(calls.filter(([name]) => name === "setComposition").at(-1)[1].text, "日本語");
    const kanjiCommit = new Promise(resolve => {ime.commitText = async args => {resolve(args); return true;};});
    await legacyKey("Enter", "Enter", "kanji-enter");
    assert.deepEqual(await kanjiCommit, {contextID: 5, text: "日本語"});
    const message = chrome.runtime.onMessage.listeners[0];
    const send = data => new Promise(resolve => message(data, {id: chrome.runtime.id, url: chrome.runtime.getURL("options.html")}, resolve));
    await send({op: "configure", settings: JSON.stringify({CurrencyRateMode:"Auto", LiveConversion:false})});
    for (const [i, char] of [..."1doru"].entries()) await legacyKey(char, i === 0 ? "Digit1" : "Key" + char.toUpperCase(), "fx" + i);
    await legacyKey(" ", "Space", "fx-space");
    const fx = calls.filter(([name]) => name === "setCandidates").at(-1)[1].candidates;
    assert(fx.slice(0, 9).some(row => row.candidate === "≒150円"));
    assert.equal(rateQueries, 1);
    ime.onReset.listeners[0]("meltype-rust");
    const {preferences} = await send({op: "site_preferences"});
    assert.equal((await send({op: "save_site_preferences", preferences: {...preferences, enabled: true}})).preferences.enabled, true);
    ime.onReset.listeners[0]("meltype-rust");
    const codeAck = new Promise(resolve => {ime.keyEventHandled = (id, handled) => resolve({id, handled});});
    ime.onKeyEvent.listeners[0]("meltype-rust", {type: "keydown", key: "a", code: "KeyA"}, "code");
    assert.deepEqual(await codeAck, {id: "code", handled: false});
    ime.onSurroundingTextChanged.listeners[0]("meltype-rust", {text: "// ", focus: 3, anchor: 3});
    for (const [index, character] of [..."nihongo"].entries()) await legacyKey(character, `Key${character.toUpperCase()}`, `comment-${index}`);
    assert.equal(calls.filter(([name]) => name === "setComposition").at(-1)[1].text, "にほんご");
    // Tab updates refresh policy without clearing the existing composition.
    activeUrl = "https://example.com/";
    chrome.tabs.onUpdated.listeners[0](1, {url: activeUrl});
    await legacyKey("Enter", "Enter", "comment-confirm");
    assert.equal(calls.filter(([name]) => name === "setComposition").at(-1)[1].text, "にほんご");
    ime.onReset.listeners[0]("meltype-rust");
    await legacyKey("a", "KeyA", "normal-after-navigation");
    assert.equal(calls.filter(([name]) => name === "setComposition").at(-1)[1].text, "あ");
  } finally { globalThis.fetch = originalFetch; }
});
