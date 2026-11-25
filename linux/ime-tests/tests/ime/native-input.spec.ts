// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/** Issue全体の再現ではなく、属性・操作ごとの条件を実IMEとブラウザーで部分検査する。 */
import { test, expect } from './linux-keyboard';
import { caret, compositionEvents, expectCompositionEnds, recording, text } from './support';

// https://github.com/yksr-melt/Meltype/issues/255 — 確定後の入力継続という部分条件。
test('text: 確定後も次の入力を受け付ける', async ({ page, ime }) => {
  const input = page.locator('#text');
  await expect(input).toHaveAttribute('type', 'text');
  await ime.type('aiueo');
  await expect(input).toHaveValue('あいうえお');
  expect((await compositionEvents(page, 'text')).filter(e => e.type === 'compositionend')).toHaveLength(0);
  await ime.key('Return');
  await expect(input).toHaveValue('あいうえお');
  await expectCompositionEnds(page, 'text', 1);
  await ime.type('aiueo');
  await ime.key('Return');
  await expect(input).toHaveValue('あいうえおあいうえお');
  await expect(input).toBeFocused();
  await expectCompositionEnds(page, 'text', 2);
  const events = await compositionEvents(page, 'text');
  expect(events.filter(e => e.type === 'compositionstart')).toHaveLength(2);
  expect(events.filter(e => e.type === 'compositionupdate').length).toBeGreaterThanOrEqual(2);
  expect(events.filter(e => e.type === 'compositionend')).toHaveLength(2);
  // Chromiumの終了イベントはキュー配送でtrusted=falseになり得る。
  expect(events.filter(e => e.type !== 'compositionend').every(e => e.trusted === true)).toBe(true);
});

test('search: Spaceで未確定の変換表示を更新して確定する', async ({ page, ime }) => {
  const input = page.locator('#search');
  await expect(input).toHaveAttribute('type', 'search');
  await ime.focus('search');
  await ime.type('kyou');
  await expect(input).toHaveValue('きょう');
  const before = await compositionEvents(page, 'search');
  expect(before.some(e => e.type === 'compositionstart' && e.trusted === true)).toBe(true);
  expect(before.filter(e => e.type === 'compositionend')).toHaveLength(0);
  await ime.key('space');
  await expect(input).toHaveValue('今日');
  await expect.poll(async () => (await compositionEvents(page, 'search')).length).toBeGreaterThan(before.length);
  const converted = await compositionEvents(page, 'search');
  expect(converted.at(-1)).toMatchObject({ type: 'compositionupdate', trusted: true });
  expect(converted.filter(e => e.type === 'compositionend')).toHaveLength(0);
  await ime.key('Return');
  await expect(input).toHaveValue('今日');
  await expectCompositionEnds(page, 'search', 1);
});

for (const field of ['textarea', 'contenteditable'] as const) {
  test(`${field}: 実IMEの確定・改行・送信を区別する`, async ({ page, ime }) => {
    const input = page.locator('#' + field);
    if (field === 'contenteditable') await expect(input).toHaveAttribute('contenteditable', 'true');
    await ime.focus(field);
    await ime.type('aiueo');
    await ime.key('Return');
    await expect.poll(() => text(input)).toBe('あいうえお');
    await expect(page.locator('#send-count')).toHaveText('0');
    await ime.key('Shift+Return');
    await expect(page.locator('#send-count')).toHaveText('0');
    await ime.type('aiueo');
    await ime.key('Return');
    await expect.poll(() => text(input)).toBe('あいうえお\nあいうえお');
    await expect(page.locator('#send-count')).toHaveText('0');
    await expectCompositionEnds(page, field, 2);
    await ime.key('Return');
    await expect(page.locator('#messages li')).toHaveText(['あいうえお\nあいうえお']);
    await expect(page.locator('#send-count')).toHaveText('1');
    await expect.poll(() => text(input)).toBe('');
  });
}

