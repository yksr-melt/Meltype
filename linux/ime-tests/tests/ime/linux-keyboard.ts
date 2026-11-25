// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/** Linux固有のOS入力だけを提供する。製品結果のアサーションはspecに置く。 */
import { test as base, expect } from '@playwright/test';
import { execFile } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import type { FieldId } from '../../src/recorder';

const execute = promisify(execFile);
const script = fileURLToPath(new URL('../../scripts/linux_ime.py', import.meta.url));
type Key = 'Return' | 'space' | 'Escape' | 'BackSpace' | 'Tab' | 'Shift+Tab' | 'Shift+Return' |
  'Left' | 'Right' | 'Up' | 'Down' | 'Home' | 'End' | 'ctrl+a';
interface Target { windowId: string; context: string | null; password: boolean }
interface Keyboard {
  focus(field: FieldId): Promise<void>;
  type(text: string): Promise<void>;
  key(key: Key): Promise<void>;
}

async function control(operation: object): Promise<Target> {
  const { stdout } = await execute('/usr/bin/python3', [script, 'control', JSON.stringify(operation)], { timeout: 15_000 });
  return JSON.parse(stdout) as Target;
}

export const test = base.extend<{ ime: Keyboard }>({
  ime: async ({ page }, use, info) => {
    if (process.platform !== 'linux' || !process.env.MELTYPE_IME_SESSION)
      throw new Error('実IME試験は専用Linuxセッションのnpm run test:ime:linux:ciから実行してください');
    await page.goto('/');
    const title = 'Meltype IME E2E ' + randomUUID();
    await page.evaluate(value => { document.title = value; }, title);
    await page.bringToFront();
    await page.locator('#text').click();
    // 選択は初期化時だけ。欄移動や通常欄への復帰を再選択で修復しない。
    let target = await control({ action: 'select', title });
    const focused = async (field: FieldId) => {
      await expect.poll(() => page.locator('#' + field).evaluate(
        node => document.hasFocus() && document.activeElement === node)).toBe(true);
    };
    await focused('text');
    try {
      await use({
        async focus(field) {
          const input = page.locator('#' + field);
          if (field === 'password') await expect(input).toHaveAttribute('type', 'password');
          await input.click();
          await focused(field);
          target = await control({ action: 'focus', title, password: field === 'password' });
        },
        async type(text) { target = await control({ action: 'type', ...target, text }); },
        async key(key) { target = await control({ action: 'key', ...target, key }); },
      });
    } finally {
      if (info.status !== info.expectedStatus) {
        await info.attach('browser-events', {
          body: (await page.locator('#events').textContent({ timeout: 2_000 }))!, contentType: 'application/json',
        });
      }
    }
  },
});
export { expect };
