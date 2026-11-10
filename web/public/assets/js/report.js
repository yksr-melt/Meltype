/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { escapeHtml, getJson, icon, initHeader } from "./common.js";
import { FILE_ACCEPT, FORMS, LIMITS, fileTypeOf, formById, formByTemplate, formatBytes, maxLength, validateFields } from "./report-schema.js";

const DRAFT_KEY = "meltype-report-draft";
const FORM_ICONS = { bug: "bug", misdetection: "wand", dictionary: "book", idea: "sparkle" };
const GROUP_ICONS = { image: "image", video: "video", text: "file", json: "braces" };

const root = document.querySelector("[data-report]");
const formElement = root.querySelector("[data-form]");
const picker = root.querySelector("[data-picker]");
const body = root.querySelector("[data-form-body]");
const note = root.querySelector("[data-form-note]");
const requiredBox = root.querySelector("[data-required]");
const optionalBox = root.querySelector("[data-optional]");
const fileInput = root.querySelector("[data-file-input]");
const dropzone = root.querySelector("[data-dropzone]");
const fileList = root.querySelector("[data-file-list]");
const filesTotal = root.querySelector("[data-files-total]");
const filesError = root.querySelector("[data-files-error]");
const submitButton = root.querySelector("[data-submit]");
const status = root.querySelector("[data-status]");
const done = root.querySelector("[data-done]");
const trap = root.querySelector("[data-trap]");
const startedAt = performance.now();

const state = { form: null, values: {}, files: [], sending: false };

function loadDraft() {
  try {
    const draft = JSON.parse(sessionStorage.getItem(DRAFT_KEY) || "null");
    if (draft && typeof draft === "object") {
      state.values = draft.values || {};
      return draft.form || null;
    }
  } catch {
    return null;
  }
  return null;
}

function saveDraft() {
  try {
    sessionStorage.setItem(DRAFT_KEY, JSON.stringify({ form: state.form && state.form.id, values: state.values }));
  } catch {
    return;
  }
}

function clearDraft() {
  try {
    sessionStorage.removeItem(DRAFT_KEY);
  } catch {
    return;
  }
}

function matchOption(options, value) {
  if (!value) return "";
  if (options.includes(value)) return value;
  const lower = value.toLowerCase();
  return (
    options.find((option) => option.toLowerCase().startsWith(lower)) ||
    options.find((option) => lower.startsWith(option.toLowerCase().split(" ")[0])) ||
    ""
  );
}

function applyQuery() {
  const params = new URLSearchParams(location.search);
  const form = formById(params.get("form")) || formByTemplate(params.get("template"));
  for (const [key, value] of params) {
    if (key === "form" || key === "template" || !value) continue;
    state.values[key] = value;
  }
  return form ? form.id : null;
}

function renderPicker() {
  picker.insertAdjacentHTML(
    "beforeend",
    FORMS.map((form) => {
      return `<label class="form-option card">
        <input type="radio" name="form" value="${form.id}">
        ${icon(FORM_ICONS[form.id])}
        <b>${escapeHtml(form.label)}</b>
        <small>${escapeHtml(form.summary)}</small>
      </label>`;
    }).join(""),
  );
  picker.addEventListener("change", (event) => {
    if (event.target.name === "form") selectForm(event.target.value, true);
  });
}

