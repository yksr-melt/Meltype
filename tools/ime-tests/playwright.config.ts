// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 基盤単体・ChromiumのDOM試験を同じ実行にまとめる。
 * ブラウザーの版はpackage-lock.jsonに固定したPlaywrightに対応するものを使う。
 */

import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests',
  forbidOnly: true,
  // 不安定な成功をリトライで隠さず、単一ワーカーでケースを実行する。
  retries: 0,
  workers: 1,
  timeout: 30_000,
  globalTimeout: 300_000,
  outputDir: 'artifacts/results',
  // compactが追加した失敗ソースの添付を、後続のHTML/JSONにも含める順序。
  reporter: [
    ['./src/reporting/compact-reporter.ts'],
    ['html', { outputFolder: 'artifacts/report', open: 'never' }],
    ['json', { outputFile: 'artifacts/results.json' }],
  ],
  projects: [
    { name: 'unit', testMatch: 'unit/**/*.spec.ts' },
    {
      name: 'chromium', testMatch: 'e2e/**/*.spec.ts',
      use: { browserName: 'chromium', baseURL: 'http://127.0.0.1:4173', trace: 'retain-on-failure', screenshot: 'only-on-failure' },
    },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://127.0.0.1:4173',
    // 別の作業ツリーや古い画面を誤って検査しないよう、既存サーバーは再利用しない。
    reuseExistingServer: false,
    timeout: 30_000,
  },
});
