/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Konayukiw */

import { readFileSync, readdirSync } from "node:fs";
import { DatabaseSync } from "node:sqlite";

const MIGRATIONS = new URL("../migrations/", import.meta.url);

class Statement {
  constructor(db, sql, args = []) {
    this.db = db;
    this.sql = sql;
    this.args = args;
  }

  bind(...args) {
    return new Statement(this.db, this.sql, args);
  }

  returnsRows() {
    return /^\s*(select|with)\b/i.test(this.sql) || /\breturning\b/i.test(this.sql);
  }

  async first() {
    return this.db.prepare(this.sql).get(...this.args) ?? null;
  }

  async all() {
    return { results: this.db.prepare(this.sql).all(...this.args), success: true, meta: {} };
  }

  async run() {
    if (this.returnsRows()) return this.all();
    const info = this.db.prepare(this.sql).run(...this.args);
    return { results: [], success: true, meta: { changes: Number(info.changes), last_row_id: Number(info.lastInsertRowid) } };
  }
}

export class FakeD1 {
  constructor() {
    this.db = new DatabaseSync(":memory:");
    this.db.exec("PRAGMA foreign_keys = ON");
    for (const name of readdirSync(MIGRATIONS).filter((n) => n.endsWith(".sql")).sort()) {
      this.db.exec(readFileSync(new URL(name, MIGRATIONS), "utf8"));
    }
  }

  prepare(sql) {
    return new Statement(this.db, sql);
  }

  async batch(statements) {
    this.db.exec("BEGIN");
    try {
      const results = [];
      for (const statement of statements) results.push(await statement.run());
      this.db.exec("COMMIT");
      return results;
    } catch (e) {
      this.db.exec("ROLLBACK");
      throw e;
    }
  }
}

export class FakeR2 {
  constructor() {
    this.objects = new Map();
  }

  async put(key, value, options = {}) {
    const bytes = value instanceof Uint8Array ? value : new Uint8Array(await new Response(value).arrayBuffer());
    this.objects.set(key, { bytes, options });
  }

  async get(key, options = {}) {
    const object = this.objects.get(key);
    if (!object) return null;
    let bytes = object.bytes;
    if (options.range) bytes = bytes.slice(options.range.offset, options.range.offset + options.range.length);
    return { body: new Blob([bytes]).stream(), size: object.bytes.length };
  }

  async delete(keys) {
    for (const key of Array.isArray(keys) ? keys : [keys]) this.objects.delete(key);
  }
}

export class FakeKV {
  constructor() {
    this.values = new Map();
    this.puts = [];
  }

  async getWithMetadata(key) {
    const entry = this.values.get(key);
    return entry ? { value: entry.value, metadata: entry.metadata ?? null, cacheStatus: null } : { value: null, metadata: null, cacheStatus: null };
  }

  async put(key, value, options = {}) {
    this.values.set(key, { value, metadata: options.metadata });
    this.puts.push({ key, value, options });
  }
}

export const ASSETS = {
  async fetch(request) {
    return new Response("<!doctype html><title>dashboard</title>", { headers: { "content-type": "text/html; charset=utf-8" } });
  },
};
