// SPDX-License-Identifier: GPL-3.0-or-later
import {test} from "node:test";
import assert from "node:assert/strict";
import {readFileSync} from "node:fs";
import {initSync, BrowserSession} from "../pkg/meltype_browser.js";
initSync({module: readFileSync(new URL("../pkg/meltype_browser_bg.wasm", import.meta.url))});
const key = (s, vk, c = 0, mods = 0) => JSON.parse(s.key(vk, typeof c === "string" ? c.codePointAt(0) : c, mods, 100));
const type = (s, text) => { for (const c of text) key(s, c.toUpperCase().charCodeAt(0), c); };

test("selected shunti candidates pass through the real WASM core and reset rejects stale replies", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  const s = new BrowserSession(JSON.stringify({settings:{AiModel:"Shunti",UseShunti:true,LiveConversion:false}}));
  try {
    type(s,"kyouhaiitenkidesu"); key(s,32," ");
    const request=JSON.parse(s.ai_request());
    assert.equal(request[0],"きょうはいいてんきです");
    assert.equal(s.ai_apply(request[0],JSON.stringify(["今日は良い天気です"])),true);
    const view = JSON.parse(s.view()).view;
    assert.equal(view.notes[view.candidates.indexOf("今日は良い天気です")], "AI · shunti");
    assert.equal(key(s,13).commits[0].text,"今日は良い天気です");
    assert.equal(s.ai_apply(request[0],JSON.stringify(["古い応答"])),false);
    type(s,"nihongo"); key(s,32," "); s.reset();
    assert.equal(JSON.parse(s.ai_request()),null);
    assert.equal(s.ai_apply("にほんご",JSON.stringify(["古い応答"])),false);
  } finally { s.free(); }
});

test("1ドルの最新円換算が最初の候補ページに入る", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  BrowserSession.set_exchange_rates("# 日付: 2026-11-20\nEUR\t1\nUSD\t1.2\nJPY\t180");
  const session = new BrowserSession(JSON.stringify({settings:{CurrencyRateMode:"Auto", LiveConversion:false}}));
  try {
    type(session, "1doru");
    const {candidates} = key(session, 32, " ").view;
    assert(candidates.slice(0, 9).includes("≒150円"));
    assert(candidates.some(c => c.includes("2026/11/20")));
  } finally { session.free(); }
});

test("ChromeOSの同梱辞書で基本語と文を漢字変換し、Enterで確定できる", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  for (const [input, expected] of [["nihongo", "日本語"], ["watashi", "私"], ["toukyou", "東京"],
    ["kyouhaiitenkidesu", "今日はいい天気です"], ["watashihanihongowobenkyoushiteimasu", "私は日本語を勉強しています"],
    ["ashitatoukyouniikimasu", "明日東京に行きます"], ["googledekensaku", "googleで検索"],
    ["chatgptwotsukau", "chatgptを使う"], ["ChatGPTwotsukau", "ChatGPTを使う"]]) {
    const s = new BrowserSession("");
    try {
      type(s, input);
      const result = key(s, 32, " ");
      if (input === "googledekensaku") {
        // Candidates belong to the selected English clause; the complete
        // sentence remains visible and Enter confirms both clauses together.
        assert.deepEqual(result.view.clauses, ["google", "で検索"]);
        assert.deepEqual(result.view.candidates, ["google", "Google", "GOOGLE", "ｇｏｏｇｌｅ"]);
      } else if (input === "chatgptwotsukau" || input === "ChatGPTwotsukau") {
        const english = input === "chatgptwotsukau" ? "chatgpt" : "ChatGPT";
        assert.deepEqual(result.view.clauses, [english, "を使う"]);
        assert.equal(result.view.candidates[0], english);
      } else {
        assert(result.view.candidates.includes(expected), `${input}: ${JSON.stringify(result.view.candidates)}`);
      }
      assert.equal(result.view.text, expected, input);
      assert.equal(key(s, 13).commits[0].text, expected);
    } finally { s.free(); }
  }
  assert.throws(() => BrowserSession.load_dictionary(new Uint8Array([0, 1, 2])));
});

