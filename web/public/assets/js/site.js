/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { detectOs, escapeHtml, formatDate, formatNumber, getJson, icon, initCopyButtons, initHeader, prefersReducedMotion } from "./common.js";
import { formatBytes } from "./report-schema.js";

const KANA = {
  a: "あ", i: "い", u: "う", e: "え", o: "お",
  ka: "か", ki: "き", ku: "く", ke: "け", ko: "こ",
  sa: "さ", si: "し", shi: "し", su: "す", se: "せ", so: "そ",
  ta: "た", ti: "ち", chi: "ち", tu: "つ", tsu: "つ", te: "て", to: "と",
  na: "な", ni: "に", nu: "ぬ", ne: "ね", no: "の",
  ha: "は", hi: "ひ", hu: "ふ", fu: "ふ", he: "へ", ho: "ほ",
  ma: "ま", mi: "み", mu: "む", me: "め", mo: "も",
  ya: "や", yu: "ゆ", yo: "よ",
  ra: "ら", ri: "り", ru: "る", re: "れ", ro: "ろ",
  wa: "わ", wo: "を",
  ga: "が", gi: "ぎ", gu: "ぐ", ge: "げ", go: "ご",
  za: "ざ", zi: "じ", ji: "じ", zu: "ず", ze: "ぜ", zo: "ぞ",
  da: "だ", di: "ぢ", du: "づ", de: "で", do: "ど",
  ba: "ば", bi: "び", bu: "ぶ", be: "べ", bo: "ぼ",
  pa: "ぱ", pi: "ぴ", pu: "ぷ", pe: "ぺ", po: "ぽ",
  kya: "きゃ", kyu: "きゅ", kyo: "きょ", sha: "しゃ", shu: "しゅ", sho: "しょ",
  cha: "ちゃ", chu: "ちゅ", cho: "ちょ", nya: "にゃ", nyu: "にゅ", nyo: "にょ",
  hya: "ひゃ", hyu: "ひゅ", hyo: "ひょ", mya: "みゃ", myu: "みゅ", myo: "みょ",
  rya: "りゃ", ryu: "りゅ", ryo: "りょ", gya: "ぎゃ", gyu: "ぎゅ", gyo: "ぎょ",
  ja: "じゃ", ju: "じゅ", jo: "じょ", bya: "びゃ", byu: "びゅ", byo: "びょ",
};
const KANA_KEYS = Object.keys(KANA);

function toKana(romaji) {
  let out = "";
  let i = 0;
  while (i < romaji.length) {
    let matched = false;
    for (const length of [3, 2, 1]) {
      const part = romaji.slice(i, i + length);
      if (part.length === length && KANA[part]) {
        out += KANA[part];
        i += length;
        matched = true;
        break;
      }
    }
    if (matched) continue;
    const c = romaji[i];
    const next = romaji[i + 1];
    if (c === "n" && next === "n") {
      out += "ん";
      i += 2;
      continue;
    }
    if (c === "n" && next && !"aiueoy".includes(next)) {
      out += "ん";
      i += 1;
      continue;
    }
    if (next && c === next && !"aiueon".includes(c)) {
      out += "っ";
      i += 1;
      continue;
    }
    const rest = romaji.slice(i);
    if (KANA_KEYS.some((key) => key.startsWith(rest))) return out + rest;
    out += c;
    i += 1;
  }
  return out;
}

const ja = (raw, kana, text) => ({ raw, kana, text, lang: "ja" });
const en = (raw) => ({ raw, text: raw, lang: "en" });

