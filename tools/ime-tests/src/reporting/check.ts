// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 比較失敗を、通常ログ用の期待値・観測値と詳細用の例外に分けて保持する。
 * 比較はプリミティブ値の厳密一致。配列などの比較方法は呼び出し元で明示する。
 */

import { expect } from '@playwright/test';
import type { Comparable, FailureSummary } from '../schemas/failure';

export class CaseFailure extends Error {
  constructor(readonly summary: FailureSummary, cause?: unknown) {
    super(`${summary.label}: 期待 ${summary.expected} / 実際 ${summary.actual}`, { cause });
  }
}
function shown(value: Comparable): string { return JSON.stringify(value); }
export function checkEqual(label: string, expected: Comparable, actual: Comparable): void {
  if (expected !== actual) throw new CaseFailure({ label, expected: shown(expected), actual: shown(actual) });
}
/** 自動待機を維持し、失敗時に最後の読取り値を残す。判定後の再読取りはしない。 */
export async function waitEqual(label: string, expected: Comparable, read: () => Promise<Comparable>): Promise<void> {
  let actual: Comparable = null;
  let readingFailed = false;
  let readingError: unknown;
  try {
    await expect.poll(async () => {
      try { actual = await read(); readingFailed = false; return actual; }
      catch (error) { readingFailed = true; readingError = error; throw error; }
    }).toBe(expected);
  } catch (error) {
    // 読取り自体の失敗を「期待値と違う値が取れた」と表示しない。
    if (readingFailed) throw readingError;
    throw new CaseFailure({ label, expected: shown(expected), actual: shown(actual) }, error);
  }
}
