/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import assert from "node:assert/strict";
import { beforeEach, describe, it } from "node:test";

import { ASSETS, FakeD1, FakeKV, FakeR2 } from "./fakes.mjs";

let rewritten = [];
globalThis.HTMLRewriter = class {
  on(selector) {
    rewritten.push(selector);
    return this;
  }
  transform(response) {
    return response;
  }
};

const { default: worker } = await import("../src/worker.js");
const { _resetAccessCache } = await import("../src/access.js");
const { cleanup } = await import("../src/cleanup.js");
const { _resetGithubCache } = await import("../src/github.js");
const { looksLike } = await import("../src/reports.js");
const { readJson } = await import("../src/http.js");
const { LIMITS, formById, privateLabels, toMarkdown, validateFiles } = await import("../public/assets/js/report-schema.js");

const TEAM = "meltype-test.cloudflareaccess.com";
const AUD = "test-aud";
const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1, 2, 3, 4]);

const keyPair = await crypto.subtle.generateKey(
  { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
  true,
  ["sign", "verify"],
);
const publicJwk = { ...(await crypto.subtle.exportKey("jwk", keyPair.publicKey)), kid: "k1", alg: "RS256" };

const b64url = (bytes) => Buffer.from(bytes).toString("base64url");

async function jwt(payload, kid = "k1") {
  const head = b64url(new TextEncoder().encode(JSON.stringify({ alg: "RS256", kid, typ: "JWT" })));
  const body = b64url(new TextEncoder().encode(JSON.stringify(payload)));
  const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", keyPair.privateKey, new TextEncoder().encode(`${head}.${body}`));
  return `${head}.${body}.${b64url(new Uint8Array(signature))}`;
}

function goodClaims(extra = {}) {
  const at = Math.floor(Date.now() / 1000);
  return { aud: [AUD], iss: `https://${TEAM}`, exp: at + 600, iat: at, email: "owner@example.com", ...extra };
}

const GITHUB = {
  "https://api.github.com/repos/yksr-melt/Meltype": () => ({ html_url: "https://github.com/yksr-melt/Meltype", stargazers_count: github.stars, forks_count: 1, open_issues_count: 2 }),
  "https://api.github.com/repos/yksr-melt/Meltype/releases?per_page=100": () => [
    {
      tag_name: "v1.0.0",
      name: "v1.0.0",
      draft: false,
      prerelease: false,
      published_at: "2026-10-01T00:00:00Z",
      html_url: "https://github.com/yksr-melt/Meltype/releases/tag/v1.0.0",
      body: "",
      assets: [{ name: "a.zip", size: 1, download_count: 5, browser_download_url: "https://example.com/a.zip" }],
    },
  ],
  "https://api.github.com/repos/yksr-melt/Meltype/contributors?per_page=100": () => [{ login: "someone", type: "User", avatar_url: "", html_url: "", contributions: 1 }],
};

let turnstileAnswer;
let turnstileRequests;
let github;
globalThis.fetch = async (input, init) => {
  const url = String(input instanceof Request ? input.url : input);
  if (url === `https://${TEAM}/cdn-cgi/access/certs`) return Response.json({ keys: [publicJwk] });
  if (url.startsWith("https://challenges.cloudflare.com/turnstile/")) {
    turnstileRequests.push({ type: init.headers["content-type"], body: JSON.parse(init.body) });
    return Response.json(turnstileAnswer);
  }
  if (GITHUB[url]) {
    github.calls++;
    return github.down ? new Response("down", { status: 503 }) : Response.json(GITHUB[url]());
  }
  if (url === "https://raw.githubusercontent.com/yksr-melt/Meltype/HEAD/README.md") return new Response("");
  throw new Error(`テストで想定していない fetch: ${url} ${init && init.method}`);
};

let env;
let pending;
const ctx = { waitUntil: (promise) => pending.push(promise) };

function call(path, init = {}) {
  return worker.fetch(new Request(`https://meltype.moe${path}`, init), env, ctx);
}

function report(extra = {}, ip = "203.0.113.5") {
  return call("/api/reports", {
    method: "POST",
    headers: { "content-type": "application/json", "cf-connecting-ip": ip },
    body: JSON.stringify({
      form: "dictionary",
      fields: { kind: "そのほか", words: "しゃおみ → Xiaomi", contact: "Discord: someone" },
      files: [],
      website: "",
      elapsed: 5000,
      turnstile: "token",
      ...extra,
    }),
  });
}

beforeEach(() => {
  _resetAccessCache();
  _resetGithubCache();
  turnstileAnswer = { success: true, action: "report", hostname: "meltype.moe" };
  turnstileRequests = [];
  github = { calls: 0, down: false, stars: 100 };
  pending = [];
  env = {
    DB: new FakeD1(),
    TYPING_DB: new FakeD1("migrations-typing"),
    FILES: new FakeR2(),
    CACHE: new FakeKV(),
    ASSETS,
    ACCESS_TEAM_DOMAIN: TEAM,
    ACCESS_AUD: AUD,
    TURNSTILE_SECRET: "secret",
    TURNSTILE_HOSTNAMES: "meltype.moe",
    IP_HASH_SALT: "salt",
  };
});

describe("ダッシュボードの認証 (#1)", () => {
  it("JWT が無いと報告も添付もダッシュボードの HTML も読めない", async () => {
    assert.equal((await report()).status, 201);
    for (const path of ["/api/dashboard/reports/1", "/api/dashboard/reports", "/api/dashboard/stats", "/api/dashboard/reports/1/files/0", "/dashboard/", "/dashboard"]) {
      assert.equal((await call(path)).status, 403, path);
    }
  });

  it("Access の設定が無ければ 503 で閉じる (fail-closed)", async () => {
    env.ACCESS_AUD = "";
    const token = await jwt(goodClaims());
    assert.equal((await call("/api/dashboard/reports", { headers: { "cf-access-jwt-assertion": token } })).status, 503);
  });

  it("正しい JWT のヘッダーなら読める", async () => {
    assert.equal((await report()).status, 201);
    const token = await jwt(goodClaims());
    const response = await call("/api/dashboard/reports/1", { headers: { "cf-access-jwt-assertion": token } });
    assert.equal(response.status, 200);
    assert.equal((await response.json()).fields.contact, "Discord: someone");
    assert.equal((await call("/api/dashboard/stats", { headers: { "cf-access-jwt-assertion": token } })).status, 200);
  });

  it("クッキーだけでは通さない (Access が付けるヘッダーだけを見る)", async () => {
    const token = await jwt(goodClaims());
    assert.equal((await call("/api/dashboard/stats", { headers: { cookie: `CF_Authorization=${token}` } })).status, 403);
  });

  it("DASHBOARD_DEV_BYPASS は localhost のときだけ効く", async () => {
    env.DASHBOARD_DEV_BYPASS = "1";
    assert.equal((await worker.fetch(new Request("http://localhost:8787/api/dashboard/stats"), env, ctx)).status, 200);
    assert.equal((await call("/api/dashboard/stats")).status, 403);
  });

  it("AUD・発行元・期限・署名が違う JWT は通さない", async () => {
    const at = Math.floor(Date.now() / 1000);
    const bad = [
      await jwt(goodClaims({ aud: ["other"] })),
      await jwt(goodClaims({ iss: "https://evil.cloudflareaccess.com" })),
      await jwt(goodClaims({ exp: at - 3600 })),
      await jwt(goodClaims(), "unknown-kid"),
      (await jwt(goodClaims())).slice(0, -4) + "AAAA",
      "not.a.jwt",
    ];
    for (const token of bad) {
      assert.equal((await call("/api/dashboard/reports", { headers: { "cf-access-jwt-assertion": token } })).status, 403);
    }
  });

  it("公開の API (報告の送信・GitHub) には JWT は要らない", async () => {
    assert.equal((await report()).status, 201);
  });
});

describe("Turnstile (#2)", () => {
  it("トークンが無い・失敗・別のホストなら受け付けない", async () => {
    assert.equal((await report({ turnstile: "" })).status, 400);
    turnstileAnswer = { success: false, "error-codes": ["invalid-input-response"] };
    assert.equal((await report()).status, 400);
    turnstileAnswer = { success: true, action: "report", hostname: "evil.example" };
    assert.equal((await report()).status, 400);
  });

  it("IP_HASH_SALT が無ければ 503 (fail-closed)", async () => {
    delete env.IP_HASH_SALT;
    assert.equal((await report()).status, 503);
  });

  it("siteverify には JSON でシークレット・トークン・IP を送る", async () => {
    assert.equal((await report()).status, 201);
    assert.deepEqual(turnstileRequests, [{ type: "application/json", body: { secret: "secret", response: "token", remoteip: "203.0.113.5" } }]);
  });

  it("TURNSTILE_SECRET が無ければ 503 (fail-closed)", async () => {
    delete env.TURNSTILE_SECRET;
    assert.equal((await report()).status, 503);
  });
});

describe("添付 (#2)", () => {
  async function withFile(name, size) {
    const response = await report({ form: "idea", fields: { problem: "p", idea: "i", version: "1.4.0" }, files: [{ name, size }] });
    assert.equal(response.status, 201);
    return response.json();
  }

  function put(created, bytes, index = 0) {
    return call(`/api/reports/${created.id}/files/${index}`, {
      method: "PUT",
      headers: { authorization: `Bearer ${created.uploadToken}`, "content-length": String(bytes.length), "content-type": "application/octet-stream" },
      body: bytes,
    });
  }

  it("拡張子と中身が違うと受け取らない", async () => {
    const html = new TextEncoder().encode("<html><script>alert(1)</script>");
    const created = await withFile("shot.png", html.length);
    assert.equal((await put(created, html)).status, 415);
  });

  it("届いた添付は既定でダウンロード、画像だけ ?inline=1 で inline", async () => {
    const created = await withFile("shot.png", PNG.length);
    assert.equal((await put(created, PNG)).status, 200);
    const headers = { "cf-access-jwt-assertion": await jwt(goodClaims()) };
    const download = await call(`/api/dashboard/reports/${created.id}/files/0`, { headers });
    assert.equal(download.status, 200);
    assert.match(download.headers.get("content-disposition"), /^attachment;/);
    assert.equal(download.headers.get("x-content-type-options"), "nosniff");
    const inline = await call(`/api/dashboard/reports/${created.id}/files/0?inline=1`, { headers });
    assert.match(inline.headers.get("content-disposition"), /^inline;/);
  });

  it("テキストは ?inline=1 でもダウンロード", async () => {
    const text = new TextEncoder().encode("log line\n");
    const created = await withFile("a.log", text.length);
    assert.equal((await put(created, text)).status, 200);
    const headers = { "cf-access-jwt-assertion": await jwt(goodClaims()) };
    const response = await call(`/api/dashboard/reports/${created.id}/files/0?inline=1`, { headers });
    assert.match(response.headers.get("content-disposition"), /^attachment;/);
  });

  it("1 ファイルの上限を超えると送信の前に断る", () => {
    assert.ok(validateFiles([{ name: "big.mp4", size: LIMITS.maxFileBytes + 1 }]).error);
    assert.ok(validateFiles([{ name: "ok.mp4", size: LIMITS.maxFileBytes }]).files);
  });

  it("先頭のバイトで種類を見分ける", () => {
    assert.ok(looksLike("image/png", PNG));
    assert.ok(!looksLike("image/jpeg", PNG));
    assert.ok(looksLike("text/plain; charset=utf-8", new TextEncoder().encode("ok")));
    assert.ok(!looksLike("text/plain; charset=utf-8", new Uint8Array([0xff, 0xfe, 0x00])));
  });
});

describe("R2 が無いとき", () => {
  beforeEach(() => {
    delete env.FILES;
  });

  it("報告の画面で添付の欄を隠す", async () => {
    rewritten = [];
    await call("/report/");
    assert.ok(rewritten.includes("[data-attachments]"));
    assert.ok(rewritten.includes("[data-report]"));
  });

  it("R2 があるときは隠さない", async () => {
    env.FILES = new FakeR2();
    rewritten = [];
    await call("/report/");
    assert.ok(!rewritten.includes("[data-attachments]"));
  });

  it("文章だけの報告は受け付ける", async () => {
    assert.equal((await report()).status, 201);
  });

  it("添付のある報告は断る", async () => {
    const response = await report({ form: "idea", fields: { problem: "p", idea: "i", version: "1.4.0" }, files: [{ name: "shot.png", size: PNG.length }] });
    assert.equal(response.status, 422);
  });

  it("掃除の Cron が R2 に触らずに動く", async () => {
    await cleanup(env);
  });
});

describe("本文の上限 (readJson)", () => {
  const MAX = 1024;
  // Content-Length の無い本文を、chunk ずつ流す Request。読んだバイト数を数える
  function streamed(chunkCount, chunkSize, headers = {}) {
    const state = { pulled: 0, cancelled: false };
    const body = new ReadableStream({
      pull(controller) {
        if (state.pulled >= chunkCount) return controller.close();
        state.pulled++;
        controller.enqueue(new Uint8Array(chunkSize).fill(0x20));
      },
      cancel() {
        state.cancelled = true;
      },
    });
    const request = new Request("https://meltype.moe/api/reports", {
      method: "POST",
      headers: { "content-type": "application/json", ...headers },
      body,
      duplex: "half",
    });
    return { request, state };
  }

  it("Content-Length が無くても、上限を超えたら読むのをやめて 413 にする", async () => {
    const { request, state } = streamed(4096, 1024);
    const { error } = await readJson(request, MAX);
    assert.equal(error.status, 413);
    assert.ok(state.pulled * 1024 < 64 * 1024, `上限の後も ${state.pulled} KiB 読んだ`);
    assert.equal(state.cancelled, true);
  });

  it("Content-Length を小さく偽っても同じ", async () => {
    const { request } = streamed(4096, 1024, { "content-length": "10" });
    const { error } = await readJson(request, MAX);
    assert.equal(error.status, 413);
  });

  it("ちょうど上限なら読める。1 バイト超えたら 413", async () => {
    const pad = (n) => `{"a":"${"x".repeat(n - 8)}"}`;
    const ok = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "application/json" }, body: pad(MAX) });
    assert.equal((await readJson(ok, MAX)).body.a.length, MAX - 8);
    const over = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "application/json" }, body: pad(MAX + 1) });
    assert.equal((await readJson(over, MAX)).error.status, 413);
  });

  it("日本語を含む本文は、文字数ではなくバイト数で数え、チャンクの途中で切れても読める", async () => {
    const text = JSON.stringify({ a: "しゃおみ".repeat(50) });
    const bytes = new TextEncoder().encode(text);
    const body = new ReadableStream({
      start(controller) {
        controller.enqueue(bytes.slice(0, 7));
        controller.enqueue(bytes.slice(7));
        controller.close();
      },
    });
    const request = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "application/json" }, body, duplex: "half" });
    assert.equal((await readJson(request, 4096)).body.a, "しゃおみ".repeat(50));
    const tooSmall = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "application/json" }, body: text });
    assert.equal((await readJson(tooSmall, bytes.length - 1)).error.status, 413);
  });

  it("本文が無い・JSON でないときは 400。JSON 以外の形式は 415", async () => {
    const empty = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "application/json" } });
    assert.equal((await readJson(empty, MAX)).error.status, 400);
    const broken = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "application/json" }, body: "{" });
    assert.equal((await readJson(broken, MAX)).error.status, 400);
    const text = new Request("https://meltype.moe/", { method: "POST", headers: { "content-type": "text/plain" }, body: "{}" });
    assert.equal((await readJson(text, MAX)).error.status, 415);
  });

  it("公開の報告 API でも、大きな本文は Turnstile の前に 413 になる", async () => {
    const res = await report({ fields: { words: "x".repeat(300 * 1024) } });
    assert.equal(res.status, 413);
    assert.equal(turnstileRequests.length, 0);
  });
});