const EXAMPLES = [
  { label: "kyouhagoogledekensaku", app: "メモ帳", note: "英単語は英字のまま、日本語はかな・漢字に", segs: [ja("kyouha", "きょうは", "今日は"), en("google"), ja("dekensaku", "でけんさく", "で検索")] },
  { label: "ashitanomeetingwotsuika", app: "Slack", note: "日本語の文の中の英単語だけが英字に", segs: [ja("ashitano", "あしたの", "明日の"), en("meeting"), ja("wotsuika", "をついか", "を追加")] },
  { label: "I want to go to the park", app: "Discord", note: "英文はそのまま入る", segs: [en("I want to go to the park")] },
  { label: "// pythonnobug", app: "VS Code", ctx: "// ", note: "コードではコメントと文字列の中だけ日本語に", segs: [en("python"), ja("no", "の", "の"), en("bug")] },
  { label: "/review kyouha", app: "ターミナル", ctx: "> ", note: "/command はそのままアプリへ渡す", segs: [en("/review "), ja("kyouha", "きょうは", "今日は")] },
  { label: "egao", app: "メモ帳", note: "変換して ↑ で絵文字に", keys: ["Space", "↑", "Enter"], segs: [ja("egao", "えがお", "😊")] },
];

const sleep = (ms, signal) =>
  new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, ms);
    signal.addEventListener("abort", () => {
      clearTimeout(timer);
      reject(new DOMException("stopped", "AbortError"));
    }, { once: true });
  });

function initDemo() {
  const root = document.querySelector("[data-demo]");
  if (!root) return;
  const line = root.querySelector("[data-demo-line]");
  const keys = root.querySelector("[data-demo-keys]");
  const app = root.querySelector("[data-demo-app]");
  const note = root.querySelector("[data-demo-note]");
  const press = root.querySelector("[data-demo-press]");
  const picker = root.querySelector("[data-demo-picker]");
  const reduced = prefersReducedMotion();
  let current = 0;
  let controller = null;
  let paused = reduced;
  let visible = true;

  const buttons = EXAMPLES.map((example, index) => {
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = example.label;
    button.setAttribute("aria-pressed", "false");
    button.addEventListener("click", () => start(index));
    picker.append(button);
    return button;
  });
  const playButton = document.createElement("button");
  playButton.type = "button";
  playButton.className = "demo-play";
  picker.append(playButton);
  const syncPlay = () => {
    playButton.textContent = paused ? "▶ 再生" : "❚❚ 止める";
    playButton.setAttribute("aria-label", paused ? "見本の再生を始める" : "見本の再生を止める");
  };
  playButton.addEventListener("click", () => {
    paused = !paused;
    syncPlay();
    if (paused) stop();
    else start(current);
  });
  syncPlay();

  function render(example, typed, stage) {
    const parts = [];
    if (example.ctx) parts.push(`<span class="ctx">${escapeHtml(example.ctx)}</span>`);
    let remaining = typed;
    for (const seg of example.segs) {
      if (!remaining && stage === "typing") break;
      const piece = stage === "typing" ? remaining.slice(0, seg.raw.length) : seg.raw;
      remaining = stage === "typing" ? remaining.slice(seg.raw.length) : "";
      let text = piece;
      if (seg.lang === "ja") text = stage === "typing" ? toKana(piece) : stage === "kana" ? seg.kana : seg.text;
      const classes = ["seg", seg.lang === "en" ? "is-en" : "is-ja"];
      if (stage === "converted") classes.push("is-conv");
      if (stage === "done") classes.push("is-done");
      parts.push(`<span class="${classes.join(" ")}">${escapeHtml(text)}</span>`);
    }
    if (stage !== "done") parts.push('<span class="caret"></span>');
    line.innerHTML = parts.join("");
  }

  function showKey(name) {
    press.innerHTML = name ? `<kbd>${escapeHtml(name)}</kbd>` : "";
  }

  function showStatic(example) {
    app.textContent = example.app;
    note.textContent = example.note;
    keys.textContent = example.segs.map((s) => s.raw).join("");
    showKey("");
    render(example, "", "done");
  }

  async function play(index, signal) {
    const example = EXAMPLES[index];
    buttons.forEach((button, i) => button.setAttribute("aria-pressed", String(i === index)));
    app.textContent = example.app;
    note.textContent = example.note;
    showKey("");
    const full = example.segs.map((s) => s.raw).join("");
    keys.textContent = "";
    render(example, "", "typing");
    await sleep(500, signal);
    for (let i = 1; i <= full.length; i++) {
      keys.textContent = full.slice(0, i);
      render(example, full.slice(0, i), "typing");
      await sleep(full[i - 1] === " " ? 140 : 70 + Math.random() * 90, signal);
    }
    render(example, full, "kana");
    await sleep(600, signal);
    const converts = example.segs.some((s) => s.lang === "ja" && s.kana !== s.text);
    const sequence = example.keys || (converts ? ["Space", "Enter"] : ["Enter"]);
    for (const key of sequence) {
      showKey(key);
      if (key === "Enter") render(example, full, "done");
      else render(example, full, "converted");
      await sleep(key === "Enter" ? 2600 : 1100, signal);
    }
    showKey("");
  }

  function stop() {
    if (controller) controller.abort();
    controller = null;
  }

  async function start(index) {
    stop();
    current = index;
    if (paused || !visible) {
      buttons.forEach((button, i) => button.setAttribute("aria-pressed", String(i === index)));
      showStatic(EXAMPLES[index]);
      return;
    }
    controller = new AbortController();
    const { signal } = controller;
    try {
      for (;;) {
        await play(current, signal);
        current = (current + 1) % EXAMPLES.length;
      }
    } catch (e) {
      if (e.name !== "AbortError") throw e;
    }
  }

  const observer = new IntersectionObserver((entries) => {
    const now = entries[entries.length - 1].isIntersecting;
    if (now === visible) return;
    visible = now;
    if (!visible) stop();
    else if (!paused) start(current);
  });
  observer.observe(root);
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) stop();
    else if (!paused && visible) start(current);
  });
  start(0);
}

