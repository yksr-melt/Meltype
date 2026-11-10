/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { error, now } from "./http.js";

const FRESH = 60 * 60;
const KEEP = 24 * 60 * 60;
const RETRY = 60;
const CACHE_KEY = "github";
const RELEASES_SHOWN = 6;
const NOTES_LIMIT = 8000;
const AI_ACCOUNTS = new Set(["claude", "cursoragent", "copilot", "devin-ai-integration"]);

async function api(path, env) {
  const headers = {
    "user-agent": "meltype-web",
    accept: "application/vnd.github+json",
    "x-github-api-version": "2022-11-28",
  };
  if (env.GITHUB_TOKEN) headers.authorization = `Bearer ${env.GITHUB_TOKEN}`;
  const response = await fetch(`https://api.github.com${path}`, { headers });
  if (!response.ok) throw new Error(`GitHub ${path}: ${response.status}`);
  return response.json();
}

async function readme(repo) {
  const response = await fetch(`https://raw.githubusercontent.com/${repo}/HEAD/README.md`, { headers: { "user-agent": "meltype-web" } });
  return response.ok ? response.text() : "";
}

export function parseSponsors(markdown) {
  const start = markdown.indexOf("<!-- sponsors:start -->");
  const end = markdown.indexOf("<!-- sponsors:end -->");
  if (start < 0 || end < start) return [];
  const block = markdown.slice(start, end);
  const logins = [];
  for (const match of block.matchAll(/href="https:\/\/github\.com\/([A-Za-z0-9-]+)"/g)) {
    if (!logins.includes(match[1])) logins.push(match[1]);
  }
  return logins;
}

async function fetchSummary(env) {
  const repo = env.GITHUB_REPO || "yksr-melt/Meltype";
  const [info, releases, contributors, text] = await Promise.all([
    api(`/repos/${repo}`, env),
    api(`/repos/${repo}/releases?per_page=100`, env),
    api(`/repos/${repo}/contributors?per_page=100`, env),
    readme(repo).catch(() => ""),
  ]);
  const published = releases.filter((release) => !release.draft);
  const totalDownloads = published.reduce((sum, release) => sum + release.assets.reduce((s, asset) => s + asset.download_count, 0), 0);
  return {
    repo,
    url: info.html_url,
    stars: info.stargazers_count,
    forks: info.forks_count,
    openIssues: info.open_issues_count,
    totalDownloads,
    releases: published.slice(0, RELEASES_SHOWN).map((release) => ({
      tag: release.tag_name,
      name: release.name || release.tag_name,
      prerelease: release.prerelease,
      publishedAt: release.published_at,
      url: release.html_url,
      notes: (release.body || "").replace(/\r\n?/g, "\n").slice(0, NOTES_LIMIT),
      assets: release.assets.map((asset) => ({
        name: asset.name,
        size: asset.size,
        downloads: asset.download_count,
        url: asset.browser_download_url,
        digest: asset.digest || null,
      })),
    })),
    contributors: contributors
      .filter((person) => person.type === "User" && !person.login.endsWith("[bot]") && !AI_ACCOUNTS.has(person.login.toLowerCase()))
      .map((person) => ({ login: person.login, avatar: person.avatar_url, url: person.html_url, contributions: person.contributions })),
    sponsors: parseSponsors(text),
    fetchedAt: new Date().toISOString(),
  };
}

let refreshStartedAt = 0;

async function refresh(env) {
  const value = JSON.stringify(await fetchSummary(env));
  await env.CACHE.put(CACHE_KEY, value, { expirationTtl: KEEP, metadata: { fetchedAt: now() } });
  return value;
}

function respond(value, state) {
  return new Response(value, {
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "public, max-age=300",
      "x-meltype-cache": state,
    },
  });
}

export async function githubSummary(env, ctx) {
  const cached = await env.CACHE.getWithMetadata(CACHE_KEY);
  if (cached.value) {
    const fetchedAt = Number(cached.metadata && cached.metadata.fetchedAt) || 0;
    if (now() - fetchedAt >= FRESH) {
      if (now() - refreshStartedAt >= RETRY) {
        refreshStartedAt = now();
        ctx.waitUntil(refresh(env).catch((e) => console.error("GitHub の情報を更新できませんでした", e)));
      }
      return respond(cached.value, "stale");
    }
    return respond(cached.value, "hit");
  }
  try {
    return respond(await refresh(env), "miss");
  } catch (e) {
    console.error("GitHub の情報を読めませんでした", e);
    return error(502, "GitHub の情報を読めませんでした");
  }
}

export function _resetGithubCache() {
  refreshStartedAt = 0;
}