describe("レートリミット (#3)", () => {
  it("同時に 5 件送っても 10 分に 3 件しか通らない", async () => {
    const responses = await Promise.all(Array.from({ length: 5 }, () => report()));
    const statuses = responses.map((r) => r.status).sort();
    assert.deepEqual(statuses, [201, 201, 201, 429, 429]);
    const limited = responses.find((r) => r.status === 429);
    assert.ok(Number(limited.headers.get("retry-after")) > 0);
    assert.equal((await report({}, "198.51.100.7")).status, 201);
  });
});

describe("設定の利用アンケート", () => {
  it("設定名と利用状況で回答を受け付け、ログインした運営だけが読める", async () => {
    const form = "settings-survey";
    const fields = { setting: "ライブ変換", usage: "使っている", "setting-context": "文章を書くとき", contact: "保存しない" };
    assert.equal((await report({ form, fields: { ...fields, usage: "別の値" } })).status, 422);
    assert.equal((await report({ form, fields, files: [{ name: "a.png", size: 12 }] })).status, 422);
    const response = await report({ form, fields });
    assert.equal(response.status, 201);
    const { id, title, uploadToken } = await response.json();
    assert.equal(title, "[アンケート] ライブ変換 — 使っている");
    assert.equal(uploadToken, null);
    const path = `/api/dashboard/reports/${id}`;
    assert.equal((await call(path)).status, 403);
    const headers = { "Cf-Access-Jwt-Assertion": await jwt(goodClaims()) };
    const detail = await (await call(path, { headers })).json();
    assert.deepEqual(detail.fields, { setting: "ライブ変換", usage: "使っている", "setting-context": "文章を書くとき" });
    assert.equal((await report()).status, 201);
    assert.equal((await report()).status, 201);
    assert.equal((await report()).status, 201);
  });
});

