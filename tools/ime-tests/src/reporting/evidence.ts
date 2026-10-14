// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * テスト本体の成否と添付保存の成否を区別する。
 * 期待値不一致だけを定型要約に変換し、それ以外の例外は元の種類のまま伝える。
 */

import type { TestInfo } from '@playwright/test';
import { CaseFailure } from './check';

export interface Evidence {
  readonly name: string;
  readonly body: string;
  readonly contentType: string;
}
export function jsonEvidence(name: string, value: unknown): Evidence {
  return { name, body: JSON.stringify(value, null, 2), contentType: 'application/json' };
}
/** 成否にかかわらず観測を保存する。保存失敗で元の失敗を上書きしない。 */
export async function withEvidence<T>(
  info: Pick<TestInfo, 'attach' | 'annotations'>,
  run: () => Promise<T>,
  evidence: () => readonly Evidence[],
): Promise<T> {
  let failed = false;
  let original: unknown;
  try { return await run(); }
  catch (error) { failed = true; original = error; throw error; }
  finally {
    // evidenceは保存済みの観測から添付を組み立てるだけにし、再観測や失敗し得るI/Oを入れない。
    const items = [...evidence()];
    if (original instanceof CaseFailure) items.push(jsonEvidence('failure-summary', original.summary));
    let saveError: unknown;
    for (const item of items) {
      try { await info.attach(item.name, { body: item.body, contentType: item.contentType }); }
      catch (error) {
        saveError = error;
        info.annotations.push({ type: 'evidence-error', description: error instanceof Error ? error.message : String(error) });
      }
    }
    // 本体が成功していても証拠を保存できなければ失敗。本体の失敗があればそちらを優先する。
    if (!failed && saveError !== undefined) throw saveError;
  }
}
