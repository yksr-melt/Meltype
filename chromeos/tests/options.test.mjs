// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import {ShuntiPackages} from "../shunti.mjs";

// 設定画面の実モジュールを、DOMとChrome通信の最小限の代替で動かす。
class Element {
  value = ""; checked = false; disabled = false; textContent = "";
  children = []; options = []; dataset = {}; listeners = new Map(); attributes = new Map();
  hidden = false; tabIndex = 0; id = "";
  addEventListener(type, callback) { this.listeners.set(type, callback); }
  append(...items) { this.children.push(...items); this.options.push(...items); }
  replaceChildren(...items) { this.children = items; }
  setAttribute(name, value) { this.attributes.set(name, value); }
  getAttribute(name) { return this.attributes.get(name); }
  focus() { document.activeElement = this; }
  async fire(type, details = {}) { await this.listeners.get(type)?.({preventDefault() {}, ...details}); }
}
let serial = 0;
async function page(t, {available = true} = {}) {
  const oldDocument = globalThis.document, oldChrome = globalThis.chrome;
  t.after(() => { globalThis.document = oldDocument; globalThis.chrome = oldChrome; });
  const html = readFileSync(new URL("../options.html", import.meta.url), "utf8");
  const elements = new Map([...html.matchAll(/<[a-z][\w-]*\b([^>]*\bid="([^"]+)"[^>]*)>/g)].map(([, attrs, id]) => {
    const element = new Element(); element.id = id;
    for (const [, name, value] of attrs.matchAll(/([\w-]+)="([^"]*)"/g)) {
      element.setAttribute(name, value);
      if (name.startsWith("data-")) element.dataset[name.slice(5)] = value;
    }
    element.checked = /\schecked(?:\s|$)/.test(attrs);
    element.disabled = /\sdisabled(?:\s|$)/.test(attrs);
    element.hidden = /\shidden(?:\s|$)/.test(attrs);
    return [`#${id}`, element];
  }));
  const fields = [...html.matchAll(/<(input|select)\b([^>]*\bdata-setting="([^"]+)"[^>]*)>/g)].map(([, tag, attrs, key]) => {
    const id = attrs.match(/\bid="([^"]+)"/)[1];
    const field = elements.get(`#${id}`); field.dataset.setting = key;
    // 実際の type をそのまま使う (checkbox・number・text)
    field.type = tag === "select" ? "select-one" : (attrs.match(/\btype="([^"]+)"/)?.[1] ?? "text");
    if (tag !== "select") delete field.options;
    if (tag === "select") {
      const contents = html.slice(html.indexOf(`<select${attrs}>`) + `<select${attrs}>`.length).split("</select>")[0];
      field.options = [...contents.matchAll(/value="([^"]+)"/g)].map(([, value]) => ({value}));
    }
    return field;
  });
  elements.get("#core-controls").disabled = true;
  globalThis.document = {
    querySelector: selector => { assert(elements.has(selector), selector); return elements.get(selector); },
    querySelectorAll: selector => {
      if (selector === "[data-setting]") return fields;
      if (selector === '[role="tab"]') return [...elements.values()].filter(element => element.getAttribute("role") === "tab");
      if (selector === '[role="tabpanel"]') return [...elements.values()].filter(element => element.getAttribute("role") === "tabpanel");
      if (selector === "[data-actions]") return [...elements.values()].filter(element => element.dataset.actions);
      if (selector === "[data-help]") return [...elements.values()].filter(element => element.dataset.help);
      assert.equal(selector, "#export, #clear");
      return [elements.get("#export"), elements.get("#clear")];
    },
    createElement: () => new Element(),
  };
  let saved = {...JSON.parse(readFileSync(new URL("../../rust/core/data/settings-defaults.json", import.meta.url))),
    FutureOption: {keep: true}, AppProfile: "General"};
  const writes = [];
  let failure;
  globalThis.chrome = {runtime: {sendMessage: async message => {
    if (message.op === "site_preferences") return {preferences: {enabled: false, defaultMode: "normal", rules: []}};
    if (message.op === "status") return {available, diagnostic: available ? "ready" : "unavailable"};
    if (message.op === "snapshot") return {snapshot: JSON.stringify({settings: saved})};
    if (message.op === "configure") {
      if (failure) return {error: failure};
      saved = JSON.parse(message.settings); writes.push(structuredClone(saved)); return {saved: true};
    }
    assert.fail(message.op);
  }}};
  await import(`../options.mjs?test=${serial++}`);
  await new Promise(resolve => setImmediate(resolve));
  return {elements, fields: Object.fromEntries(fields.map(field => [field.dataset.setting, field])), writes,
    saved: () => saved, fail: message => { failure = message; }};
}