describe("掃除の Cron (#4)", () => {
  it("受付時間が過ぎて届かなかった添付を消し、報告は本文だけ残す", async () => {
    const response = await report({ form: "idea", fields: { problem: "p", idea: "i", version: "1.4.0" }, files: [{ name: "a.png", size: 12 }] });
    const created = await response.json();
    await env.FILES.put(`reports/${created.id}/0`, PNG);
    const later = Math.floor(Date.now() / 1000) + 2 * 60 * 60;
    const result = await cleanup(env, later);
    assert.equal(result.staleFiles, 1);
    assert.equal(result.staleReports, 1);
    const row = await env.DB.prepare("SELECT status, upload_token_hash FROM reports WHERE id = ?").bind(created.id).first();
    assert.equal(row.status, "new");
    assert.equal(row.upload_token_hash, null);
    assert.equal(env.FILES.objects.size, 0);
  });

  it("古い添付・報告・レートリミットの記録を消す", async () => {
    const response = await report({ form: "idea", fields: { problem: "p", idea: "i", version: "1.4.0" }, files: [{ name: "a.png", size: PNG.length }] });
    const created = await response.json();
    await call(`/api/reports/${created.id}/files/0`, {
      method: "PUT",
      headers: { authorization: `Bearer ${created.uploadToken}`, "content-length": String(PNG.length) },
      body: PNG,
    });
    const day = 24 * 60 * 60;
    const at = Math.floor(Date.now() / 1000);
    const afterFiles = await cleanup({ ...env, FILE_RETENTION_DAYS: "30", REPORT_RETENTION_DAYS: "180" }, at + 31 * day);
    assert.equal(afterFiles.expiredFiles, 1);
    assert.equal(afterFiles.rateEvents, 1);
    assert.equal(env.FILES.objects.size, 0);
    const afterReports = await cleanup({ ...env, FILE_RETENTION_DAYS: "30", REPORT_RETENTION_DAYS: "180" }, at + 181 * day);
    assert.equal(afterReports.expiredReports, 1);
    assert.equal((await env.DB.prepare("SELECT COUNT(*) AS n FROM reports").first()).n, 0);
  });
});

