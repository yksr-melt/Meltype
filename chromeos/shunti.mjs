// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
// Remote files are model data only. The executable WASM is bundled with the extension.
import createShunti from "./ai/shunti-engine.mjs";
const revision = "14801bff-s8";
const names = ["kkc_lex.bin", "kkc_model.bin"];
const limit = 128 * 1024 * 1024;
export const modelOrigins = ["https://github.com/*", "https://release-assets.githubusercontent.com/*"];
const hex = bytes => [...new Uint8Array(bytes)].map(b => b.toString(16).padStart(2, "0")).join("");
const sha = bytes => crypto.subtle.digest("SHA-256", bytes).then(hex);
class IndexedData {
  async database() {
    return new Promise((resolve, reject) => {
      const open = indexedDB.open("meltype-ai", 1);
      open.onupgradeneeded = () => open.result.createObjectStore("packages");
      open.onsuccess = () => resolve(open.result); open.onerror = () => reject(open.error);
    });
  }
  async read(key = revision) {
    const db = await this.database();
    try {
      return await new Promise((resolve, reject) => {
        const request = db.transaction("packages").objectStore("packages").get(key);
        request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error);
      });
    } finally { db.close(); }
  }
  async write(value, key = revision) {
    const db = await this.database();
    try {
      await new Promise((resolve, reject) => {
        const transaction = db.transaction("packages", "readwrite");
        transaction.objectStore("packages").put(value, key);
        transaction.oncomplete = resolve; transaction.onerror = () => reject(transaction.error);
        transaction.onabort = () => reject(transaction.error ?? new Error("モデル保存が中断されました"));
      });
    } finally { db.close(); }
  }
}
export class ShuntiPackages {
  constructor({storage = new IndexedData(), fetcher = (...args) => fetch(...args), metadata, variant = "shunti-v7-s8p"} = {}) {
    Object.assign(this, {storage, fetcher, metadata, variant});
    this.revision = variant === "shunti-v7-s8p" ? revision : variant;
  }
  async manifest() {
    const document = this.metadata ?? await (await this.fetcher(new URL(this.variant === "shunti-v7-s8p" ? "./ai/packages.json" : "./ai/model-catalog.json",import.meta.url))).json();
    const selected = document.models?.find(m => m.id === this.variant && m.family === "Shunti" && m.available);
    const packageData = selected ? {revision:this.revision, files:selected.files} : this.variant === "shunti-v7-s8p" ? document.shunti : null;
    if (packageData?.revision !== this.revision || packageData.files?.length !== 2 ||
        !names.every(name => packageData.files.some(file => file.name === name)) ||
        packageData.files.some(file => !/^[0-9a-f]{64}$/.test(file.sha256) || !/^https:\/\/github\.com\/shuntilettuce\/Japanese-IME-for-HarmonyOS-next\/releases\/download\/shuntelligence-v[5-7]\//.test(file.url))) {
      throw new Error("shuntiパッケージの記録が不正です");
    }
    return packageData;
  }
  async verified(value) {
    if (value?.revision !== this.revision) throw new Error("shunti環境が見つかりません");
    const manifest = await this.manifest();
    for (const spec of manifest.files) {
      const data = value.files?.[spec.name];
      if (!(data instanceof ArrayBuffer) || !data.byteLength || data.byteLength > limit || await sha(data) !== spec.sha256) {
        throw new Error("shuntiの辞書・モデルが変更されています。再導入してください。");
      }
    }
    return value;
  }
  async installed() {
    try { await this.verified(await this.storage.read(this.revision)); return true; } catch { return false; }
  }
  async load() { return this.verified(await this.storage.read(this.revision)); }
  // Caller must obtain consent and download permissions before invoking this method.
  async install(progress = () => {}) {
    const previous = await this.storage.read(this.revision);
    if (previous) {
      try { return await this.verified(previous); } catch { /* 壊れた保存データは検証済みの再取得分で置き換える。 */ }
    }
    const manifest = await this.manifest(), files = {};
    for (const spec of manifest.files) {
      const response = await this.fetcher(spec.url, {credentials: "omit", referrerPolicy: "no-referrer", signal: AbortSignal.timeout(900000)});
      if (!response.ok) throw new Error(`AIパッケージを取得できません (${response.status})`);
      const expected = Number(response.headers.get("content-length"));
      if (expected > limit) throw new Error("AIファイルが大きすぎます");
      const preallocated = Number.isSafeInteger(expected) && expected > 0 && !response.headers.get("content-encoding")
        ? new Uint8Array(expected) : null;
      const chunks = []; let size = 0;
      const reader = response.body.getReader();
      try {
        while (true) {
          const {done, value} = await reader.read(); if (done) break;
          size += value.byteLength;
          if (size > limit) throw new Error("AIファイルが大きすぎます");
          if (preallocated) {
            if (size > expected) throw new Error("AIファイルのサイズが一致しません");
            preallocated.set(value, size - value.byteLength);
          } else chunks.push(value);
          progress(spec.name, size, expected);
        }
      } finally { await reader.cancel(); }
      if (preallocated && size !== expected) throw new Error("AIファイルの取得が途中で終了しました");
      const bytes = preallocated ?? new Uint8Array(size); let offset = 0;
      for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.length; }
      if (await sha(bytes) !== spec.sha256) throw new Error("AIファイルのSHA-256が一致しません");
      files[spec.name] = bytes.buffer;
    }
    const value = {revision:this.revision, files};
    // Publish the entire environment in one transaction; failures leave no partial package.
    await this.storage.write(value, this.revision);
    return value;
  }
}
export class ShuntiClient {
  constructor(packages = new ShuntiPackages(), factoryOptions = {}) { this.factoryOptions = factoryOptions; this.packages = packages; this.allocations = []; this.epoch = 0; }
  async prepare() {
    if (this.ready) return;
    const epoch = ++this.epoch;
    const files = (await this.packages.load()).files;
    const module = await createShunti({...this.factoryOptions, print:()=>{}, printErr:()=>{}, locateFile: name => new URL(`./ai/${name}`,import.meta.url).href});
    if (epoch !== this.epoch) return;
    this.module = module;
    const allocate = size => { const p = module._malloc(size); if (!p) throw new Error("AI用メモリーを確保できません"); this.allocations.push(p); return p; };
    try {
      const lex = allocate(files[names[0]].byteLength); module.HEAPU8.set(new Uint8Array(files[names[0]]), lex);
      const model = allocate(files[names[1]].byteLength); module.HEAPU8.set(new Uint8Array(files[names[1]]), model);
      this.engine = module._kkc_open(lex, files[names[0]].byteLength, model, files[names[1]].byteLength);
      if (!this.engine) throw new Error("shuntiの辞書・モデルを開けません");
      module._kkc_set_threads(this.engine, 1);
      this.context = allocate(322); this.reading = allocate(322); this.output = allocate(131072);
      this.ready = true;
    } catch (error) { this.dispose(); throw error; }
  }
  convert(reading, context) {
    if (!this.ready || !reading || reading.length > 160 || /[\x00-\x1f]/u.test(reading)) return [];
    const module = this.module;
    context = [...context].slice(-80).join("");
    for (let i = 0; i < context.length; i++) module.HEAPU16[(this.context >> 1) + i] = context.charCodeAt(i);
    for (let i = 0; i < reading.length; i++) module.HEAPU16[(this.reading >> 1) + i] = reading.charCodeAt(i);
    module.HEAPU16.fill(0, this.output >> 1, (this.output >> 1) + 65536);
    const count = module._kkc_convert(this.engine, this.context, context.length, this.reading, reading.length, 8, 1, this.output, 65536);
    if (count < 1 || count > 8) return [];
    const result = [], decoder = new TextDecoder("utf-16le", {fatal: true}); let start = this.output >> 1;
    for (let i = 0; i < count; i++) {
      let end = start;
      while (end < (this.output >> 1) + 65536 && module.HEAPU16[end]) end++;
      if (end >= (this.output >> 1) + 65536 || end - start > 1024) return [];
      const value = decoder.decode(module.HEAPU8.subarray(start * 2, end * 2));
      if (value && !/[\x00-\x1f]/u.test(value)) result.push(value);
      start = end + 1;
    }
    return result;
  }
  dispose() {
    this.epoch++; this.ready = false;
    if (this.module) {
      if (this.engine) this.module._kkc_close(this.engine);
      for (const pointer of this.allocations) this.module._free(pointer);
    }
    this.engine = 0; this.allocations = []; this.module = null;
  }
}