test("入力設定と詳細JSONを同期し、未知の設定・非表示の設定を保存後も保持する", async t => {
  const p = await page(t), get = id => p.elements.get(id);
  assert.equal(Object.keys(p.fields).length, 10);
  assert.equal(p.fields.AiModel.value, "None");
  assert.equal(get("#ai-details").hidden, true);
  assert.deepEqual(p.fields.AiModel.options.map(item => item.value), ["None", "Shunti"]);
  assert.equal(get("#core-controls").disabled, false);
  assert.equal(p.fields.DetectionLevel.value, "Balanced");
  assert.equal(p.fields.LearningEnabled.checked, true);
  // 通貨の換算: ChromeOS は固定と手入力、それに小数の桁
  assert.equal(p.fields.CurrencyRateMode.value, "Fixed");
  assert.deepEqual(p.fields.CurrencyRateMode.options.map(item => item.value), ["Fixed", "Auto", "Manual"]);
  p.fields.CurrencyDecimals.value = "2";
  await p.fields.CurrencyDecimals.fire("change");
  assert.equal(JSON.parse(get("#settings").value).CurrencyDecimals, 2, "小数の桁は数で持つ");
  p.fields.CurrencyRateMode.value = "Manual";
  await p.fields.CurrencyRateMode.fire("change");
  assert.equal(JSON.parse(get("#settings").value).CurrencyRateMode, "Manual");
  p.fields.CurrencyManualRates.value = "USD = 150";
  await p.fields.CurrencyManualRates.fire("change");
  p.fields.TabConversion.checked = false;
  await p.fields.TabConversion.fire("change");
  p.fields.Punctuation.value = "FullWidthCommaPeriod";
  await p.fields.Punctuation.fire("change");
  assert.equal(JSON.parse(get("#settings").value).TabConversion, false);
  assert.equal(p.writes.length, 0);
  const json = JSON.parse(get("#settings").value);
  json.DetectionLevel = "Conservative";
  json.LearningEnabled = false;
  json.AppProfile = "do not replace hidden settings";
  get("#settings").value = JSON.stringify(json);
  await get("#settings").fire("input");
  assert.equal(p.fields.DetectionLevel.value, "Conservative");
  assert.equal(p.fields.LearningEnabled.checked, false);
  // 画面を開いた後に変更された非表示設定も、保存直前の値を使う。
  p.saved().AppProfile = "Code";
  await get("#settings-form").fire("submit");
  assert.equal(p.writes.length, 1);
  assert.equal(p.saved().AppProfile, "Code");
  assert.deepEqual(p.saved().FutureOption, {keep: true});
  assert.equal(p.saved().Punctuation, "FullWidthCommaPeriod");
  assert.equal(p.saved().TabConversion, false);
  assert.equal(p.saved().CurrencyManualRates, "USD = 150");
  assert.equal("AppProfile" in JSON.parse(get("#settings").value), false);
  p.fields.DetectionLevel.value = "Aggressive";
  await p.fields.DetectionLevel.fire("change");
  await get("#reload").fire("click");
  assert.equal(p.fields.DetectionLevel.value, "Conservative");
  assert.equal(p.fields.LearningEnabled.checked, false);
});

test("不正なJSONと保存エラーで設定を失わず、再試行できる", async t => {
  const p = await page(t), get = id => p.elements.get(id);
  for (const invalid of ["{", "[]", "null"]) {
    get("#settings").value = invalid;
    await get("#settings-form").fire("submit");
    assert.equal(p.writes.length, 0);
    assert.equal(get("#core-controls").disabled, false);
    assert.notEqual(get("#status").textContent, "設定を保存しました。");
  }
  await get("#reload").fire("click");
  p.fields.CorrectTypos.checked = false;
  await p.fields.CorrectTypos.fire("change");
  p.fail("storage failed");
  await get("#settings-form").fire("submit");
  assert.match(get("#status").textContent, /storage failed/);
  assert.equal(JSON.parse(get("#settings").value).CorrectTypos, false);
  assert.equal(get("#core-controls").disabled, false);
  p.fail(undefined);
  await get("#settings-form").fire("submit");
  assert.equal(p.saved().CorrectTypos, false);
});

test("IMEが利用できない環境では入力設定と学習の操作を無効にする", async t => {
  const p = await page(t, {available: false});
  for (const id of ["#core-controls", "#save-settings", "#reload", "#export", "#clear"]) assert.equal(p.elements.get(id).disabled, true);
  assert.equal(p.elements.get("#site-enabled").disabled, false);
  assert.equal(p.writes.length, 0);
});

test("ページを切り替えても編集中の設定を保持し、キーボードでも各ページへ移動する", async t => {
  const p = await page(t), get = id => p.elements.get(id);
  p.fields.CorrectTypos.checked = false;
  await p.fields.CorrectTypos.fire("change");
  const draft = get("#settings").value;
  await get("#tab-sites").fire("click");
  assert.equal(get("#panel-settings").hidden, true);
  assert.equal(get("#panel-sites").hidden, false);
  assert.equal(get("#settings-actions").hidden, true);
  assert.equal(get("#site-actions").hidden, false);
  assert.equal(get("#tab-sites").getAttribute("aria-selected"), "true");
  assert.equal(get("#tab-settings").tabIndex, -1);
  await get("#tab-sites").fire("keydown", {key: "ArrowRight"});
  assert.equal(document.activeElement, get("#tab-data"));
  assert.equal(get("#panel-data").hidden, false);
  await get("#tab-data").fire("keydown", {key: "End"});
  assert.equal(document.activeElement, get("#tab-help"));
  assert.equal(get("#panel-help").hidden, false);
  await get("#tab-help").fire("keydown", {key: "ArrowRight"});
  assert.equal(document.activeElement, get("#tab-settings"));
  await get("#tab-settings").fire("keydown", {key: "ArrowLeft"});
  assert.equal(document.activeElement, get("#tab-help"));
  await get("#tab-help").fire("keydown", {key: "Home"});
  assert.equal(document.activeElement, get("#tab-settings"));
  assert.equal(get("#panel-settings").hidden, false);
  assert.equal(get("#settings-actions").hidden, false);
  assert.equal(get("#settings").value, draft);
  assert.equal(p.writes.length, 0);
  await get("#settings-form").fire("submit");
  assert.equal(p.saved().CorrectTypos, false);
});

