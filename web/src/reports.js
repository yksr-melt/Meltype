/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { formById, titleFor, validateFields, validateFiles } from "../public/assets/js/report-schema.js";
import { error, json, now, randomToken, readJson, sha256Hex } from "./http.js";

const SHORT_WINDOW = 10 * 60;
const SHORT_LIMIT = 3;
const DAY = 24 * 60 * 60;
const DAY_LIMIT = 10;
const UPLOAD_WINDOW = 60 * 60;
const MIN_FILL_MS = 3000;
const MAX_BODY = 200 * 1024;
const JST = 9 * 60 * 60;

function expandIpv6(ip) {
  const [head, tail] = ip.split("::");
  const left = head ? head.split(":") : [];
  const right = tail !== undefined && tail ? tail.split(":") : [];
  const fill = tail === undefined ? [] : Array(Math.max(0, 8 - left.length - right.length)).fill("0");
  return [...left, ...fill, ...right].map((part) => part.padStart(4, "0").toLowerCase());
}

function rateKey(ip) {
  if (!ip.includes(":")) return ip;
  return expandIpv6(ip.split("%")[0]).slice(0, 4).join(":") + "::/64";
}

async function hashIp(request, env) {
  const ip = request.headers.get("cf-connecting-ip") || "unknown";
  return sha256Hex(`${env.IP_HASH_SALT || "meltype-local-development"}\n${rateKey(ip)}`);
}

async function checkRate(env, ipHash, at) {
  const { results } = await env.DB.prepare("SELECT created_at FROM rate_events WHERE ip_hash = ? AND created_at > ? ORDER BY created_at")
    .bind(ipHash, at - DAY)
    .all();
  const times = results.map((row) => row.created_at);
  const recent = times.filter((time) => time > at - SHORT_WINDOW);
  if (recent.length >= SHORT_LIMIT) {
    return {
      retryAfter: Math.max(1, recent[recent.length - SHORT_LIMIT] + SHORT_WINDOW - at),
      message: `同じ場所から送れるのは 10 分に ${SHORT_LIMIT} 件までです。少し待ってからもう一度送ってください`,
    };
  }
  if (times.length >= DAY_LIMIT) {
    return {
      retryAfter: Math.max(1, times[times.length - DAY_LIMIT] + DAY - at),
      message: `同じ場所から送れるのは 1 日に ${DAY_LIMIT} 件までです。時間をおいてからもう一度送ってください`,
    };
  }
  return null;
}

export async function createReport(request, env) {
  const { body, error: invalid } = await readJson(request, MAX_BODY);
  if (invalid) return invalid;
  const form = formById(body && body.form);
  if (!form) return error(400, "報告の種類を選んでください");
  if (body.website) return error(400, "送信できませんでした");
  if (!(Number(body.elapsed) >= MIN_FILL_MS)) return error(400, "送信が速すぎます。内容を確かめてから、もう一度送ってください");

  const { errors, fields } = validateFields(form, body.fields);
  if (Object.keys(errors).length) return error(422, "書いていない所か、直してほしい所があります", { fields: errors });
  const checked = validateFiles(body.files);
  if (checked.error) return error(422, checked.error, { files: checked.error });

  const at = now();
  const ipHash = await hashIp(request, env);
  const limited = await checkRate(env, ipHash, at);
  if (limited) return error(429, limited.message, { retryAfter: limited.retryAfter });

  const files = checked.files;
  const token = files.length ? randomToken() : null;
  const title = titleFor(form, fields);
  const inserted = await env.DB.prepare(
    "INSERT INTO reports (form, title, fields, status, created_at, upload_token_hash, upload_expires_at) VALUES (?, ?, ?, ?, ?, ?, ?) RETURNING id",
  )
    .bind(form.id, title, JSON.stringify(fields), files.length ? "uploading" : "new", at, token ? await sha256Hex(token) : null, token ? at + UPLOAD_WINDOW : null)
    .first();
  const id = inserted.id;

  const statements = files.map((file, index) =>
    env.DB.prepare("INSERT INTO attachments (report_id, idx, name, mime, grp, size) VALUES (?, ?, ?, ?, ?, ?)").bind(id, index, file.name, file.mime, file.group, file.size),
  );
  statements.push(env.DB.prepare("INSERT INTO rate_events (ip_hash, created_at) VALUES (?, ?)").bind(ipHash, at));
  statements.push(env.DB.prepare("DELETE FROM rate_events WHERE created_at <= ?").bind(at - DAY));
  await env.DB.batch(statements);

  return json(
    { id, title, uploadToken: token, files: files.map((file, index) => ({ index, name: file.name, size: file.size })) },
    { status: 201 },
  );
}

