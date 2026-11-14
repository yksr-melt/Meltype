// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Yukishiro
//
// 欧州中央銀行 (ECB) の参照レート (eurofxref-daily.xml) から、通貨の換算に使う表 dictionaries/exchange-rates.txt を作る
// (100どる → 約1.6万円 の候補。issue #342)。Meltype は使うときにネットにつながず、リリースのときに作った表を同梱する。
// リリースの前に実行してコミットする (docs/dev/RELEASE.md)。
//   node tools/make-exchange-rates.mjs
// ECB の参照レートは、出典を書けば再利用できる (https://www.ecb.europa.eu/stats/policy_and_exchange_rates/euro_reference_exchange_rates/html/index.en.html)。
import { writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const url = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
const response = await fetch(url);
if (!response.ok) throw new Error(`ECB のレートを読めません: ${response.status}`);
const xml = await response.text();
const date = /time=['"](\d{4}-\d{2}-\d{2})['"]/.exec(xml)?.[1];
if (!date) throw new Error("ECB のレートの日付を読めません");
const rates = [...xml.matchAll(/currency=['"]([A-Z]{3})['"]\s+rate=['"]([0-9.]+)['"]/g)].map(m => [m[1], m[2]]);
if (rates.length < 10 || !rates.some(([code]) => code === "JPY")) throw new Error("ECB のレートが足りません");

const lines = [
  "# Meltype 通貨の換算に使うレート (1 ユーロあたりの各通貨の額)",
  "# 欧州中央銀行 (ECB) の参照レートから tools/make-exchange-rates.mjs で作成。手で直さず、スクリプトを流して作り直す。",
  "# 出典: European Central Bank (https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml)",
  `# 日付: ${date}`,
  "EUR\t1",
  ...rates.map(([code, rate]) => `${code}\t${rate}`),
];
const out = join(dirname(fileURLToPath(import.meta.url)), "..", "dictionaries", "exchange-rates.txt");
writeFileSync(out, lines.join("\n") + "\n");
console.log(`dictionaries/exchange-rates.txt を作りました (${date}、${rates.length + 1} 通貨)`);
