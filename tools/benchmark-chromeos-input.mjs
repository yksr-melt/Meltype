// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
// node --expose-gc tools/benchmark-chromeos-input.mjs <unpacked extension> [inter-key delay in ms]
import assert from "node:assert/strict";
import {subtle} from "node:crypto";
import {readFile} from "node:fs/promises";
import {join, resolve} from "node:path";
import {pathToFileURL} from "node:url";
import {setTimeout as delay} from "node:timers/promises";

const root = resolve(process.argv[2] ?? "chromeos");
const spacing = Number(process.argv[3] ?? 65);
if (!Number.isFinite(spacing) || spacing < 0) throw new Error("Invalid inter-key delay");
const moduleAt = file => import(pathToFileURL(join(root, file)));
const {initSync, BrowserSession} = await moduleAt("pkg/meltype_browser.js");
const {ImeController} = await moduleAt("controller.mjs");
const commits = [], errors = [], timings = [];
// Include the real controller queue and WASM. Chrome callbacks and local storage are simulated.
const ime = Object.fromEntries(["setComposition", "clearComposition", "setCandidates",
  "setCursorPosition", "setCandidateWindowProperties"].map(name => [name, async () => true]));
ime.commitText = async ({text}) => { commits.push(text); return true; };
ime.keyEventHandled = () => {};
let core, memory, seq = 0;
const deferred = typeof BrowserSession.dictionary_integrity === "function";
const loadDictionary = async () => {
  const bytes = await readFile(join(root, "pkg/kanji.bin"));
  if (deferred) {
    const hash = await subtle.digest("SHA-256", bytes);
    assert.equal("sha256-" + Buffer.from(hash).toString("base64"), BrowserSession.dictionary_integrity());
    BrowserSession.load_verified_dictionary(bytes);
  } else BrowserSession.load_dictionary(bytes);
};
const controller = new ImeController({
  ime, loadDictionary, report: e => errors.push(e), persist: async () => {},
  load: async () => {
    ({memory} = initSync({module: await readFile(join(root, "pkg/meltype_browser_bg.wasm"))}));
    if (!deferred) await loadDictionary();
    return core = new BrowserSession("");
  },
});
const start = performance.now();
controller.activate("meltype-rust");
controller.focus({contextID: 1, type: "text"});
await controller.ready();
const readyMs = performance.now() - start;
global.gc?.();
const mb = bytes => bytes / (1024 * 1024);
const idleRssMiB = mb(process.memoryUsage().rss), idleWasmMiB = mb(memory.buffer.byteLength);
async function press(key, code) {
  const begin = performance.now();
  controller.key("meltype-rust", {type: "keydown", key, code}, String(++seq));
  await controller.queue;
  return performance.now() - begin;
}
const spaces = [];
try {
  for (let round = 0; round < 2; round++) {
    for (const ch of "irekatawo") {
      timings.push(await press(ch, "Key" + ch.toUpperCase()));
      if (spacing) await delay(spacing);
    }
    spaces.push(await press(" ", "Space"));
    await press("Enter", "Enter");
  }
  assert.deepEqual(errors, []);
  assert.deepEqual(commits, ["入れ方を", "入れ方を"]);
  const firstKeyMs = timings.shift();
  timings.sort((a,b) => a-b);
  console.log(JSON.stringify({spacing, readyMs, idleRssMiB, idleWasmMiB, firstKeyMs,
    typingMedianMs: timings[Math.floor(timings.length/2)], firstSpaceMs: spaces[0], secondSpaceMs: spaces[1]}));
} finally { core.free(); }