function fieldHtml(field, required) {
  const id = `f-${field.id}`;
  const wide = field.type === "textarea" || field.type === "code" || field.type === "checks";
  const hint = field.hint ? `<span class="hint" id="${id}-hint">${escapeHtml(field.hint)}</span>` : "";
  const describedBy = [field.hint ? `${id}-hint` : "", `${id}-error`].filter(Boolean).join(" ");
  const common = `id="${id}" name="${field.id}" aria-describedby="${describedBy}"${required ? ' aria-required="true"' : ""}`;
  const placeholder = field.placeholder ? ` placeholder="${escapeHtml(field.placeholder)}"` : "";
  let control = "";
  let counter = "";
  if (field.type === "select") {
    control = `<select class="select" ${common}><option value="">選んでください</option>${field.options.map((o) => `<option>${escapeHtml(o)}</option>`).join("")}</select>`;
  } else if (field.type === "checks") {
    control = `<div class="checks">${field.options
      .map((o, i) => `<label class="check"><input type="checkbox" name="${field.id}" value="${escapeHtml(o)}" id="${id}-${i}">${escapeHtml(o)}</label>`)
      .join("")}</div>`;
    return `<div class="field is-wide" data-field="${field.id}"><fieldset aria-describedby="${describedBy}"><legend>${escapeHtml(field.label)}</legend>${hint}${control}</fieldset><span class="field-error" id="${id}-error"></span></div>`;
  } else if (field.type === "text") {
    control = `<input class="input" type="text" autocomplete="off" ${common}${placeholder}>`;
  } else {
    control = `<textarea class="textarea${field.type === "code" ? " is-code" : ""}" ${common}${placeholder}${field.type === "code" ? ' spellcheck="false" wrap="off"' : ""} rows="${field.type === "code" ? 6 : 4}"></textarea>`;
    counter = `<span class="counter" data-counter="${field.id}"></span>`;
  }
  return `<div class="field${wide ? " is-wide" : ""}" data-field="${field.id}">
    <label for="${id}">${escapeHtml(field.label)}${required ? '<span class="req">必須</span>' : ""}</label>
    ${wide ? hint + control : control + hint}${counter}
    <span class="field-error" id="${id}-error"></span>
  </div>`;
}

function fillValues(form) {
  for (const field of [...form.required, ...form.optional]) {
    const value = state.values[field.id];
    if (value == null) continue;
    if (field.type === "checks") {
      const list = Array.isArray(value) ? value : String(value).split(",");
      formElement.querySelectorAll(`input[name="${field.id}"]`).forEach((box) => (box.checked = list.includes(box.value)));
    } else if (field.type === "select") {
      formElement.querySelector(`#f-${field.id}`).value = matchOption(field.options, String(value));
    } else {
      formElement.querySelector(`#f-${field.id}`).value = String(value);
    }
  }
  updateCounters();
}

function selectForm(id, focus) {
  const form = formById(id);
  if (!form) return;
  state.form = form;
  const radio = picker.querySelector(`input[value="${id}"]`);
  if (radio) radio.checked = true;
  note.innerHTML = `${icon("info")}<span>${escapeHtml(form.note)}</span>`;
  requiredBox.innerHTML = form.required.map((field) => fieldHtml(field, true)).join("");
  optionalBox.innerHTML = form.optional.map((field) => fieldHtml(field, false)).join("");
  body.hidden = false;
  fillValues(form);
  saveDraft();
  if (focus) {
    const first = requiredBox.querySelector("input, select, textarea");
    body.scrollIntoView({ behavior: "smooth", block: "start" });
    if (first) setTimeout(() => first.focus({ preventScroll: true }), 350);
  }
}

function readValue(field) {
  if (field.type === "checks") return [...formElement.querySelectorAll(`input[name="${field.id}"]:checked`)].map((box) => box.value);
  const element = formElement.querySelector(`#f-${field.id}`);
  return element ? element.value : "";
}

function collect() {
  const values = {};
  for (const field of [...state.form.required, ...state.form.optional]) values[field.id] = readValue(field);
  return values;
}

function updateCounters() {
  if (!state.form) return;
  formElement.querySelectorAll("[data-counter]").forEach((counter) => {
    const field = [...state.form.required, ...state.form.optional].find((f) => f.id === counter.dataset.counter);
    const length = readValue(field).length;
    const max = maxLength(field);
    counter.textContent = length > max * 0.6 ? `${length.toLocaleString("ja-JP")} / ${max.toLocaleString("ja-JP")}` : "";
    counter.classList.toggle("is-over", length > max);
  });
}