test("破損した辞書を拒否し、読み込み済みの漢字辞書と学習スナップショットを保つ", () => {
  const minimal = new Uint8Array(48);
  minimal.set(new TextEncoder().encode("MTKKC001"));
  const view = new DataView(minimal.buffer);
  for (const [at, value] of [[8, 1], [12, 1], [16, 1], [26, 1], [30, 1], [34, 1]]) view.setUint32(at, value, true);
  minimal.set([97, 98], 46);
  for (const [offset, value] of [[8, 0xffffffff], [12, 0xffffffff], [22, 0xffffffff], [26, 0xffffffff], [38, 1], [46, 255]]) {
    const corrupt = minimal.slice();
    if (offset === 38) new DataView(corrupt.buffer).setUint16(offset, value, true);
    else if (offset === 46) corrupt[offset] = value;
    else new DataView(corrupt.buffer).setUint32(offset, value, true);
    assert.throws(() => BrowserSession.load_dictionary(corrupt), String(offset));
  }
  const s = new BrowserSession("");
  try {
    type(s, "nihongo");
    assert.equal(key(s, 32, " ").view.text, "日本語");
    assert(!s.snapshot().includes("MTKKC001"));
    assert(s.snapshot().length < 100000);
  } finally { s.free(); }
});

test("website code mode passes source through and permits Japanese in comments and strings", () => {
  const s = new BrowserSession("");
  assert.equal(s.set_code_mode(true), true);
  for (const c of "const answer = 42;") {
    const result = key(s, c.toUpperCase().charCodeAt(0), c);
    assert.equal(result.consumed, false, c);
    assert.deepEqual(result.commits, []);
    assert.equal(result.view, null);
  }
  for (const before of ["// ", 'const message = "']) {
    s.focus(false); s.context(before, "");
    for (const c of "nihongo") assert.equal(key(s, c.toUpperCase().charCodeAt(0), c).consumed, true);
    assert.equal(JSON.parse(s.view()).view.text, "にほんご");
    assert.equal(key(s, 13).commits[0].text, "にほんご");
  }
  s.focus(false);
  assert.equal(s.set_code_mode(false), true);
  assert.equal(key(s, 65, "a").view.text, "あ");
  s.free();
});

test("website mode changes wait for composition and are never saved in the Core snapshot", () => {
  const s = new BrowserSession("");
  type(s, "nihongo");
  const before = s.view();
  assert.equal(s.set_code_mode(true), false);
  assert.equal(s.view(), before);
  assert.equal(key(s, 13).commits[0].text, "にほんご");
  assert.equal(s.set_code_mode(true), true);
  assert.equal(key(s, 65, "a").consumed, false);
  const restored = new BrowserSession(s.snapshot());
  assert.equal(key(restored, 65, "a").view.text, "あ");
  s.free(); restored.free();
});
test("real WASM shares composition, FST candidates, selection and durable learning", () => {
  const s = new BrowserSession("");
  type(s, "hashi");
  const view = key(s, 32, " ").view;
  assert(view.candidates.includes("橋"));
  const index = view.candidates.indexOf("箸");
  assert(index >= 0);
  assert.equal(JSON.parse(s.select(index)).commits[0].text, "箸");
  const restored = new BrowserSession(s.snapshot());
  type(restored, "hashi");
  assert.equal(key(restored, 32, " ").view.text, "箸");
  restored.clear_learning();
  assert.deepEqual(JSON.parse(restored.snapshot()).state.preferences.selected, {});
  s.free(); restored.free();
});

