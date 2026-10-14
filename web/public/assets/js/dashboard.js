/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { copyText, escapeHtml, formatDate, formatNumber, getJson, icon, initHeader, relativeTime, toast } from "./common.js";
import { FORMS, formById, formatBytes, toMarkdown } from "./report-schema.js";

const REPO = "https://github.com/yksr-melt/Meltype";
const COLORS = { bug: "is-pink", misdetection: "is-sky", dictionary: "is-peri", idea: "is-sky" };
const LABELS = { bug: "bug", misdetection: "誤判定", dictionary: "辞書", idea: "提案" };
const STATUS = { new: "届いた", uploading: "添付を送っている途中" };
const GROUP_ICONS = { image: "image", video: "video", text: "file", json: "braces" };
const PREVIEW_BYTES = 64 * 1024;
const ISSUE_BODY_LIMIT = 6000;

const list = document.querySelector("[data-list]");
const detail = document.querySelector("[data-detail]");
const more = document.querySelector("[data-more]");
const newButton = document.querySelector("[data-new-reports]");
const filters = {
  q: document.querySelector('[data-filter="q"]'),
  form: document.querySelector('[data-filter="form"]'),
  status: document.querySelector('[data-filter="status"]'),
};
const state = { items: [], next: null, selected: null, topId: 0, loading: false, current: null, days: null };

filters.form.innerHTML = `<option value="">すべての種類</option>${FORMS.map((form) => `<option value="${form.id}">${escapeHtml(form.label)}</option>`).join("")}`;

function query(extra = {}) {
  const params = new URLSearchParams();
  for (const [key, element] of Object.entries(filters)) {
    if (element.value.trim()) params.set(key, element.value.trim());
  }
  for (const [key, value] of Object.entries(extra)) if (value != null) params.set(key, value);
  return params.toString();
}

function drawChart(days) {
  const svg = document.querySelector("[data-chart]");
  const width = Math.max(280, Math.round(svg.clientWidth || 600));
  const height = 160;
  svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
  state.days = days;
  const bottom = 136;
  const max = Math.max(1, ...days.map((d) => d.count));
  const slot = width / days.length;
  const parts = [`<line class="grid" x1="0" y1="${bottom}" x2="${width}" y2="${bottom}"/>`];
  days.forEach((day, index) => {
    const h = Math.max(day.count ? 4 : 0, Math.round((day.count / max) * 118));
    const x = index * slot + 3;
    const label = day.day.slice(5).replace("-", "/");
    parts.push(
      `<rect class="bar${index === days.length - 1 ? " is-today" : ""}" x="${x.toFixed(1)}" y="${bottom - h}" width="${(slot - 6).toFixed(1)}" height="${h}" rx="3"><title>${label}: ${day.count} 件</title></rect>`,
    );
    const last = index === days.length - 1;
    if (index % 7 === 1 || last) {
      parts.push(`<text class="axis" x="${(last ? x + slot - 6 : x + (slot - 6) / 2).toFixed(1)}" y="${height - 6}" text-anchor="${last ? "end" : "middle"}">${label}</text>`);
    }
  });
  parts.push(`<text class="axis" x="4" y="12">最大 ${max} 件</text>`);
  svg.innerHTML = parts.join("");
}

function renderKinds(byForm, total) {
  document.querySelector("[data-kinds]").innerHTML = FORMS.map((form) => {
    const count = byForm[form.id] || 0;
    const ratio = total ? Math.round((count / total) * 100) : 0;
    return `<div class="kind-bar ${COLORS[form.id]}"><div><span>${escapeHtml(form.label)}</span><b>${formatNumber(count)}</b></div><span class="meter"><span data-ratio="${ratio}"></span></span></div>`;
  }).join("");
  document.querySelectorAll("[data-ratio]").forEach((bar) => (bar.style.width = `${bar.dataset.ratio}%`));
}

async function loadStats() {
  try {
    const stats = await getJson("/api/dashboard/stats");
    for (const key of ["total", "today", "week", "withFiles"]) {
      document.querySelector(`[data-stat="${key}"]`).textContent = formatNumber(stats[key]);
    }
    drawChart(stats.days);
    renderKinds(stats.byForm, stats.total);
  } catch (e) {
    toast(e.message);
  }
}

function itemHtml(report) {
  const form = formById(report.form);
  const files = report.files
    ? `<span>${icon(report.groups.length === 1 ? GROUP_ICONS[report.groups[0]] || "clip" : "clip")} ${report.uploaded}/${report.files}</span>`
    : "";
  const status = report.status === "uploading" ? `<span class="badge is-pink">${STATUS.uploading}</span>` : "";
  return `<li class="report-item"><button type="button" data-id="${report.id}"${state.selected === report.id ? ' aria-current="true"' : ""}>
    <span class="report-no">#${report.id}</span>
    <span class="report-title">${escapeHtml(report.title)}</span>
    <span class="report-sub"><span class="badge ${COLORS[report.form]}">${escapeHtml(form ? form.label : report.form)}</span><span title="${escapeHtml(formatDate(report.created_at * 1000, true))}">${relativeTime(report.created_at * 1000)}</span>${files}${status}</span>
  </button></li>`;
}

