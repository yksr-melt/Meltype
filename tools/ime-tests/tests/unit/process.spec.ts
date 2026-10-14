// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: 外部プロセスの異常終了とtimeoutを区別し、stdout・stderrを保持することを確認する。
 * 検証範囲: プロセス実行ヘルパーが返す終了状態・出力と、成功判定時の例外。
 * 制約: 終了や待機を制御できるNodeプロセスを使用し、製品CLIの機能は検査しない。
 */

import { test, expect } from '@playwright/test';
import { executeProcess, requireSuccess } from '../../src/process';

test('[process.reject-exit] 出力があっても異常終了を拒否し両出力を残す', async () => {
  const result = await executeProcess(process.execPath, ['-e', `console.log('partial-output'); console.error('failure'); process.exit(23)`]);
  expect(result.exitCode).toBe(23); expect(result.stdout).toContain('partial-output'); expect(result.stderr).toContain('failure');
  expect(() => requireSuccess(result)).toThrow('23');
});
test('[process.timeout] timeoutと異常終了を区別して出力を残す', async () => {
  // 自然終了しないプロセスを使い、期限超過による終了と部分出力の保持を確認する。
  const result = await executeProcess(process.execPath, ['-e', "console.log('started'); setInterval(() => {}, 1000)"], 500);
  expect(result.timedOut).toBe(true); expect(result.stdout).toContain('started');
  expect(() => requireSuccess(result)).toThrow('timeout');
});