test("ChromeOSの読込・再設定・書出しではAI設定を除き、辞書変換と予測を保持する", () => {
  const settings = {UseZenzai: true, ZenzaiServerPath: "server", ZenzaiModelPath: "model",
    PredictiveCandidates: true, FutureSetting: {keep: true},
    Profiles: [{Name: "移行", Values: {UseZenzai: true, ZenzaiServerPath: "server", ZenzaiModelPath: "model", PredictiveCandidates: true}}]};
  const s = new BrowserSession(JSON.stringify({settings}));
  try {
    for (const configure of [false, true]) {
      if (configure) s.configure(JSON.stringify(settings));
      const saved = JSON.parse(s.snapshot()).settings;
      const profile = saved.Profiles.find(profile => profile.Name === "移行");
      for (const key of ["UseZenzai", "ZenzaiServerPath", "ZenzaiModelPath"]) {
        assert.equal(key in saved, false);
        assert.equal(key in profile.Values, false);
      }
      assert.equal(saved.PredictiveCandidates, true);
      assert.deepEqual(saved.FutureSetting, {keep: true});
      type(s, "hashi");
      assert(key(s, 32, " ").view.candidates.includes("橋"));
      key(s, 13);
    }
  } finally { s.free(); }
});
test("uncommitted text is not saved; reset and no-learning focus preserve privacy", () => {
  const s = new BrowserSession("");
  s.focus(false);
  type(s, "hashi");
  const snapshot = s.snapshot();
  assert(!snapshot.includes('"raw"'));
  const view = key(s, 32, " ").view;
  s.select(view.candidates.indexOf("箸"));
  assert.deepEqual(JSON.parse(s.snapshot()).state.preferences.selected, {});
  type(s, "nihongo"); s.reset();
  assert.equal(JSON.parse(s.view()).view, null);
  s.free();
});
test("configuration migration and unknown fields survive a WASM round trip", () => {
  const s = new BrowserSession("");
  s.configure(JSON.stringify({InputStyle: "Both", Mode: "AutoSwitch", futureField: {keep: true}}));
  const settings = JSON.parse(s.snapshot()).settings;
  assert.equal(settings.InputStyle, "Romaji");
  assert.notEqual(settings.Mode, "AutoSwitch");
  assert.deepEqual(settings.futureField, {keep: true});
  assert.throws(() => s.configure("{broken"));
  assert.throws(() => s.configure(JSON.stringify({CursorIntoBrackets: true})));
  assert.throws(() => s.key(65, 97, 0, NaN));
  assert.throws(() => new BrowserSession("{broken"));
  s.free();
});
test("F6/F7 and Enter use the actual shared Core", () => {
  const s = new BrowserSession("");
  type(s, "nihongo");
  assert.equal(key(s, 118).view.text, "ニホンゴ");
  assert.equal(key(s, 117).view.text, "にほんご");
  assert.equal(key(s, 13).commits[0].text, "にほんご");
  s.free();
});

test("real WASM exposes learned kanji predictions for browser candidate clicks", () => {
  const phrase = "橋の近くで待っています";
  const s = new BrowserSession(JSON.stringify({state: {phrases: [{
    reading: "はしのちかくでまっています", text: phrase, count: 3, used: Math.floor(Date.now() / 1000)
  }]}}));
  s.focus(false);
  type(s, "hashi");
  const view = JSON.parse(s.view()).view;
  const index = view.predictions.indexOf(phrase);
  assert(index >= 0);
  assert.equal(JSON.parse(s.select_prediction(index)).commits[0].text, phrase);
  assert.deepEqual(JSON.parse(s.snapshot()).state.preferences.selected, {});
  s.free();
});


test("ChatGPTを含む日英混在文は英語を保ち、日本語部分を漢字変換する", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  for (const [raw, expected] of [["chatgptwotsukau", "chatgptを使う"],
    ["chatgptwotsukatteimasu", "chatgptを使っています"],
    ["ChatGPTwotsukau", "ChatGPTを使う"],
    ["googledekensaku", "googleで検索"], ["githubwotsukau", "githubを使う"]]) {
    const s = new BrowserSession("");
    try {
      type(s, raw);
      const view = key(s, 32, " ").view;
      assert.equal(view.text, expected, raw);
      assert(view.clauses.length >= 2, raw);
      assert.equal(key(s, 13).commits[0].text, expected, raw);
    } finally { s.free(); }
  }
});

test("上流Open・Closed PRのチャット語句は同梱語句を優先して変換する", () => {
  for (const [raw, expected] of [["kyouhisashiburi", "今日久しぶり"], ["ryokai", "りょかい"],
    ["nigakki", "二学期"], ["ichigakki", "一学期"], ["sangakki", "三学期"],
    ["todorisuto", "todoリスト"], ["nasasou", "なさそう"], ["ninatta", "になった"],
    ["shiri-zu", "シリーズ"], ["doshitano", "どしたの"], ["shitanode", "したので"],
    ["kenritsu", "県立"], ["sukininatta", "好きになった"]]) {
    const s = new BrowserSession("");
    try {
      type(s, raw);
      assert.equal(key(s, 32, " ").view.text, expected, raw);
      assert.equal(key(s, 13).commits[0].text, expected, raw);
    } finally { s.free(); }
  }
});

test("同梱語句の分割は二を助詞のにへ戻さず、別の単語の学習で変えない", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  for (const [raw, expected] of [["nigakki", ["二", "学期"]],
    ["ichigakki", ["一", "学期"]], ["sangakki", ["三", "学期"]]]) {
    const s = new BrowserSession(JSON.stringify({state: {preferences: {selected: {
      "に": "荷", "いち": "市", "さん": "酸", "がっき": "楽器",
    }}}}));
    try {
      type(s, raw);
      const result = key(s, 32, " ");
      assert.deepEqual(result.view.clauses, expected, raw);
      assert.equal(result.view.text, expected.join(""), raw);
      const next = key(s, 39).view;
      assert.equal(next.selectedClause, 1);
      assert(next.candidates.includes("学期"));
      assert.equal(key(s, 13).commits[0].text, expected.join(""), raw);
    } finally { s.free(); }
  }
});