function renderList() {
  list.innerHTML = state.items.length
    ? state.items.map(itemHtml).join("")
    : `<li class="empty">${filters.q.value || filters.form.value || filters.status.value ? "条件に合う報告はありません" : "まだ報告は届いていません"}</li>`;
  more.hidden = !state.next;
}

async function loadList(reset) {
  if (state.loading) return;
  state.loading = true;
  try {
    const result = await getJson(`/api/dashboard/reports?${query({ before: reset ? null : state.next })}`);
    state.items = reset ? result.reports : [...state.items, ...result.reports];
    state.next = result.next;
    if (reset && !filters.q.value && !filters.form.value && !filters.status.value && result.reports[0]) state.topId = Math.max(state.topId, result.reports[0].id);
    renderList();
  } catch (e) {
    list.innerHTML = `<li class="empty">${escapeHtml(e.message)}</li>`;
  } finally {
    state.loading = false;
  }
}

function valueHtml(field, value) {
  if (Array.isArray(value)) return escapeHtml(value.join("、"));
  if (field && field.type === "code") return `<pre>${escapeHtml(value)}</pre>`;
  return escapeHtml(value);
}

function fieldsHtml(fields, values) {
  const rows = fields
    .filter((field) => values[field.id] != null && values[field.id] !== "" && !(Array.isArray(values[field.id]) && !values[field.id].length))
    .map((field) => `<div><dt>${escapeHtml(field.label)}</dt><dd>${valueHtml(field, values[field.id])}</dd></div>`);
  return rows.length ? `<dl class="kv">${rows.join("")}</dl>` : '<p class="small">書かれていません</p>';
}

function fileUrl(report, attachment, download) {
  return `/api/dashboard/reports/${report.id}/files/${attachment.idx}${download ? "?download=1" : ""}`;
}

function attachmentHtml(report, attachment) {
  const head = `<div class="attach-head">${icon(GROUP_ICONS[attachment.grp] || "clip")}<b title="${escapeHtml(attachment.name)}">${escapeHtml(attachment.name)}</b><small>${formatBytes(attachment.size)}</small>`;
  if (!attachment.uploaded_at) return `<div class="attach">${head}<span class="badge is-pink">届いていません</span></div></div>`;
  const links = `<a href="${fileUrl(report, attachment, false)}" target="_blank" rel="noopener">開く</a><a href="${fileUrl(report, attachment, true)}">保存</a></div>`;
  if (attachment.grp === "image") return `<div class="attach">${head}${links}<a href="${fileUrl(report, attachment, false)}" target="_blank" rel="noopener"><img src="${fileUrl(report, attachment, false)}" alt="${escapeHtml(attachment.name)}" loading="lazy"></a></div>`;
  if (attachment.grp === "video") return `<div class="attach">${head}${links}<video src="${fileUrl(report, attachment, false)}" controls preload="metadata"></video></div>`;
  return `<div class="attach">${head}${links}<pre data-preview="${attachment.idx}">読み込んでいます…</pre></div>`;
}

async function loadPreviews(report) {
  for (const element of detail.querySelectorAll("[data-preview]")) {
    const attachment = report.attachments.find((a) => String(a.idx) === element.dataset.preview);
    try {
      const response = await fetch(fileUrl(report, attachment, false), { headers: { range: `bytes=0-${PREVIEW_BYTES - 1}` } });
      if (!response.ok) throw new Error(String(response.status));
      const text = await response.text();
      if (state.current !== report) return;
      element.textContent = attachment.size > PREVIEW_BYTES ? `${text}\n\n… (最初の ${formatBytes(PREVIEW_BYTES)} だけ表示しています)` : text;
    } catch {
      element.textContent = "読み込めませんでした";
    }
  }
}

function markdownFor(report) {
  const form = formById(report.form);
  const lines = [toMarkdown(form, report.fields)];
  const files = report.attachments.filter((a) => a.uploaded_at);
  if (files.length) lines.push(`### 添付\n\n${files.map((a) => `- ${a.name} (${formatBytes(a.size)})`).join("\n")}\n\n添付は報告ダッシュボードの #${report.id} にあります。`);
  lines.push(`---\n報告フォームから (#${report.id}、${formatDate(report.created_at * 1000, true)})`);
  return lines.join("\n\n");
}

function issueUrl(report) {
  let body = markdownFor(report);
  if (body.length > ISSUE_BODY_LIMIT) body = `${body.slice(0, ISSUE_BODY_LIMIT)}\n\n…(長いので途中までです。続きは報告ダッシュボードの #${report.id} を見てください)`;
  const params = new URLSearchParams({ title: report.title, body, labels: LABELS[report.form] || "" });
  return `${REPO}/issues/new?${params}`;
}