export async function uploadFile(request, env, id, index) {
  const auth = request.headers.get("authorization") || "";
  const token = auth.startsWith("Bearer ") ? auth.slice(7).trim() : "";
  if (!token) return error(401, "添付の受付番号がありません");

  const report = await env.DB.prepare("SELECT upload_token_hash, upload_expires_at FROM reports WHERE id = ?").bind(id).first();
  if (!report || !report.upload_token_hash || report.upload_token_hash !== (await sha256Hex(token))) {
    return error(403, "この報告には添付できません");
  }
  const at = now();
  if (report.upload_expires_at < at) return error(410, "添付の受付時間 (1 時間) が過ぎました");

  const file = await env.DB.prepare("SELECT name, mime, size, uploaded_at FROM attachments WHERE report_id = ? AND idx = ?").bind(id, index).first();
  if (!file) return error(404, "そのファイルは報告に含まれていません");
  if (file.uploaded_at) return error(409, "このファイルはもう届いています");
  if (Number(request.headers.get("content-length")) !== file.size || !request.body) {
    return error(400, "ファイルの大きさが送信前と違います");
  }

  await env.FILES.put(`reports/${id}/${index}`, request.body, {
    httpMetadata: { contentType: file.mime },
    customMetadata: { name: encodeURIComponent(file.name) },
  });
  await env.DB.batch([
    env.DB.prepare("UPDATE attachments SET uploaded_at = ? WHERE report_id = ? AND idx = ?").bind(at, id, index),
    env.DB.prepare(
      "UPDATE reports SET status = 'new', upload_token_hash = NULL, upload_expires_at = NULL WHERE id = ? AND NOT EXISTS (SELECT 1 FROM attachments WHERE report_id = ? AND uploaded_at IS NULL)",
    ).bind(id, id),
  ]);
  return json({ ok: true });
}

function likePattern(text) {
  return "%" + text.replace(/[\\%_]/g, (c) => "\\" + c) + "%";
}

export async function listReports(env, url) {
  const where = [];
  const binds = [];
  const form = url.searchParams.get("form");
  if (form && formById(form)) {
    where.push("r.form = ?");
    binds.push(form);
  }
  const status = url.searchParams.get("status");
  if (status === "new" || status === "uploading") {
    where.push("r.status = ?");
    binds.push(status);
  }
  const q = (url.searchParams.get("q") || "").trim().slice(0, 100);
  if (q) {
    where.push("(r.title LIKE ? ESCAPE '\\' OR r.fields LIKE ? ESCAPE '\\')");
    binds.push(likePattern(q), likePattern(JSON.stringify(q).slice(1, -1)));
  }
  const before = Number(url.searchParams.get("before"));
  if (Number.isSafeInteger(before) && before > 0) {
    where.push("r.id < ?");
    binds.push(before);
  }
  const limit = Math.min(50, Math.max(1, Number(url.searchParams.get("limit")) || 30));
  const sql =
    "SELECT r.id, r.form, r.title, r.status, r.created_at, COUNT(a.idx) AS files, COALESCE(SUM(a.uploaded_at IS NOT NULL), 0) AS uploaded, " +
    "COALESCE(GROUP_CONCAT(DISTINCT a.grp), '') AS groups FROM reports r LEFT JOIN attachments a ON a.report_id = r.id" +
    (where.length ? " WHERE " + where.join(" AND ") : "") +
    " GROUP BY r.id ORDER BY r.id DESC LIMIT ?";
  const { results } = await env.DB.prepare(sql).bind(...binds, limit + 1).all();
  const more = results.length > limit;
  const reports = results.slice(0, limit).map((row) => ({ ...row, groups: row.groups ? row.groups.split(",") : [] }));
  return json({ reports, next: more ? reports[reports.length - 1].id : null });
}