function initSnow() {
  const canvas = document.querySelector("[data-snow]");
  if (!canvas || prefersReducedMotion()) return;
  const context = canvas.getContext("2d");
  const palettes = {
    light: ["rgba(127,208,245,0.55)", "rgba(178,224,248,0.85)"],
    dark: ["rgba(255,255,255,0.5)", "rgba(158,220,248,0.4)"],
  };
  let flakes = [];
  let width = 0;
  let height = 0;
  let frame = 0;
  let running = false;

  function resize() {
    const ratio = Math.min(window.devicePixelRatio || 1, 2);
    width = canvas.clientWidth;
    height = canvas.clientHeight;
    canvas.width = width * ratio;
    canvas.height = height * ratio;
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    const colors = palettes[getComputedStyle(document.documentElement).colorScheme === "dark" ? "dark" : "light"];
    const count = Math.round(Math.min(18, width / 70));
    flakes = Array.from({ length: count }, () => ({
      x: Math.random() * width,
      y: Math.random() * height,
      r: 1.2 + Math.random() * 2,
      speed: 0.12 + Math.random() * 0.25,
      drift: Math.random() * Math.PI * 2,
      color: colors[Math.floor(Math.random() * colors.length)],
    }));
  }

  function tick() {
    context.clearRect(0, 0, width, height);
    for (const flake of flakes) {
      flake.y += flake.speed;
      flake.drift += 0.008;
      flake.x += Math.sin(flake.drift) * 0.25;
      if (flake.y > height + 6) {
        flake.y = -6;
        flake.x = Math.random() * width;
      }
      context.beginPath();
      context.fillStyle = flake.color;
      context.arc(flake.x, flake.y, flake.r, 0, Math.PI * 2);
      context.fill();
    }
    frame = requestAnimationFrame(tick);
  }

  function setRunning(on) {
    if (on === running) return;
    running = on;
    if (on) frame = requestAnimationFrame(tick);
    else cancelAnimationFrame(frame);
  }

  resize();
  window.addEventListener("resize", resize);
  window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", resize);
  new IntersectionObserver((entries) => setRunning(entries[entries.length - 1].isIntersecting && !document.hidden)).observe(canvas);
  document.addEventListener("visibilitychange", () => setRunning(!document.hidden));
}

