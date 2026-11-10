// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

// 大きな移行が必要な .NET の提案は IDE に残し、Code scanning には警告・エラーを送る。
// 既存の公開名の指摘は、ソースに理由を書いて抑制したものだけを外す。
// 対象のルールでも重要度を上げたものは残す。セキュリティの指摘や未知のルールは取り除かない。
import fs from 'node:fs';
import { fileURLToPath } from 'node:url';

const Migrations = new Set(['SYSLIB1045', 'SYSLIB1054', 'SYSLIB1096']);
const Naming = new Set(['CA1711', 'CA1720']);

export function filter(sarif) {
  const removed = { migrations: 0, naming: 0 };
  for (const run of sarif.runs ?? []) {
    run.results = (run.results ?? []).filter(result => {
      const rule = run.tool?.driver?.rules?.find(r => r.id === result.ruleId);
      const level = result.level ?? rule?.defaultConfiguration?.level ?? 'warning';
      if (Migrations.has(result.ruleId) && level === 'note' && !result.suppressions?.length) {
        removed.migrations++;
        return false;
      }
      if (Naming.has(result.ruleId) && level === 'warning' && result.suppressions?.some(s =>
        s.kind === 'inSource' && s.status !== 'rejected' && s.justification?.trim())) {
        removed.naming++;
        return false;
      }
      return true;
    });
  }
  return removed;
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const path = process.argv[2];
  const sarif = JSON.parse(fs.readFileSync(path, 'utf8'));
  const removed = filter(sarif);
  fs.writeFileSync(path, JSON.stringify(sarif));
  console.log(`移行の提案 ${removed.migrations} 件は IDE に残し、理由付きで抑制した命名の指摘 ${removed.naming} 件は除きました (${path})`);
}
