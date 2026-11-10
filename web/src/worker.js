/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { requireAccess } from "./access.js";
import { cleanup } from "./cleanup.js";
import { githubSummary } from "./github.js";
import { error } from "./http.js";
import { createReport, getFile, getReport, listReports, stats, uploadFile } from "./reports.js";

const routes = [
  ["POST", /^\/api\/reports$/, (c) => createReport(c.request, c.env)],
  ["PUT", /^\/api\/reports\/(\d+)\/files\/(\d+)$/, (c) => uploadFile(c.request, c.env, Number(c.match[1]), Number(c.match[2]))],
  ["GET", /^\/api\/github$/, (c) => githubSummary(c.env, c.ctx)],
  ["GET", /^\/api\/dashboard\/stats$/, (c) => stats(c.env)],
  ["GET", /^\/api\/dashboard\/reports$/, (c) => listReports(c.env, c.url)],
  ["GET", /^\/api\/dashboard\/reports\/(\d+)$/, (c) => getReport(c.env, Number(c.match[1]))],
  ["GET", /^\/api\/dashboard\/reports\/(\d+)\/files\/(\d+)$/, (c) => getFile(c.env, c.request, Number(c.match[1]), Number(c.match[2]), c.url)],
];

class SetAttribute {
  constructor(attribute, value) {
    this.attribute = attribute;
    this.value = value;
  }

  element(element) {
    element.setAttribute(this.attribute, this.value);
  }
}

class Hide {
  element(element) {
    element.setAttribute("hidden", "");
  }
}

class AbsoluteUrl {
  constructor(attribute, origin) {
    this.attribute = attribute;
    this.origin = origin;
  }

  element(element) {
    const value = element.getAttribute(this.attribute);
    if (value && value.startsWith("/")) element.setAttribute(this.attribute, new URL(value, this.origin).href);
  }
}

async function page(request, env, url) {
  const response = await env.ASSETS.fetch(request);
  if (!(response.headers.get("content-type") || "").includes("text/html")) return response;
  const origin = env.SITE_ORIGIN || url.origin;
  let rewriter = new HTMLRewriter();
  // R2 (添付の置き場) が無いあいだは、添付の欄を出さない (報告の文章だけ受け付ける)
  if (!env.FILES) rewriter = rewriter.on("[data-attachments]", new Hide()).on("[data-report]", new SetAttribute("data-no-attachments", ""));
  return rewriter
    .on('meta[property="og:image"]', new AbsoluteUrl("content", origin))
    .on('meta[property="og:url"]', new AbsoluteUrl("content", origin))
    .on('link[rel="canonical"]', new AbsoluteUrl("href", origin))
    .on("[data-turnstile]", new SetAttribute("data-sitekey", env.TURNSTILE_SITE_KEY || ""))
    .transform(response);
}

function isAdminPath(pathname) {
  return pathname === "/dashboard" || pathname.startsWith("/dashboard/") || pathname.startsWith("/api/dashboard/");
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    if (isAdminPath(url.pathname)) {
      const denied = await requireAccess(request, env);
      if (denied) return denied;
    }
    if (!url.pathname.startsWith("/api/")) return page(request, env, url);
    const matching = routes.filter(([, pattern]) => pattern.test(url.pathname));
    if (!matching.length) return error(404, "見つかりません");
    const route = matching.find(([method]) => method === request.method || (method === "GET" && request.method === "HEAD"));
    if (!route) return error(405, "この方法では使えません");
    try {
      return await route[2]({ request, env, ctx, url, match: route[1].exec(url.pathname) });
    } catch (e) {
      console.error(e);
      return error(500, "サーバーで問題が起きました。時間をおいてもう一度試してください");
    }
  },

  async scheduled(controller, env, ctx) {
    ctx.waitUntil(
      cleanup(env)
        .then((result) => console.log("掃除しました", JSON.stringify(result)))
        .catch((e) => console.error("掃除に失敗しました", e)),
    );
  },
};