function showErrors(errors) {
  formElement.querySelectorAll("[data-field]").forEach((element) => {
    const message = errors[element.dataset.field];
    element.classList.toggle("is-invalid", Boolean(message));
    const target = element.querySelector(".field-error");
    target.textContent = message || "";
    const control = element.querySelector("input:not([type=checkbox]), select, textarea");
    if (control) control.setAttribute("aria-invalid", message ? "true" : "false");
  });
}

function validateOne(fieldId) {
  if (!state.form) return;
  const { errors } = validateFields(state.form, collect());
  const element = formElement.querySelector(`[data-field="${fieldId}"]`);
  if (!element || !element.classList.contains("is-invalid")) return;
  const message = errors[fieldId];
  element.classList.toggle("is-invalid", Boolean(message));
  element.querySelector(".field-error").textContent = message || "";
}

function onInput(event) {
  const name = event.target.name;
  if (!name || !state.form || name === "form" || name === "website") return;
  const field = [...state.form.required, ...state.form.optional].find((f) => f.id === name);
  if (!field) return;
  state.values[name] = readValue(field);
  saveDraft();
  updateCounters();
  validateOne(name);
}

function setStatus(kind, message) {
  status.innerHTML = message ? `<p class="callout is-${kind}">${icon(kind === "danger" ? "alert" : "info")}<span>${escapeHtml(message)}</span></p>` : "";
}

function fileRules() {
  root.querySelector("[data-file-rules]").textContent = `画像 (PNG・JPEG・WebP・GIF)・動画 (MP4・WebM)・ログ (.log・.txt)・設定 (.json)。${LIMITS.maxFiles} ファイル・1 ファイル ${formatBytes(LIMITS.maxFileBytes)}・合計 ${formatBytes(LIMITS.maxTotalBytes)} まで`;
  fileInput.accept = FILE_ACCEPT;
}

function totalSize() {
  return state.files.reduce((sum, entry) => sum + entry.file.size, 0);
}

function renderFiles() {
  fileList.innerHTML = "";
  for (const entry of state.files) {
    const item = document.createElement("li");
    item.className = "file-item";
    entry.element = item;
    const thumb = document.createElement("span");
    thumb.className = "file-thumb";
    if (entry.type.group === "image" || entry.type.group === "video") {
      entry.url ||= URL.createObjectURL(entry.file);
      const media = document.createElement(entry.type.group === "image" ? "img" : "video");
      media.src = entry.url;
      if (media.tagName === "VIDEO") media.muted = true;
      media.alt = "";
      thumb.append(media);
    } else {
      thumb.innerHTML = icon(GROUP_ICONS[entry.type.group]);
    }
    const meta = document.createElement("span");
    meta.className = "file-meta";
    meta.innerHTML = `<b>${escapeHtml(entry.file.name)}</b><small>${formatBytes(entry.file.size)}</small><span class="file-progress" hidden><span></span></span>`;
    const remove = document.createElement("button");
    remove.type = "button";
    remove.className = "icon-btn";
    remove.innerHTML = `${icon("trash")}<span class="sr-only">${escapeHtml(entry.file.name)} を外す</span>`;
    remove.addEventListener("click", () => {
      if (state.sending) return;
      if (entry.url) URL.revokeObjectURL(entry.url);
      state.files = state.files.filter((e) => e !== entry);
      renderFiles();
    });
    item.append(thumb, meta, remove);
    fileList.append(item);
  }
  filesTotal.textContent = state.files.length ? `${state.files.length} / ${LIMITS.maxFiles} ファイル・${formatBytes(totalSize())} / ${formatBytes(LIMITS.maxTotalBytes)}` : "";
}