describe("Issue にする内容", () => {
  const fields = {
    os: "Windows 11",
    version: "1.2.1",
    actual: "kyouha と打って Space を押しても変換されない",
    log: "secret log",
    environment: "OS: Windows 11 26200",
    contact: "Discord: someone",
  };

  it("連絡先・ログ・実行環境は載せない", () => {
    const markdown = toMarkdown(formById("bug"), fields);
    assert.match(markdown, /kyouha と打って Space/);
    for (const text of ["secret log", "OS: Windows 11 26200", "Discord: someone", "### ログ", "### 実行環境", "### 連絡先"]) {
      assert.ok(!markdown.includes(text), text);
    }
  });

  it("フォームから外した欄も、保存済みの報告なら載せる", () => {
    assert.match(toMarkdown(formById("bug"), { ...fields, expected: "変換されてほしい" }), /### どうなってほしかったか\n\n変換されてほしい/);
  });

  it("外した欄の名前を返す (書かれていない欄は返さない)", () => {
    assert.deepEqual(privateLabels(formById("bug"), fields), ["実行環境", "ログ", "連絡先"]);
    assert.deepEqual(privateLabels(formById("bug"), { ...fields, log: "", environment: undefined }), ["連絡先"]);
  });
});

describe("GitHub の情報のキャッシュ (KV)", () => {
  it("KV に無ければ GitHub から読んで、expirationTtl つきで入れる", async () => {
    const response = await call("/api/github");
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("x-meltype-cache"), "miss");
    assert.equal((await response.json()).stars, 100);
    assert.equal(env.CACHE.puts.length, 1);
    assert.equal(env.CACHE.puts[0].options.expirationTtl, 24 * 60 * 60);
    assert.equal(typeof env.CACHE.puts[0].options.metadata.fetchedAt, "number");
  });

  it("1 時間以内なら GitHub を読まずに KV の値を返す (JWT は要らない)", async () => {
    await call("/api/github");
    const calls = github.calls;
    const response = await call("/api/github");
    assert.equal(response.headers.get("x-meltype-cache"), "hit");
    assert.equal(github.calls, calls);
  });

  it("古くなったら古い値を返しつつ裏で取り直す", async () => {
    await call("/api/github");
    env.CACHE.values.get("github").metadata = { fetchedAt: Math.floor(Date.now() / 1000) - 2 * 60 * 60 };
    github.stars = 200;
    const response = await call("/api/github");
    assert.equal(response.headers.get("x-meltype-cache"), "stale");
    assert.equal((await response.json()).stars, 100);
    await Promise.all(pending);
    assert.equal(JSON.parse(env.CACHE.values.get("github").value).stars, 200);
  });

  it("取り直しは 1 分に 1 回まで", async () => {
    await call("/api/github");
    env.CACHE.values.get("github").metadata = { fetchedAt: 0 };
    github.down = true;
    const calls = github.calls;
    await call("/api/github");
    await call("/api/github");
    await Promise.all(pending);
    assert.equal(github.calls - calls, 3);
  });

  it("GitHub が落ちていても、KV に古い値があればそれを返す", async () => {
    await call("/api/github");
    env.CACHE.values.get("github").metadata = { fetchedAt: 0 };
    github.down = true;
    const response = await call("/api/github");
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("x-meltype-cache"), "stale");
    assert.equal((await response.json()).stars, 100);
    await Promise.all(pending);
  });

  it("KV が空で GitHub も読めなければ 502", async () => {
    github.down = true;
    assert.equal((await call("/api/github")).status, 502);
  });
});

