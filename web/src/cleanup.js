/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { now } from "./http.js";

const DAY = 24 * 60 * 60;
const BATCH = 200;

function days(value, fallback) {
  const n = Number(value);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

async function deleteObjects(env, rows) {
  if (!env.FILES) return;
  const keys = rows.map((row) => `reports/${row.report_id}/${row.idx}`);
  for (let i = 0; i < keys.length; i += 1000) await env.FILES.delete(keys.slice(i, i + 1000));
}

export async function cleanup(env, at = now()) {
  const result = { staleReports: 0, staleFiles: 0, expiredFiles: 0, expiredReports: 0, rateEvents: 0 };

  const stale = await env.DB.prepare(
    "SELECT a.report_id, a.idx FROM attachments a JOIN reports r ON r.id = a.report_id " +
      "WHERE r.status = 'uploading' AND r.upload_expires_at < ? AND a.uploaded_at IS NULL LIMIT ?",
  )
    .bind(at, BATCH)
    .all();
  if (stale.results.length) {
    await deleteObjects(env, stale.results);
    await env.DB.batch(stale.results.map((row) => env.DB.prepare("DELETE FROM attachments WHERE report_id = ? AND idx = ?").bind(row.report_id, row.idx)));
    result.staleFiles = stale.results.length;
  }
  const closed = await env.DB.prepare(
    "UPDATE reports SET status = 'new', upload_token_hash = NULL, upload_expires_at = NULL " +
      "WHERE status = 'uploading' AND upload_expires_at < ? AND NOT EXISTS (SELECT 1 FROM attachments WHERE report_id = reports.id AND uploaded_at IS NULL)",
  )
    .bind(at)
    .run();
  result.staleReports = closed.meta.changes || 0;

  const fileBefore = at - days(env.FILE_RETENTION_DAYS, 30) * DAY;
  const expiredFiles = await env.DB.prepare(
    "SELECT a.report_id, a.idx FROM attachments a JOIN reports r ON r.id = a.report_id WHERE r.created_at < ? LIMIT ?",
  )
    .bind(fileBefore, BATCH)
    .all();
  if (expiredFiles.results.length) {
    await deleteObjects(env, expiredFiles.results);
    await env.DB.batch(
      expiredFiles.results.map((row) => env.DB.prepare("DELETE FROM attachments WHERE report_id = ? AND idx = ?").bind(row.report_id, row.idx)),
    );
    result.expiredFiles = expiredFiles.results.length;
  }

  const reportBefore = at - days(env.REPORT_RETENTION_DAYS, 180) * DAY;
  const oldFiles = await env.DB.prepare(
    "SELECT a.report_id, a.idx FROM attachments a JOIN reports r ON r.id = a.report_id WHERE r.created_at < ? LIMIT ?",
  )
    .bind(reportBefore, BATCH)
    .all();
  if (oldFiles.results.length) {
    await deleteObjects(env, oldFiles.results);
    await env.DB.batch(oldFiles.results.map((row) => env.DB.prepare("DELETE FROM attachments WHERE report_id = ? AND idx = ?").bind(row.report_id, row.idx)));
  }
  const removed = await env.DB.prepare(
    "DELETE FROM reports WHERE id IN (SELECT id FROM reports WHERE created_at < ? AND NOT EXISTS (SELECT 1 FROM attachments WHERE report_id = reports.id) LIMIT ?)",
  )
    .bind(reportBefore, BATCH)
    .run();
  result.expiredReports = removed.meta.changes || 0;

  const rate = await env.DB.prepare("DELETE FROM rate_events WHERE created_at <= ?").bind(at - DAY).run();
  result.rateEvents = rate.meta.changes || 0;

  return result;
}