export class ShuntiProxy {
  constructor(variant = "shunti-v7-s8p") { this.variant = variant; }
  async prepare() {
    if (!await chrome.offscreen.hasDocument()) {
      await chrome.offscreen.createDocument({url:"ai-offscreen.html",reasons:["WORKERS"],justification:"端末内のshunti変換を入力イベントから分離して処理するため"});
    }
    const result = await chrome.runtime.sendMessage({channel:"meltype-shunti",action:"prepare",variant:this.variant});
    if (result?.error || !result?.ready) throw new Error(result?.error ?? "shuntiを準備できません");
    this.ready = true;
  }
  async convert(reading,context) {
    if (!this.ready || this.inFlight || performance.now() < (this.pausedUntil ?? 0)) return [];
    this.inFlight = true;
    const operation = chrome.runtime.sendMessage({channel:"meltype-shunti",action:"convert",reading,context}).finally(()=>{this.inFlight=false;});
    let timer;
    try {
      const result = await Promise.race([operation,new Promise(resolve=>{timer=setTimeout(()=>resolve(null),400);})]);
      if (!result || result.error) { this.pausedUntil = performance.now()+5000; return []; }
      return result.candidates ?? [];
    } finally { clearTimeout(timer); }
  }
  async dispose() {
    this.ready = false;
    if (await chrome.offscreen.hasDocument()) await chrome.offscreen.closeDocument();
  }
}