describe("タイピング調査", async () => {
  const { SENTENCES } = await import("../public/assets/js/typing-sentences.js");
  const FIRST = SENTENCES.find((sentence) => sentence.reading === "きょうはあさからあめがふっている。");
  // 打った文字から打鍵の並びを作る
  const CODES = { " ": "Space", ".": "Period", ",": "Comma", "-": "Minus" };
  const keysOf = (text) => [...text].map((char, i) => [CODES[char] || `Key${char.toUpperCase()}`, char, i * 100, i * 100 + 50]);
  const KEYS = keysOf("kyo");

  async function start(extra = {}, ip = "203.0.113.9") {
    turnstileAnswer = { success: true, action: "typing", hostname: "meltype.moe" };
    return call("/api/typing/sessions", {
      method: "POST",
      headers: { "content-type": "application/json", "cf-connecting-ip": ip },
      body: JSON.stringify({ consent: true, turnstile: "token", profile: { touch: "yes", layout: "jis", os: "windows" }, ...extra }),
    });
  }

  function trial(token, extra = {}) {
    return call("/api/typing/trials", {
      method: "POST",
      headers: { "content-type": "application/json", authorization: `Bearer ${token}` },
      body: JSON.stringify({ sentence: FIRST.id, keys: KEYS, duration: 400, version: 2, ...extra }),
    });
  }

  const auth = async () => ({ "cf-access-jwt-assertion": await jwt(goodClaims()) });

  it("同意が無い・報告用の Turnstile では始められない", async () => {
    assert.equal((await start({ consent: false })).status, 400);
    turnstileAnswer = { success: true, action: "report", hostname: "meltype.moe" };
    const response = await call("/api/typing/sessions", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ consent: true, turnstile: "token" }),
    });
    assert.equal(response.status, 400);
  });

  it("打鍵を受け付け、ダッシュボードから書き出せる。IP と打った文字列は残らない", async () => {
    const { id, token } = await (await start()).json();
    assert.equal((await trial(token)).status, 201);
    assert.equal((await trial("wrong")).status, 401);

    assert.equal((await call("/api/dashboard/typing/trials")).status, 403);
    const exported = await (await call("/api/dashboard/typing/trials", { headers: await auth() })).json();
    assert.equal(exported.trials.length, 1);
    const [row] = exported.trials;
    assert.equal(row.session, id);
    assert.equal(row.text, FIRST.text);
    assert.equal(row.reading, FIRST.reading);
    assert.deepEqual(row.keys, KEYS);
    assert.deepEqual(row.profile, { touch: "yes", layout: "jis", os: "windows" });
    assert.ok(!("typed" in row));
    assert.equal(row.version, 2);
    assert.ok(!JSON.stringify(row).includes("203.0.113.9"));
    // 打った文字・一致率・打鍵の数など、打鍵とお手本から計算できる値は保存しない
    const columns = (await env.TYPING_DB.prepare("SELECT name FROM pragma_table_info('typing_trials')").all()).results.map((c) => c.name);
    assert.deepEqual(columns, ["id", "session_id", "sentence_id", "keys", "duration_ms", "created_at", "version"]);
    const counts = await (await call("/api/dashboard/typing/sentences", { headers: await auth() })).json();
    assert.equal(counts.sentences.find((s) => s.id === FIRST.id).count, 1);
    assert.equal(counts.sentences.filter((s) => s.count > 0).length, 1);
  });

  it("知らない文・おかしな打鍵は受け付けない", async () => {
    const { token } = await (await start()).json();
    assert.equal((await trial(token, { version: 1 })).status, 400);
    assert.equal((await trial(token, { sentence: "nope" })).status, 400);
    assert.equal((await trial(token, { keys: [["KeyK", "k", 100, 50]] })).status, 400);
    assert.equal((await trial(token, { keys: [["KeyK", "k", 100, null], ["KeyY", "y", 50, null]] })).status, 400);
    assert.equal((await trial(token, { keys: [] })).status, 400);
    assert.equal((await trial(token, { duration: 0 })).status, 400);
  });

  it("知らない答えは unknown にする", async () => {
    await start({ profile: { touch: "<script>", layout: 1 } });
    const row = await env.TYPING_DB.prepare("SELECT profile FROM typing_sessions").first();
    assert.deepEqual(JSON.parse(row.profile), { touch: "unknown", layout: "unknown", os: "other" });
  });

  it("回数制限は報告と別に数える", async () => {
    for (let i = 0; i < 3; i++) assert.equal((await report()).status, 201);
    assert.equal((await report()).status, 429);
    assert.equal((await start({}, "203.0.113.5")).status, 201);
    for (let i = 0; i < 2; i++) assert.equal((await start({}, "203.0.113.5")).status, 201);
    assert.equal((await start({}, "203.0.113.5")).status, 429);
  });

  it("お手本の長さに見合わない量の打鍵は受け付けない", async () => {
    const { token } = await (await start()).json();
    const many = Array.from({ length: 17 * 12 + 1 }, (_, i) => ["KeyA", "a", i, i]);
    assert.equal((await trial(token, { keys: many })).status, 400);
  });

  it("お手本と関係ない入力が続く文は受け付けない", async () => {
    const { token } = await (await start()).json();
    assert.equal((await trial(token, { keys: keysOf("kyouhaasakaraamegafutteiru.yamadatarou") })).status, 422);
    assert.equal((await trial(token, { keys: keysOf("kyouhaasakaramegafuteiru.") })).status, 201);
  });

  it("お手本とかけ離れた送信をダッシュボードに出し、その文を消せる", async () => {
    const { token } = await (await start()).json();
    assert.equal((await trial(token, { keys: keysOf("kyouhaasakaraamegafutteiru.") })).status, 201);
    assert.equal((await trial(token, { keys: keysOf("kyouhaa") })).status, 201);
    const { sessions } = await (await call("/api/dashboard/typing/flagged", { headers: await auth() })).json();
    assert.equal(sessions.length, 1);
    assert.equal(sessions[0].flagged, 1);
    assert.deepEqual(sessions[0].samples, ["kyouhaa"]);

    const remove = async (origin) => call(`/api/dashboard/typing/sessions/${sessions[0].id}/flagged`, { method: "DELETE", headers: { ...(await auth()), origin } });
    assert.equal((await remove("https://evil.example")).status, 403);
    assert.deepEqual(await (await remove("https://meltype.moe")).json(), { deleted: 1 });
    const left = await env.TYPING_DB.prepare("SELECT keys FROM typing_trials").all();
    assert.equal(left.results.length, 1);
  });

  it("英語が混ざった文も、お手本どおりならかけ離れた送信に出ない", async () => {
    const mixed = SENTENCES.find((sentence) => sentence.reading === "きょうはgoogleでしらべた。");
    const { token } = await (await start()).json();
    assert.equal((await trial(token, { sentence: mixed.id, keys: keysOf("kyouhagoogledeshirabeta.") })).status, 201);
    const { sessions } = await (await call("/api/dashboard/typing/flagged", { headers: await auth() })).json();
    assert.equal(sessions.length, 0);
  });

  it("タイピングのデータベースが無いときは受け付けない", async () => {
    delete env.TYPING_DB;
    assert.equal((await start()).status, 503);
  });

  it("参加に期限は無く、1 日に送れる数だけ決まっている。同意文が変わったら受け付けない", async () => {
    const { token } = await (await start()).json();
    await env.TYPING_DB.prepare("UPDATE typing_sessions SET created_at = created_at - 30 * 86400").run();
    assert.equal((await trial(token)).status, 201);
    const id = (await env.TYPING_DB.prepare("SELECT id FROM typing_sessions").first()).id;
    const yesterday = Math.floor(Date.now() / 1000) - 2 * 86400;
    await env.TYPING_DB.batch(Array.from({ length: 999 }, () => env.TYPING_DB.prepare("INSERT INTO typing_trials (session_id, sentence_id, keys, duration_ms, created_at) VALUES (?, ?, '[]', 1, unixepoch())").bind(id, FIRST.id)));
    assert.equal((await trial(token)).status, 429);
    await env.TYPING_DB.prepare("UPDATE typing_trials SET created_at = ?").bind(yesterday).run();
    assert.equal((await trial(token)).status, 201);
    await env.TYPING_DB.prepare("UPDATE typing_sessions SET consent_version = 0").run();
    assert.equal((await trial(token)).status, 410);
  });
});