function renderDetail(report) {
  state.current = report;
  const form = formById(report.form);
  const status = report.status === "uploading" ? `<span class="badge is-pink">${STATUS.uploading}</span>` : `<span class="badge is-sky">${STATUS.new}</span>`;
  detail.innerHTML = `<div class="detail-head">
      <div class="report-sub"><b>#${report.id}</b><span class="badge ${COLORS[report.form]}">${escapeHtml(form ? form.label : report.form)}</span>${status}<span>${escapeHtml(formatDate(report.created_at * 1000, true))}</span></div>
      <h2>${escapeHtml(report.title)}</h2>
      <div class="detail-actions">
        <button class="btn btn-small" type="button" data-copy-md>${icon("copy")}Issue の形でコピー</button>
        <a class="btn btn-ghost btn-small" href="${escapeHtml(issueUrl(report))}" target="_blank" rel="noopener">${icon("external")}GitHub で Issue を作る</a>
        <button class="btn btn-ghost btn-small" type="button" data-copy-link>${icon("link")}この報告へのリンク</button>
      </div>
    </div>
    <div class="detail-section"><h3><span class="pane-tag is-req">必須</span>かならず書く所</h3>${fieldsHtml(form ? form.required : [], report.fields)}</div>
    <div class="detail-section"><h3><span class="pane-tag is-opt">任意</span>あれば書く所</h3>${fieldsHtml(form ? form.optional : [], report.fields)}</div>
    <div class="detail-section"><h3>${icon("clip")}添付 (${report.attachments.length})</h3>${
      report.attachments.length ? `<div class="attach-grid">${report.attachments.map((a) => attachmentHtml(report, a)).join("")}</div>` : '<p class="small">添付はありません</p>'
    }</div>`;
  detail.querySelector("[data-copy-md]").addEventListener("click", async () => toast((await copyText(markdownFor(report))) ? "Issue の形でコピーしました" : "コピーできませんでした"));
  detail.querySelector("[data-copy-link]").addEventListener("click", async () => toast((await copyText(`${location.origin}/dashboard/?id=${report.id}`)) ? "リンクをコピーしました" : "コピーできませんでした"));
  loadPreviews(report);
}

async function select(id, scroll) {
  state.selected = id;
  list.querySelectorAll("button[data-id]").forEach((button) => {
    if (Number(button.dataset.id) === id) button.setAttribute("aria-current", "true");
    else button.removeAttribute("aria-current");
  });
  const url = new URL(location.href);
  url.searchParams.set("id", String(id));
  history.replaceState(null, "", url);
  detail.innerHTML = '<p class="detail-empty">読み込んでいます…</p>';
  try {
    renderDetail(await getJson(`/api/dashboard/reports/${id}`));
    if (scroll && window.matchMedia("(max-width: 1000px)").matches) detail.scrollIntoView({ behavior: "smooth", block: "start" });
  } catch (e) {
    detail.innerHTML = `<p class="detail-empty">${escapeHtml(e.message)}</p>`;
  }
}

async function checkNew() {
  if (document.hidden) return;
  try {
    const result = await getJson("/api/dashboard/reports?limit=1");
    const top = result.reports[0];
    if (top && top.id > state.topId && state.topId) {
      newButton.textContent = `新しい報告 #${top.id} が届きました`;
      newButton.hidden = false;
    }
  } catch {
    return;
  }
}

let searchTimer = 0;
filters.q.addEventListener("input", () => {
  clearTimeout(searchTimer);
  searchTimer = setTimeout(() => loadList(true), 300);
});
filters.form.addEventListener("change", () => loadList(true));
filters.status.addEventListener("change", () => loadList(true));
more.addEventListener("click", () => loadList(false));
list.addEventListener("click", (event) => {
  const button = event.target.closest("button[data-id]");
  if (button) select(Number(button.dataset.id), true);
});
list.addEventListener("keydown", (event) => {
  if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
  const buttons = [...list.querySelectorAll("button[data-id]")];
  const index = buttons.indexOf(document.activeElement);
  if (index < 0) return;
  event.preventDefault();
  const next = buttons[Math.min(buttons.length - 1, Math.max(0, index + (event.key === "ArrowDown" ? 1 : -1)))];
  next.focus();
  select(Number(next.dataset.id), false);
});
document.querySelector("[data-refresh]").addEventListener("click", () => {
  newButton.hidden = true;
  loadStats();
  loadList(true);
});
newButton.addEventListener("click", () => {
  newButton.hidden = true;
  loadStats();
  loadList(true);
});

let resizeTimer = 0;
window.addEventListener("resize", () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => state.days && drawChart(state.days), 150);
});

initHeader();
loadStats();
await loadList(true);
const initialId = Number(new URLSearchParams(location.search).get("id"));
if (Number.isSafeInteger(initialId) && initialId > 0) select(initialId, false);
setInterval(checkNew, 60000);