export async function getReport(env, id) {
  const report = await env.DB.prepare("SELECT id, form, title, fields, status, created_at FROM reports WHERE id = ?").bind(id).first();
  if (!report) return error(404, "その報告は見つかりません");
  const { results } = await env.DB.prepare("SELECT idx, name, mime, grp, size, uploaded_at FROM attachments WHERE report_id = ? ORDER BY idx").bind(id).all();
  return json({ ...report, fields: JSON.parse(report.fields), attachments: results });
}

function parseRange(header, size) {
  const match = /^bytes=(\d*)-(\d*)$/.exec(header || "");
  if (!match || (match[1] === "" && match[2] === "")) return null;
  let start;
  let end;
  if (match[1] === "") {
    const suffix = Number(match[2]);
    start = Math.max(0, size - suffix);
    end = size - 1;
  } else {
    start = Number(match[1]);
    end = match[2] === "" ? size - 1 : Math.min(Number(match[2]), size - 1);
  }
  if (start > end || start >= size) return { invalid: true };
  return { start, end };
}

export async function getFile(env, request, id, index, url) {
  const file = await env.DB.prepare("SELECT name, mime, size, uploaded_at FROM attachments WHERE report_id = ? AND idx = ?").bind(id, index).first();
  if (!file || !file.uploaded_at) return error(404, "そのファイルは見つかりません");
  const range = parseRange(request.headers.get("range"), file.size);
  if (range && range.invalid) {
    return new Response(null, { status: 416, headers: { "content-range": `bytes */${file.size}` } });
  }
  const object = await env.FILES.get(`reports/${id}/${index}`, range ? { range: { offset: range.start, length: range.end - range.start + 1 } } : undefined);
  if (!object) return error(404, "そのファイルは見つかりません");
  const disposition = url.searchParams.get("download") ? "attachment" : "inline";
  const headers = new Headers({
    "content-type": file.mime,
    "content-disposition": `${disposition}; filename*=UTF-8''${encodeURIComponent(file.name)}`,
    "x-content-type-options": "nosniff",
    "content-security-policy": "default-src 'none'; img-src 'self'; media-src 'self'; style-src 'unsafe-inline'; sandbox",
    "cache-control": "private, max-age=3600",
    "accept-ranges": "bytes",
  });
  if (range) {
    headers.set("content-range", `bytes ${range.start}-${range.end}/${file.size}`);
    headers.set("content-length", String(range.end - range.start + 1));
    return new Response(object.body, { status: 206, headers });
  }
  headers.set("content-length", String(file.size));
  return new Response(object.body, { headers });
}

function dayLabel(day) {
  return new Date(day * DAY * 1000).toISOString().slice(0, 10);
}

export async function stats(env) {
  const at = now();
  const today = Math.floor((at + JST) / DAY);
  const [byForm, daily, extra] = await env.DB.batch([
    env.DB.prepare("SELECT form, COUNT(*) AS count FROM reports GROUP BY form"),
    env.DB.prepare("SELECT CAST((created_at + ?) / ? AS INTEGER) AS day, COUNT(*) AS count FROM reports WHERE created_at >= ? GROUP BY day").bind(JST, DAY, (today - 29) * DAY - JST),
    env.DB.prepare(
      "SELECT (SELECT COUNT(*) FROM reports) AS total, (SELECT COUNT(DISTINCT report_id) FROM attachments) AS withFiles, (SELECT COUNT(*) FROM reports WHERE status = 'uploading') AS uploading",
    ),
  ]);
  const counts = new Map(daily.results.map((row) => [row.day, row.count]));
  const days = [];
  for (let day = today - 29; day <= today; day++) days.push({ day: dayLabel(day), count: counts.get(day) || 0 });
  const summary = extra.results[0] || {};
  return json({
    total: summary.total || 0,
    withFiles: summary.withFiles || 0,
    uploading: summary.uploading || 0,
    today: days[days.length - 1].count,
    week: days.slice(-7).reduce((sum, d) => sum + d.count, 0),
    byForm: Object.fromEntries(byForm.results.map((row) => [row.form, row.count])),
    days,
  });
}
