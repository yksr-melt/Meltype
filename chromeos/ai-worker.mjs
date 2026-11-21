// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 appipinopi
import {ShuntiClient, ShuntiPackages} from "./shunti.mjs";
let client = new ShuntiClient(), variant = "shunti-v7-s8p";
let preparation;
self.onmessage = async ({data}) => {
  try {
    let candidates;
    if (data.action === "prepare") {
      const selected = data.variant ?? "shunti-v7-s8p";
      if (selected !== variant) { client.dispose(); client = new ShuntiClient(new ShuntiPackages({variant:selected})); variant = selected; preparation = null; }
      await (preparation ??= client.prepare().catch(error => { preparation = null; throw error; }));
    }
    else {
      if (typeof data.reading !== "string" || data.reading.length > 160 || typeof data.context !== "string" || data.context.length > 8000) throw new Error("AI要求が不正です");
      candidates = client.convert(data.reading, data.context);
    }
    self.postMessage({id: data.id, ready: client.ready, candidates});
  } catch { self.postMessage({id: data.id, error: "shuntiを利用できません。設定で追加データを確認してください。"}); }
};
