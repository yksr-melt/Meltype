/* SPDX-License-Identifier: GPL-3.0-or-later */
/* Copyright (C) 2026 Yukishiro */

// ダッシュボードの読み取りを MCP (Streamable HTTP) で出す。Claude などの AI から報告を読めるようにする。
// /api/dashboard/ の下に置くので、人のダッシュボードと同じく Access の内側にある (AI には Access のサービス トークンを渡す)。
// 決まり: 読むだけの道具にする。消す操作は人がダッシュボードで決める。
// 決まり: 状態を持たない (セッションを作らず、POST 1 回に JSON 1 つで答える)。SSE も使わない。

import { json, readJson } from "./http.js";
import { getReport, listReports, stats } from "./reports.js";
import { flaggedSessions, sentenceCounts } from "./typing.js";

const VERSIONS = ["2025-06-18", "2025-03-26", "2024-11-05"];
const MAX_BYTES = 64 * 1024;

const tools = [
  {
    name: "dashboard_stats",
    description: "報告の件数 (合計・今日・7 日・フォームごと・直近 30 日の日ごと)",
    inputSchema: { type: "object", properties: {} },
    run: () => stats,
  },
  {
    name: "list_reports",
    description: "報告の一覧を新しい順に返す。next が null でなければ、before に next を入れて続きを読む",
    inputSchema: {
      type: "object",
      properties: {
        form: { type: "string", description: "フォームの id で絞る" },
        status: { type: "string", enum: ["new", "uploading"] },
        q: { type: "string", description: "題名と本文から探す語" },
        before: { type: "integer", description: "この id より古いものを返す" },
        limit: { type: "integer", minimum: 1, maximum: 50 },
      },
    },
    run: (args) => (env) => {
      const url = new URL("https://mcp.invalid/");
      for (const key of ["form", "status", "q", "before", "limit"]) if (args[key] != null) url.searchParams.set(key, String(args[key]));
      return listReports(env, url);
    },
  },
  {
    name: "get_report",
    description: "報告 1 件の本文・連絡先・添付の一覧",
    inputSchema: { type: "object", properties: { id: { type: "integer" } }, required: ["id"] },
    run: (args) => (env) => getReport(env, Number(args.id)),
  },
  {
    name: "typing_sentences",
    description: "タイピングのお手本の文と、文ごとに打たれた数",
    inputSchema: { type: "object", properties: {} },
    run: () => sentenceCounts,
  },
  {
    name: "typing_flagged",
    description: "お手本とかけ離れた文を送った参加 (消すかは人がダッシュボードで決める)",
    inputSchema: { type: "object", properties: {} },
    run: () => flaggedSessions,
  },
];

const result = (id, value) => json({ jsonrpc: "2.0", id, result: value });
const failure = (id, code, message) => json({ jsonrpc: "2.0", id, error: { code, message } });

async function callTool(env, params) {
  const tool = tools.find((t) => t.name === params?.name);
  if (!tool) return null;
  const response = await tool.run(params.arguments || {})(env);
  return { content: [{ type: "text", text: await response.text() }], isError: !response.ok };
}

export async function mcp(request, env) {
  const read = await readJson(request, MAX_BYTES);
  if (read.error) return read.error;
  const message = read.body;
  if (!message || message.jsonrpc !== "2.0" || typeof message.method !== "string") return failure(null, -32600, "Invalid Request");
  // 通知 (id が無い) には答えを返さない
  if (message.id === undefined) return new Response(null, { status: 202 });
  const { id, method, params } = message;
  switch (method) {
    case "initialize":
      return result(id, {
        protocolVersion: VERSIONS.includes(params?.protocolVersion) ? params.protocolVersion : VERSIONS[0],
        capabilities: { tools: {} },
        serverInfo: { name: "meltype-dashboard", version: "1" },
      });
    case "ping":
      return result(id, {});
    case "tools/list":
      return result(id, { tools: tools.map(({ name, description, inputSchema }) => ({ name, description, inputSchema })) });
    case "tools/call": {
      const called = await callTool(env, params);
      return called ? result(id, called) : failure(id, -32602, `Unknown tool: ${params?.name}`);
    }
    default:
      return failure(id, -32601, `Method not found: ${method}`);
  }
}