function addFiles(list) {
  // 添付を受け付けていないとき (R2 が無い) は何もしない
  if (root.hasAttribute("data-no-attachments")) return;
  const problems = [];
  for (const original of list) {
    let file = original;
    if (!fileTypeOf(file.name) && file.type.startsWith("image/")) {
      const ext = { "image/png": "png", "image/jpeg": "jpg", "image/webp": "webp", "image/gif": "gif" }[file.type];
      if (ext) file = new File([file], `screenshot-${new Date().toISOString().replace(/[-:T]/g, "").slice(0, 14)}.${ext}`, { type: file.type });
    }
    const type = fileTypeOf(file.name);
    if (!type) {
      problems.push(`${file.name} は添付できない種類です`);
      continue;
    }
    if (file.size === 0) {
      problems.push(`${file.name} は空のファイルです`);
      continue;
    }
    if (file.size > LIMITS.maxFileBytes) {
      problems.push(`${file.name} は大きすぎます (1 ファイル ${formatBytes(LIMITS.maxFileBytes)} まで)`);
      continue;
    }
    if (state.files.length >= LIMITS.maxFiles) {
      problems.push(`添付は ${LIMITS.maxFiles} ファイルまでです`);
      break;
    }
    if (totalSize() + file.size > LIMITS.maxTotalBytes) {
      problems.push(`${file.name} を足すと合計 ${formatBytes(LIMITS.maxTotalBytes)} を超えます`);
      continue;
    }
    state.files.push({ file, type });
  }
  filesError.textContent = problems.join("。");
  filesError.style.display = problems.length ? "block" : "";
  renderFiles();
}

function initFiles() {
  fileRules();
  fileInput.addEventListener("change", () => {
    addFiles([...fileInput.files]);
    fileInput.value = "";
  });
  ["dragenter", "dragover"].forEach((type) =>
    dropzone.addEventListener(type, (event) => {
      event.preventDefault();
      dropzone.classList.add("is-over");
    }),
  );
  ["dragleave", "drop"].forEach((type) => dropzone.addEventListener(type, () => dropzone.classList.remove("is-over")));
  dropzone.addEventListener("drop", (event) => {
    event.preventDefault();
    if (event.dataTransfer && event.dataTransfer.files.length) addFiles([...event.dataTransfer.files]);
  });
  document.addEventListener("paste", (event) => {
    if (!state.form || state.sending) return;
    const files = [...(event.clipboardData ? event.clipboardData.files : [])];
    if (!files.length) return;
    event.preventDefault();
    addFiles(files);
    dropzone.scrollIntoView({ behavior: "smooth", block: "center" });
  });
}

function uploadOne(id, token, index, entry) {
  return new Promise((resolve, reject) => {
    const progress = entry.element.querySelector(".file-progress");
    const bar = progress.firstElementChild;
    progress.hidden = false;
    entry.element.classList.remove("is-error");
    const request = new XMLHttpRequest();
    request.open("PUT", `/api/reports/${id}/files/${index}`);
    request.setRequestHeader("Authorization", `Bearer ${token}`);
    request.setRequestHeader("Content-Type", "application/octet-stream");
    request.upload.addEventListener("progress", (event) => {
      if (event.lengthComputable) bar.style.width = `${Math.round((event.loaded / event.total) * 100)}%`;
    });
    request.addEventListener("load", () => {
      if (request.status >= 200 && request.status < 300) {
        bar.style.width = "100%";
        entry.element.classList.add("is-done");
        resolve();
      } else {
        let message = `送れませんでした (${request.status})`;
        try {
          message = JSON.parse(request.responseText).error || message;
        } catch {
          message = `送れませんでした (${request.status})`;
        }
        entry.element.classList.add("is-error");
        reject(new Error(message));
      }
    });
    request.addEventListener("error", () => {
      entry.element.classList.add("is-error");
      reject(new Error("通信が切れました"));
    });
    request.send(entry.file);
  });
}