test('未確定のまま欄を移動しても、両欄の本文を保つ', async ({ page, ime }) => {
  await ime.type('aiueo');
  await expect(page.locator('#text')).toHaveValue('あいうえお');
  expect((await compositionEvents(page, 'text')).filter(e => e.type === 'compositionend')).toHaveLength(0);
  await ime.focus('search');
  await expect(page.locator('#text')).toHaveValue('あいうえお');
  await expect(page.locator('#search')).toHaveValue('');
  await expectCompositionEnds(page, 'text', 1);
  await ime.type('aiueo');
  await ime.key('Return');
  await expect(page.locator('#text')).toHaveValue('あいうえお');
  await expect(page.locator('#search')).toHaveValue('あいうえお');
  await expect(page.locator('#search')).toBeFocused();
  await expectCompositionEnds(page, 'search', 1);
});

// https://github.com/yksr-melt/Meltype/issues/474 — 属性と通常欄への復帰。内部の学習制御は対象外。
test('password: ASCII入力を保ち、通常欄へ戻るとIME入力を続けられる', async ({ page, ime }) => {
  await ime.type('aiueo');
  await ime.key('Return');
  await expect(page.locator('#text')).toHaveValue('あいうえお');
  const password = page.locator('#password');
  await expect(password).toHaveAttribute('type', 'password');
  await ime.focus('password');
  await ime.type('dummy8472');
  await expect(password).toHaveValue('dummy8472');
  await ime.focus('text');
  await ime.type('aiueo');
  await ime.key('Return');
  await expect(page.locator('#text')).toHaveValue('あいうえおあいうえお');
  await expect(password).toHaveValue('dummy8472');
  await expectCompositionEnds(page, 'text', 2);
  const events = (await recording(page)).events.filter(e => e.field === 'password');
  expect(events.some(e => e.type === 'input' && e.trusted === true)).toBe(true);
  for (const event of events) {
    expect(event.redacted).toBe(true);
    expect('key' in event || 'data' in event || 'value' in event || 'clipboard' in event).toBe(false);
  }
});

// https://github.com/yksr-melt/Meltype/issues/516 — 既存本文保持の部分条件。OS貼付そのものは検査しない。
test('textarea: 既存本文の途中へ入力して前後とキャレットを保つ', async ({ page, ime }) => {
  const input = page.locator('#textarea');
  await input.fill('前後');
  await ime.focus('textarea');
  await input.evaluate(node => (node as HTMLTextAreaElement).setSelectionRange(1, 1));
  await expect(input).toHaveValue('前後');
  expect(await caret(input)).toEqual({ start: 1, end: 1 });
  await ime.type('aiueo');
  await ime.key('Return');
  await expect(input).toHaveValue('前あいうえお後');
  await expect.poll(() => caret(input)).toEqual({ start: 6, end: 6 });
  await expect(page.locator('#send-count')).toHaveText('0');
  await expectCompositionEnds(page, 'textarea', 1);
});

test('contenteditable: 複数行本文の途中へ入力して前後とキャレットを保つ', async ({ page, ime }) => {
  const input = page.locator('#contenteditable');
  // 初期条件だけをDOMで用意する。検査対象の追記はOSキーで行う。
  await input.evaluate(node => { node.textContent = '先頭\n前後'; });
  await ime.focus('contenteditable');
  await input.evaluate(node => {
    const range = document.createRange();
    range.setStart(node.firstChild!, 4); range.collapse(true);
    const selection = window.getSelection()!;
    selection.removeAllRanges(); selection.addRange(range);
  });
  await expect.poll(() => text(input)).toBe('先頭\n前後');
  expect(await caret(input)).toEqual({ start: 4, end: 4 });
  await ime.type('aiueo');
  await ime.key('Return');
  await expect.poll(() => text(input)).toBe('先頭\n前あいうえお後');
  await expect.poll(() => caret(input)).toEqual({ start: 9, end: 9 });
  await expect(page.locator('#send-count')).toHaveText('0');
  await expectCompositionEnds(page, 'contenteditable', 1);
});