test("下部の説明表示と項目の説明は、設定データを保存せず切り替えられる", async t => {
  const p = await page(t), get = id => p.elements.get(id);
  const draft = get("#settings").value;
  await p.fields.TabConversion.fire("focus");
  assert.match(get("#context-help").textContent, /予測候補/);
  assert.equal(p.fields.TabConversion.getAttribute("aria-describedby"), "context-help");
  get("#show-help").checked = false;
  await get("#show-help").fire("change");
  assert.equal(get("#help-panel").hidden, true);
  get("#show-help").checked = true;
  await get("#show-help").fire("change");
  assert.equal(get("#help-panel").hidden, false);
  await p.fields.LearningEnabled.fire("mouseenter");
  assert.match(get("#context-help").textContent, /履歴は残ります/);
  assert.equal(get("#settings").value, draft);
  assert.equal(p.writes.length, 0);
});

test("AI変換の設定は詳細JSONから除き、通常の予測と未知の設定は保持する", async t => {
  const p = await page(t), get = id => p.elements.get(id);
  Object.assign(p.saved(), {UseZenzai: true, ZenzaiServerPath: "server", ZenzaiModelPath: "model"});
  p.saved().Profiles = [{Name: "移行", Values: {UseZenzai: true, ZenzaiModelPath: "model", PredictiveCandidates: false}}];
  await get("#reload").fire("click");
  const displayed = JSON.parse(get("#settings").value);
  assert.equal("UseZenzai" in displayed, false);
  assert.equal("ZenzaiModelPath" in displayed.Profiles[0].Values, false);
  Object.assign(displayed, {UseZenzai: true, ZenzaiServerPath: "server", ZenzaiModelPath: "model"});
  displayed.Profiles[0].Values.UseZenzai = true;
  get("#settings").value = JSON.stringify(displayed);
  await get("#settings-form").fire("submit");
  for (const key of ["UseZenzai", "ZenzaiServerPath", "ZenzaiModelPath"]) {
    assert.equal(key in p.saved(), false);
    assert.equal(key in p.saved().Profiles[0].Values, false);
  }
  assert.equal(p.saved().Profiles[0].Values.PredictiveCandidates, false);
  assert.deepEqual(p.saved().FutureOption, {keep: true});
});


test("モデルの導入後はインストール済みへ変わり、別の版は未導入として選択できる",async t=>{
  const fetcher=globalThis.fetch, confirm=globalThis.confirm;
  const installed=ShuntiPackages.prototype.installed, install=ShuntiPackages.prototype.install;
  const versions=new Set();
  t.after(()=>{globalThis.fetch=fetcher;globalThis.confirm=confirm;ShuntiPackages.prototype.installed=installed;ShuntiPackages.prototype.install=install;});
  globalThis.fetch=async()=>new Response(readFileSync(new URL("../../rust/ai/model-catalog.json",import.meta.url)));
  globalThis.confirm=()=>true;
  ShuntiPackages.prototype.installed=async function(){return versions.has(this.variant);};
  ShuntiPackages.prototype.install=async function(){versions.add(this.variant);};
  const p=await page(t), get=id=>p.elements.get(id);
  chrome.permissions={request:async()=>true};
  p.fields.AiModel.value="Shunti"; await p.fields.AiModel.fire("change");
  assert.equal(get("#ai-version").options.length,6);
  assert.equal(get("#ai-install").disabled,false);
  chrome.permissions.request = async () => false;
  await get("#ai-install").fire("click");
  assert.match(get("#ai-status").textContent, /許可されませんでした/);
  assert.equal(get("#ai-install").disabled,false);
  chrome.permissions.request = async () => true;
  await get("#ai-install").fire("click");
  assert.equal(get("#ai-install").textContent,"インストール済み");
  assert.equal(get("#ai-install").disabled,true);
  get("#ai-version").value="shunti-v6-xs5";await get("#ai-version").fire("change");
  assert.equal(get("#ai-install").disabled,false);
  assert.equal(JSON.parse(get("#settings").value).UseShunti,false);
  get("#ai-version").value="shunti-v7-s8p";await get("#ai-version").fire("change");
  assert.equal(get("#ai-install").textContent,"インストール済み");
  assert.equal(JSON.parse(get("#settings").value).UseShunti,true);
  assert.equal(versions.size,1);
});