function initTabs(os) {
  const list = document.querySelector("[data-tabs]");
  if (!list) return;
  const tabs = [...list.querySelectorAll('[role="tab"]')];
  const select = (tab, focus) => {
    tabs.forEach((t) => {
      const on = t === tab;
      t.setAttribute("aria-selected", String(on));
      t.tabIndex = on ? 0 : -1;
      document.getElementById(t.getAttribute("aria-controls")).hidden = !on;
    });
    if (focus) tab.focus();
  };
  tabs.forEach((tab, index) => {
    tab.addEventListener("click", () => select(tab, false));
    tab.addEventListener("keydown", (event) => {
      const step = { ArrowRight: 1, ArrowDown: 1, ArrowLeft: -1, ArrowUp: -1 }[event.key];
      if (event.key === "Home") select(tabs[0], true);
      else if (event.key === "End") select(tabs[tabs.length - 1], true);
      else if (step) select(tabs[(index + step + tabs.length) % tabs.length], true);
      else return;
      event.preventDefault();
    });
  });
  const preferred = tabs.find((tab) => tab.dataset.os === os);
  if (preferred) select(preferred, false);
}

function initScrollSpy() {
  const links = [...document.querySelectorAll('.site-nav a[href^="#"], .pills a[href^="#"]')];
  const targets = new Map();
  for (const link of links) {
    const target = document.querySelector(link.getAttribute("href"));
    if (!target) continue;
    if (!targets.has(target)) targets.set(target, []);
    targets.get(target).push(link);
  }
  const visible = new Set();
  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) visible.add(entry.target);
      else visible.delete(entry.target);
    }
    const ordered = [...targets.keys()].filter((t) => visible.has(t));
    links.forEach((link) => link.classList.remove("is-current"));
    const navTarget = ordered.find((t) => targets.get(t).some((l) => l.closest(".site-nav")));
    const pillTarget = ordered.find((t) => targets.get(t).some((l) => l.closest(".pills")));
    for (const target of [navTarget, pillTarget]) {
      if (target) targets.get(target).forEach((link) => link.classList.add("is-current"));
    }
  }, { rootMargin: "-30% 0px -60% 0px" });
  targets.forEach((_, target) => observer.observe(target));
}

const OS_ASSET = { windows: "setup.exe", mac: "mac.zip", linux: "linux.zip" };
const OS_LABEL = { windows: "Windows 版をダウンロード", mac: "Mac 版 (プレビュー) をダウンロード", linux: "Linux 版 (プレビュー) をダウンロード" };

