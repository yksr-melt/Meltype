// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 外部CLIの終了状態と出力をまとめて返す。異常終了も証拠として返し、
 * 成功が必要な呼び出し元は保存対象を確保してからrequireSuccessで判定する。
 */

import type { BotEnvironment } from './schemas/bot-report';
import { execFile } from 'node:child_process';

export interface ProcessEvidence {
  readonly command: string;
  readonly args: readonly string[];
  readonly stdout: string;
  readonly stderr: string;
  /** 起動失敗やシグナル終了など、数値の終了コードがない場合はnull。 */
  readonly exitCode: number | null;
  readonly signal: string | null;
  readonly timedOut: boolean;
  readonly error: string | null;
}
export async function executeProcess(command: string, args: readonly string[], timeoutMs = 20_000, environment?: BotEnvironment): Promise<ProcessEvidence> {
  return new Promise(resolve => {
    let timedOut = false;
    // shellを介さず引数を渡す。出力は最大1 MiBとし、超過もerrorとして呼び出し元へ返す。
    const child = execFile(command, [...args], { maxBuffer: 1024 * 1024, encoding: 'utf8',
      env: { ...process.env, ...environment } }, (error, stdout, stderr) => {
      clearTimeout(timer);
      resolve({ command, args, stdout, stderr, exitCode: error ? (typeof error.code === 'number' ? error.code : null) : 0,
        signal: error?.signal ?? null, timedOut, error: error?.message ?? null });
    });
    // timeoutでは直接の子へSIGTERMを送る。強制終了や子孫プロセスの停止は扱わない。
    const timer = setTimeout(() => { timedOut = true; child.kill('SIGTERM'); }, timeoutMs);
  });
}
export class ProcessFailure extends Error {
  constructor(readonly evidence: ProcessEvidence) {
    super(evidence.timedOut ? 'CLI timeout' : `CLI異常終了: ${evidence.exitCode ?? evidence.signal ?? evidence.error}`);
  }
}
export function requireSuccess(result: ProcessEvidence): void {
  if (result.error !== null || result.exitCode !== 0 || result.timedOut) throw new ProcessFailure(result);
}
