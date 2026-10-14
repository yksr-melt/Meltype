// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: 証拠の添付保存と、テスト本体・保存処理のエラーの優先順位を検査する。
 * 検証範囲: 保存ヘルパーが渡す添付名、保存失敗の注釈、呼び出し元へ伝わる例外。
 * 制約: 添付先はテスト用の関数で置き換え、実ファイルへの保存やブラウザー操作は検査しない。
 */

import { test, expect } from '@playwright/test';
import { CaseFailure } from '../../src/reporting/check';
import { withEvidence, jsonEvidence } from '../../src/reporting/evidence';

test('[evidence.failure] 失敗しても観測と要約を保存する', async () => {
  const names: string[] = [];
  const info = { annotations: [], attach: async (name: string) => { names.push(name); } };
  await expect(withEvidence(info, async () => { throw new CaseFailure({ label: '値', expected: '1', actual: '2' }); },
    () => [jsonEvidence('観測', { value: 2 })])).rejects.toThrow('値');
  expect(names).toEqual(['観測', 'failure-summary']);
});
test('[evidence.original-error] 保存失敗で元のエラーを上書きしない', async () => {
  const annotations: { type: string; description?: string }[] = [];
  const info = { annotations, attach: async () => { throw new Error('保存失敗'); } };
  await expect(withEvidence(info, async () => { throw new Error('元の失敗'); }, () => [jsonEvidence('観測', null)])).rejects.toThrow('元の失敗');
  expect(annotations[0]?.description).toBe('保存失敗');
  // 本体が成功した場合は保存失敗を握りつぶさず、実行全体を失敗させる。
  await expect(withEvidence(info, async () => {}, () => [jsonEvidence('観測', null)])).rejects.toThrow('保存失敗');
});