async function uploadAll(result) {
  const failed = [];
  for (const [index, entry] of state.files.entries()) {
    let lastError = null;
    for (let attempt = 0; attempt < 2; attempt++) {
      try {
        await uploadOne(result.id, result.uploadToken, index, entry);
        lastError = null;
        break;
      } catch (e) {
        lastError = e;
      }
    }
    if (lastError) failed.push(`${entry.file.name}: ${lastError.message}`);
  }
  return failed;
}

function turnstileToken() {
  const input = formElement.querySelector('[name="cf-turnstile-response"]');
  return input ? input.value : "";
}

function resetTurnstile() {
  if (window.turnstile) window.turnstile.reset();
}

function retryText(seconds) {
  if (!seconds) return "";
  const minutes = Math.ceil(seconds / 60);
  return minutes >= 60 ? `(あと約 ${Math.ceil(minutes / 60)} 時間)` : `(あと約 ${minutes} 分)`;
}

async function submit(event) {
  event.preventDefault();
  if (state.sending || !state.form) return;
  const values = collect();
  const { errors } = validateFields(state.form, values);
  showErrors(errors);
  if (Object.keys(errors).length) {
    setStatus("danger", "書いていない欄があります。赤く示した欄を確かめてください");
    const first = formElement.querySelector(".is-invalid input, .is-invalid select, .is-invalid textarea");
    if (first) first.focus();
    return;
  }
  state.sending = true;
  submitButton.disabled = true;
  setStatus("info", state.files.length ? "送っています。添付の大きさによっては少し時間がかかります" : "送っています");
  try {
    const result = await getJson("/api/reports", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({
        form: state.form.id,
        fields: values,
        files: state.files.map((entry) => ({ name: entry.file.name, size: entry.file.size })),
        website: trap.value,
        elapsed: Math.round(performance.now() - startedAt),
        turnstile: turnstileToken(),
      }),
    });
    const failed = state.files.length ? await uploadAll(result) : [];
    finish(result, failed);
  } catch (e) {
    state.sending = false;
    submitButton.disabled = false;
    resetTurnstile();
    if (e.body && e.body.fields) showErrors(e.body.fields);
    if (e.body && e.body.files) {
      filesError.textContent = e.body.files;
      filesError.style.display = "block";
    }
    const retry = e.status === 429 && e.body ? ` ${retryText(e.body.retryAfter)}` : "";
    setStatus("danger", `${e.message}${retry}`);
  }
}

function finish(result, failed) {
  state.sending = false;
  clearDraft();
  formElement.hidden = true;
  done.hidden = false;
  done.querySelector("[data-receipt]").textContent = `#${result.id}`;
  done.querySelector("[data-done-files]").textContent = failed.length
    ? `添付のうち ${failed.length} 件が届きませんでした (${failed.join("、")})。受付番号を書いて Discord か GitHub で送ってもらえると助かります。`
    : state.files.length
      ? `添付 ${state.files.length} 件も届きました。`
      : "";
  done.focus();
  done.scrollIntoView({ behavior: "smooth", block: "center" });
}

function reset() {
  state.files.forEach((entry) => entry.url && URL.revokeObjectURL(entry.url));
  state.files = [];
  const keep = {};
  for (const key of ["os", "version", "environment", "mode", "contact", "os-detail"]) {
    if (state.values[key]) keep[key] = state.values[key];
  }
  state.values = keep;
  renderFiles();
  setStatus("", "");
  submitButton.disabled = false;
  done.hidden = true;
  formElement.hidden = false;
  resetTurnstile();
  if (state.form) selectForm(state.form.id, false);
  picker.scrollIntoView({ behavior: "smooth", block: "start" });
}

initHeader();
renderPicker();
initFiles();
const draftForm = loadDraft();
const queryForm = applyQuery();
const initial = queryForm || draftForm;
if (initial) selectForm(initial, false);
formElement.addEventListener("input", onInput);
formElement.addEventListener("change", onInput);
formElement.addEventListener("submit", submit);
done.querySelector("[data-again]").addEventListener("click", reset);
