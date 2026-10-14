// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * botへ渡す再現結果と、コメントから判別する結果の契約。
 * 報告内容との一致（reproduced）と期待どおりの結果（match）を区別する。
 */

export interface BotReportCase {
  readonly id: string;
  readonly operation: {
    /** botへ渡す確定・未確定・変換結果。実Mozcは起動しない。 */
    readonly committed: string;
    readonly composing: string | null;
    readonly converted: string | null;
    /** 利用者が期待する正しい結果。下のexpected.verdict（botの判定）とは別。 */
    readonly expected: string;
    /** 不具合として報告された結果。期待値に一致せず、この空でない結果に一致した場合に再現と判定する。 */
    readonly reported: string;
  };
  readonly expected: { readonly verdict: 'different' | 'match' | 'reproduced' };
}
/** localhostの偽APIだけを使用する、既存botの環境変数契約。 */
export interface BotEnvironment {
  readonly GITHUB_API_URL: string;
  readonly GITHUB_TOKEN: string;
  readonly GITHUB_REPOSITORY: string;
  readonly GITHUB_EVENT_PATH: string;
  readonly GITHUB_SERVER_URL: string;
  readonly GITHUB_RUN_ID: string;
  readonly BOT_ACTION: string;
  readonly BOT_ISSUE: string;
  readonly BOT_RESULTS: string;
  readonly BOT_KEYS: string;
  readonly BOT_LAST: string;
  readonly BOT_EXPECTED: string;
  readonly BOT_REPORTED: string;
  readonly BOT_COMMENT_ID: string;
  readonly BOT_JOB_RESULT: string;
}