function inline(text) {
  const codes = [];
  let html = escapeHtml(text).replace(/`([^`]+)`/g, (_, code) => {
    codes.push(`<code>${code}</code>`);
    return `\u0000${codes.length - 1}\u0000`;
  });
  html = html
    .replace(/\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g, '<a href="$2">$1</a>')
    .replace(/(^|[\s(])(https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/(?:pull|issues)\/(\d+))/g, '$1<a href="$2">#$3</a>')
    .replace(/(^|[\s(])(https?:\/\/[^\s<]+)/g, '$1<a href="$2">$2</a>')
    .replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>")
    .replace(/(^|[\s(（])@([A-Za-z0-9-]+(?:\[bot\])?)/g, (match, lead, login) =>
      login.endsWith("[bot]") ? `${lead}@${login}` : `${lead}<a href="https://github.com/${login}">@${login}</a>`,
    );
  return html.replace(/\u0000(\d+)\u0000/g, (_, index) => codes[Number(index)]);
}

function markdown(text) {
  const out = [];
  let list = null;
  let paragraph = [];
  const flush = () => {
    if (paragraph.length) out.push(`<p>${paragraph.map(inline).join("<br>")}</p>`);
    paragraph = [];
  };
  const closeList = () => {
    if (list) out.push(`<ul>${list.join("")}</ul>`);
    list = null;
  };
  let table = null;
  const closeTable = () => {
    if (table) {
      const [head, ...body] = table;
      const cells = (row, tag) => row.map((cell) => `<${tag}>${inline(cell)}</${tag}>`).join("");
      out.push(`<div class="table-wrap"><table><thead><tr>${cells(head, "th")}</tr></thead><tbody>${body.map((row) => `<tr>${cells(row, "td")}</tr>`).join("")}</tbody></table></div>`);
    }
    table = null;
  };
  for (const raw of text.split("\n")) {
    const lineText = raw.replace(/\s+$/, "");
    if (/^\s*\|.*\|$/.test(lineText)) {
      flush();
      closeList();
      if (!/^[\s|:-]+$/.test(lineText)) (table ||= []).push(lineText.trim().slice(1, -1).split("|").map((cell) => cell.trim()));
      continue;
    }
    closeTable();
    const heading = /^(#{1,4})\s+(.*)$/.exec(lineText);
    const item = /^\s*[*-]\s+(.*)$/.exec(lineText);
    if (!lineText.trim()) {
      flush();
      closeList();
    } else if (heading) {
      flush();
      closeList();
      out.push(`<h4>${inline(heading[2])}</h4>`);
    } else if (item) {
      flush();
      (list ||= []).push(`<li>${inline(item[1])}</li>`);
    } else if (list && /^\s{2,}/.test(raw)) {
      list[list.length - 1] = list[list.length - 1].replace(/<\/li>$/, ` ${inline(lineText.trim())}</li>`);
    } else {
      closeList();
      paragraph.push(lineText);
    }
  }
  flush();
  closeList();
  closeTable();
  return out.join("");
}

function assetFor(release, suffix) {
  return release && release.assets.find((asset) => asset.name.endsWith(suffix));
}

function renderReleases(data) {
  const box = document.querySelector("[data-releases]");
  if (!box || !data.releases.length) return;
  const stable = data.releases.filter((r) => !r.prerelease);
  const latest = stable[0];
  const preview = data.releases.find((r) => r.prerelease && (!latest || new Date(r.publishedAt) > new Date(latest.publishedAt)));
  const shown = [latest, preview, ...stable.slice(1, 3)].filter(Boolean);
  box.innerHTML = shown
    .map((release) => {
      const badges = [];
      if (release === latest) badges.push('<span class="badge is-sky">最新の版</span>');
      if (release.prerelease) badges.push('<span class="badge is-pink">プレビュー版</span>');
      const assets = release.assets
        .map((asset) => `<li><a href="${escapeHtml(asset.url)}">${icon("download")}${escapeHtml(asset.name)} <small>${formatBytes(asset.size)}・${formatNumber(asset.downloads)} 回</small></a></li>`)
        .join("");
      return `<article class="release${release === latest ? " is-latest" : ""}">
        <div class="release-head">
          <h3><a href="${escapeHtml(release.url)}">${escapeHtml(release.name)}</a></h3>
          ${badges.join("")}
          <span class="release-date">${formatDate(release.publishedAt)}</span>
        </div>
        <div class="release-notes">${markdown(release.notes || "")}</div>
        <button class="release-more" type="button" aria-expanded="false">続きを読む</button>
        <ul class="assets">${assets}</ul>
      </article>`;
    })
    .join("");
  box.querySelectorAll(".release").forEach((article) => {
    const notes = article.querySelector(".release-notes");
    const more = article.querySelector(".release-more");
    if (notes.scrollHeight <= notes.clientHeight + 4) {
      notes.classList.add("is-open");
      more.remove();
      return;
    }
    more.addEventListener("click", () => {
      const open = notes.classList.toggle("is-open");
      more.textContent = open ? "閉じる" : "続きを読む";
      more.setAttribute("aria-expanded", String(open));
    });
  });
}

function renderPeople(data) {
  const sponsors = document.querySelector("[data-sponsors]");
  if (sponsors && data.sponsors.length) {
    sponsors.innerHTML = data.sponsors
      .map((login) => `<li><a href="https://github.com/${escapeHtml(login)}" title="@${escapeHtml(login)}"><img src="https://github.com/${escapeHtml(login)}.png?size=120" alt="@${escapeHtml(login)}" width="60" height="60" loading="lazy"></a></li>`)
      .join("");
  }
  const contributors = document.querySelector("[data-contributors]");
  if (contributors && data.contributors.length) {
    contributors.innerHTML = data.contributors
      .map((person) => {
        const avatar = `${person.avatar}${person.avatar.includes("?") ? "&" : "?"}s=120`;
        return `<li><a href="${escapeHtml(person.url)}" title="@${escapeHtml(person.login)}・${formatNumber(person.contributions)} コミット"><img src="${escapeHtml(avatar)}" alt="" width="60" height="60" loading="lazy"><span class="name">${escapeHtml(person.login)}</span><span class="commits">${formatNumber(person.contributions)} commits</span></a></li>`;
      })
      .join("");
    const count = document.querySelector("[data-contributor-count]");
    if (count) count.textContent = `${data.contributors.length} 人`;
  }
}

function applyDownloads(data, os) {
  const latest = data.releases.find((r) => !r.prerelease);
  if (!latest) return;
  const version = latest.tag.replace(/^v/, "");
  const stats = {
    stars: formatNumber(data.stars),
    downloads: formatNumber(data.totalDownloads),
    version: latest.tag,
  };
  for (const [key, value] of Object.entries(stats)) {
    const element = document.querySelector(`[data-stat="${key}"]`);
    if (element) element.textContent = value;
  }
  document.querySelectorAll("[data-version-inline]").forEach((element) => (element.textContent = version));
  document.querySelectorAll("[data-asset-link]").forEach((link) => {
    const asset = assetFor(latest, link.dataset.assetLink);
    if (!asset) return;
    link.href = asset.url;
    const code = link.querySelector("code");
    if (code) code.textContent = asset.name;
  });
  const asset = OS_ASSET[os] ? assetFor(latest, OS_ASSET[os]) : null;
  const main = document.querySelector("[data-download-main]");
  if (asset && main) {
    main.href = asset.url;
    main.querySelector("[data-download-label]").textContent = OS_LABEL[os];
    main.querySelector("[data-download-meta]").textContent = `${latest.tag}・${asset.name.split("-").pop()}・${formatBytes(asset.size)}`;
    document.querySelectorAll("[data-download-link]:not([data-download-main])").forEach((link) => (link.href = asset.url));
  } else if (main && os !== "mobile") {
    main.querySelector("[data-download-meta]").textContent = `${latest.tag}・Windows 10 / 11`;
  }
  const hashes = document.querySelector("[data-hashes]");
  const digests = latest.assets.filter((a) => a.digest);
  if (hashes && digests.length) {
    hashes.innerHTML = `<p>${escapeHtml(latest.tag)} の SHA-256:</p>` + digests.map((a) => `<div><b>${escapeHtml(a.name)}</b><br><code>${escapeHtml(a.digest.replace(/^sha256:/, ""))}</code></div>`).join("");
    hashes.hidden = false;
  }
}

async function loadGitHub(os) {
  try {
    const data = await getJson("/api/github");
    applyDownloads(data, os);
    renderReleases(data);
    renderPeople(data);
  } catch (e) {
    console.warn("GitHub の情報を読めませんでした", e);
  }
}

function initDownloadLabel(os) {
  const main = document.querySelector("[data-download-main]");
  if (!main) return;
  if (OS_LABEL[os]) main.querySelector("[data-download-label]").textContent = OS_LABEL[os];
  if (os === "mac") main.querySelector("[data-download-meta]").textContent = "macOS 13 以降・Apple シリコン";
  if (os === "linux") main.querySelector("[data-download-meta]").textContent = "IBus / fcitx5・x86_64";
  if (os === "mobile") main.querySelector("[data-download-meta]").textContent = "PC で開いてください・Windows 10 / 11";
}

initHeader();
initCopyButtons();
const os = detectOs();
initDownloadLabel(os);
initTabs(os);
initScrollSpy();
initSnow();
initDemo();
loadGitHub(os);
