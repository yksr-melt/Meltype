// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/** 通常ログは1件2行。詳細な例外・子プロセス出力は別添付に保持する。 */
export interface FailureSummary {
  readonly label: string;
  readonly expected: string;
  readonly actual: string;
}
export type Comparable = string | number | boolean | null;
// 添付は外部データとして検査し、不正な要約を通常の比較結果として表示しない。
export function parseFailure(value: unknown): FailureSummary {
  if (typeof value !== 'object' || value === null ||
      !('label' in value) || typeof value.label !== 'string' ||
      !('expected' in value) || typeof value.expected !== 'string' ||
      !('actual' in value) || typeof value.actual !== 'string')
    throw new Error('失敗要約の形式が不正です');
  return { label: value.label, expected: value.expected, actual: value.actual };
}
