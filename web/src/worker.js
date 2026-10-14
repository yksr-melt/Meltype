/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

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
  return new HTMLRewriter()
    .on('meta[property="og:image"]', new AbsoluteUrl("content", url.origin))
    .on('meta[property="og:url"]', new AbsoluteUrl("content", url.origin))
    .on('link[rel="canonical"]', new AbsoluteUrl("href", url.origin))
    .transform(response);
}

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
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
};