test("部首の説明で漢字を選び、通常の入力欄で確定できる", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  const s = new BrowserSession(JSON.stringify({settings: {LiveConversion: false}}));
  try {
    type(s, "gonbennnifurutori");
    const view = key(s, 32, " ").view;
    assert.equal(view.text, "誰");
    assert.ok(view.candidates.includes("讎"));
    assert.equal(key(s, 13).commits[0].text, "誰");
  } finally {
    s.free();
  }
});

test("同梱Mozc辞書で irekatawo を入れ方をとして確定する", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  const s = new BrowserSession("");
  try {
    type(s, "irekatawo");
    assert.equal(JSON.parse(s.view()).view.text, "いれかたを");
    assert.equal(key(s, 32, " ").view.text, "入れ方を");
    assert.equal(key(s, 13).commits[0].text, "入れ方を");
  } finally { s.free(); }
});

test("日本語の入れ方・入れますと英語のmeetingは打ち直しても正しく確定する", () => {
  BrowserSession.load_dictionary(readFileSync(new URL("../pkg/kanji.bin", import.meta.url)));
  for (const [mistyped, replacement, reading, expected] of [
    ["irekatawa", "wo", "いれかたを", "入れ方を"],
    ["iremasi", "su", "いれます", "入れます"],
    ["ashitanomeetingwa", "wo", "あしたのmeetingを", "明日のmeetingを"],
  ]) {
    const s = new BrowserSession("");
    try {
      type(s, mistyped); key(s, 8); type(s, replacement);
      assert.equal(JSON.parse(s.view()).view.text, reading);
      assert.equal(key(s, 32, " ").view.text, expected);
      assert.equal(key(s, 13).commits[0].text, expected);
    } finally { s.free(); }
  }
});

test("同梱辞書のSHA-256を照合し、変換中に読み込んだ辞書を適用できる", async () => {
  const bytes = readFileSync(new URL("../pkg/kanji.bin", import.meta.url));
  const {createHash} = await import("node:crypto");
  assert.equal(BrowserSession.dictionary_integrity(), "sha256-" + createHash("sha256").update(bytes).digest("base64"));
  const s = new BrowserSession("");
  try {
    assert.equal(s.apply_dictionary(), false);
    type(s, "iremasu"); key(s, 32, " ");
    BrowserSession.load_verified_dictionary(bytes);
    assert.equal(s.apply_dictionary(), true);
    assert.equal(key(s, 13).commits[0].text, "入れます");
  } finally { s.free(); }
});

test("検索・チャット・URL入力欄で確定は一度だけ行い、半角／全角で往復できる", async () => {
  const {ImeController} = await import("../controller.mjs");
  const s = new BrowserSession(JSON.stringify({settings: {DirectModeAutoDetect: false}}));
  const commits = [], errors = [];
  const ime = Object.fromEntries(["setComposition", "clearComposition", "setCandidates",
    "setCandidateWindowProperties", "setCursorPosition"].map(name => [name, async () => true]));
  ime.commitText = async data => { commits.push([data.contextID, data.text]); return true; };
  ime.keyEventHandled = () => {};
  const c = new ImeController({ime, load: async () => s, persist: async () => {}, report: e => errors.push(e)});
  let seq = 0;
  const send = async (key, code, type = "keydown") => {
    c.key("meltype-rust", {key, code, type}, String(++seq)); await c.queue;
  };
  try {
    c.activate("meltype-rust");
    for (const [id, field] of ["search", "text", "url"].entries()) {
      c.focus({contextID: id + 1, type: field});
      for (const ch of "meetingwo") await send(ch, "Key" + ch.toUpperCase());
      await send(" ", "Space"); await send("Enter", "Enter");
      for (const direct of [true, false]) {
        await send("ZenkakuHankaku", "Backquote");
        assert.equal(s.is_direct(), direct, field);
        await send("Unidentified", "", "keyup");
      }
    }
    assert.deepEqual(commits, [[1, "meetingを"], [2, "meetingを"], [3, "meetingを"]]);
    assert.deepEqual(errors, []);
  } finally { s.free(); }
});
