/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

export const ICONS = "/assets/img/icons.svg";

export function icon(name, extra = "") {
  return `<svg class="ic${extra ? " " + extra : ""}" aria-hidden="true"><use href="${ICONS}#i-${name}"/></svg>`;
}

export function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
}

export function formatNumber(value) {
  return Number(value || 0).toLocaleString("ja-JP");
}

export function formatDate(value, withTime = false) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const options = { year: "numeric", month: "long", day: "numeric", timeZone: "Asia/Tokyo" };
  if (withTime) Object.assign(options, { hour: "2-digit", minute: "2-digit" });
  return date.toLocaleString("ja-JP", options);
}

export function relativeTime(value) {
  const seconds = Math.round((Date.now() - new Date(value).getTime()) / 1000);
  if (seconds < 60) return "たった今";
  if (seconds < 3600) return `${Math.floor(seconds / 60)} 分前`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} 時間前`;
  if (seconds < 86400 * 30) return `${Math.floor(seconds / 86400)} 日前`;
  return formatDate(value);
}

let toastTimer = 0;

export function toast(message) {
  let element = document.querySelector(".toast");
  if (!element) {
    element = document.createElement("div");
    element.className = "toast";
    element.setAttribute("role", "status");
    document.body.append(element);
  }
  element.textContent = message;
  element.classList.add("is-shown");
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => element.classList.remove("is-shown"), 2200);
}

export async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.append(area);
    area.select();
    const ok = document.execCommand("copy");
    area.remove();
    return ok;
  }
}

export function initCopyButtons(root = document) {
  root.querySelectorAll("[data-copy]").forEach((button) => {
    button.addEventListener("click", async () => {
      const ok = await copyText(button.dataset.copy);
      toast(ok ? "コピーしました" : "コピーできませんでした");
      if (ok) {
        button.classList.add("is-done");
        setTimeout(() => button.classList.remove("is-done"), 1400);
      }
    });
  });
}

// いま使っているのがマウスかタッチかを html の .is-mouse で示す。
// (hover: hover) はマウスもつながるスマホ・タブレットでも当てはまり、タップしたカードが浮いたまま残るため、
// 浮き上がりはマウスで動かしているあいだだけにする。
function trackPointer() {
  const root = document.documentElement;
  const set = (event) => root.classList.toggle("is-mouse", event.pointerType === "mouse");
  window.addEventListener("pointermove", set, { passive: true });
  window.addEventListener("pointerdown", set, { passive: true });
}

export function initHeader() {
  trackPointer();
  const header = document.querySelector("[data-header]");
  const toggle = document.querySelector("[data-nav-toggle]");
  const nav = document.querySelector("[data-nav]");
  const setOpen = (open) => {
    if (!toggle || !nav) return;
    nav.classList.toggle("is-open", open);
    toggle.setAttribute("aria-expanded", String(open));
    toggle.querySelector("use").setAttribute("href", `${ICONS}#i-${open ? "close" : "menu"}`);
  };
  if (header) {
    const bar = header.querySelector(".header-inner");
    let scrolled = false;
    let openedAt = 0;
    let tappedIsland = false;
    const setIsland = (open) => {
      header.classList.toggle("is-open", open);
      openedAt = window.scrollY;
      if (!open) setOpen(false);
    };
    const update = () => {
      const y = window.scrollY;
      if (!scrolled && y > 80) scrolled = true;
      else if (scrolled && y < 24) scrolled = false;
      header.classList.toggle("is-scrolled", scrolled);
      if (header.classList.contains("is-open") && Math.abs(y - openedAt) > 160) setIsland(false);
    };
    update();
    window.addEventListener("scroll", update, { passive: true });
    // マウスで乗せたときは、少し待ってから広げ、離れても少し待ってから縮める。
    // 通りすがりのカーソルでヒョっと広がったり、端をかすめただけで閉じたりしないように。
    let peekTimer = 0;
    const setPeek = (on, delay) => {
      clearTimeout(peekTimer);
      peekTimer = setTimeout(() => header.classList.toggle("is-peek", on), delay);
    };
    bar.addEventListener("pointerenter", (event) => {
      if (event.pointerType === "mouse") setPeek(true, scrolled ? 90 : 0);
    });
    bar.addEventListener("pointerleave", (event) => {
      if (event.pointerType === "mouse") setPeek(false, 320);
    });
    bar.addEventListener("pointerdown", (event) => {
      tappedIsland = event.pointerType !== "mouse" && scrolled && !header.classList.contains("is-open");
    });
    bar.addEventListener(
      "click",
      (event) => {
        if (!tappedIsland) return;
        tappedIsland = false;
        event.preventDefault();
        event.stopPropagation();
        setIsland(true);
      },
      true,
    );
    document.addEventListener("click", (event) => {
      if (header.classList.contains("is-open") && !header.contains(event.target)) setIsland(false);
    });
  }
  if (toggle && nav) {
    toggle.addEventListener("click", () => setOpen(!nav.classList.contains("is-open")));
    nav.addEventListener("click", (event) => {
      if (event.target.closest("a")) setOpen(false);
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && nav.classList.contains("is-open")) {
        setOpen(false);
        toggle.focus();
      }
    });
  }
}

export function detectOs() {
  const platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
  const agent = navigator.userAgent || "";
  if (/android|iphone|ipad|ipod/i.test(agent)) return "mobile";
  if (/win/i.test(platform) || /windows/i.test(agent)) return "windows";
  if (/mac/i.test(platform) || /mac os x/i.test(agent)) return "mac";
  if (/linux|x11/i.test(platform) || /linux/i.test(agent)) return "linux";
  return "unknown";
}

export const prefersReducedMotion = () => window.matchMedia("(prefers-reduced-motion: reduce)").matches;

export async function getJson(url, options) {
  const response = await fetch(url, options);
  let body = null;
  try {
    body = await response.json();
  } catch {
    body = null;
  }
  if (!response.ok) {
    const error = new Error((body && body.error) || `通信できませんでした (${response.status})`);
    error.status = response.status;
    error.body = body;
    throw error;
  }
  return body;
}
