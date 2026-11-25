// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import { defineConfig } from '@playwright/test';
import base from './playwright.config';

if (process.platform !== 'linux' || !process.env.MELTYPE_IME_SESSION)
  throw new Error('実IME試験はnpm run test:ime:linux:ciから専用Linuxセッションで起動してください');

export default defineConfig({
  ...base,
  globalTimeout: 60 * 60_000,
  projects: [{
    name: 'real-ime-linux', testMatch: '**/ime/**/*.spec.ts',
    use: {
      browserName: 'chromium', headless: false, baseURL: 'http://127.0.0.1:4173',
      launchOptions: { args: ['--ozone-platform=x11', '--gtk-version=3'] },
      trace: 'retain-on-failure', screenshot: 'only-on-failure',
    },
  }],
});
