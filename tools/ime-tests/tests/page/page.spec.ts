// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/** テストページ自身の契約。製品IMEの回帰試験には数えない。 */
import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import type { Recording } from '../../src/recorder';

test.beforeEach(async ({ page }) => { await page.goto('/'); });

test('記録ON/OFFで画面内の送信が変わらない', async ({ page }) => {
  const input = page.locator('#textarea');
  await input.fill('ダミー本文'); await input.press('Enter');
  await expect(page.locator('#event-count')).not.toHaveText('0');
  await page.locator('#record').uncheck();
  const before = (await page.locator('#events').textContent())!;
  await input.fill('ダミー本文'); await input.press('Enter');
  await expect(page.locator('#messages li')).toHaveText(['ダミー本文', 'ダミー本文']);
  await expect(page.locator('#send-count')).toHaveText('2');
  await expect(page.locator('#events')).toHaveText(before);
});

test('書き出しJSONは画面の記録と一致し、passwordの内容を含まない', async ({ page }) => {
  await page.locator('#save-text').check();
  await page.locator('#text').fill('通常欄のダミー');
  await page.locator('#password').fill('dummy-private-8472');
  await page.locator('#password').dispatchEvent('compositionupdate', { data: 'dummy-private-8472' });
  await page.locator('#export').focus();
  const before = (await page.locator('#events').textContent())!;
  const pending = page.waitForEvent('download');
  await page.locator('#export').click();
  const download = await pending;
  const path = await download.path();
  expect(path).not.toBeNull();
  const json = await readFile(path!, 'utf8');
  expect(json).toBe(before);
  expect(json).not.toContain('dummy-private-8472');
  expect(json).toContain('通常欄のダミー');
  const recorded = JSON.parse(json) as Recording;
  expect(recorded.version).toBe(1);
  expect(recorded.events.map(e => e.sequence)).toEqual(recorded.events.map((_, i) => i + 1));
  const privateEvents = recorded.events.filter(e => e.field === 'password');
  expect(privateEvents.length).toBeGreaterThan(0);
  for (const event of privateEvents) {
    expect(event.redacted).toBe(true);
    expect('key' in event || 'data' in event || 'value' in event || 'clipboard' in event).toBe(false);
  }
});

for (const field of ['textarea', 'contenteditable'] as const) {
  test(`${field}: 合成イベントに対するページの送信ガード`, async ({ page }) => {
    const input = page.locator('#' + field);
    await input.fill('ダミー本文');
    await input.dispatchEvent('compositionstart');
    await input.dispatchEvent('keydown', { key: 'Enter', isComposing: false });
    await expect(page.locator('#send-count')).toHaveText('0');
    await input.dispatchEvent('compositionend');
    await input.dispatchEvent('keydown', { key: 'Enter', isComposing: true });
    await input.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 });
    await expect(page.locator('#send-count')).toHaveText('0');
    await input.dispatchEvent('compositionstart');
    const other = page.locator(field === 'textarea' ? '#contenteditable' : '#textarea');
    await other.fill('次の欄'); await other.press('Enter');
    await expect(page.locator('#messages li')).toHaveText(['次の欄']);
    await expect(page.locator('#send-count')).toHaveText('1');
  });
}
