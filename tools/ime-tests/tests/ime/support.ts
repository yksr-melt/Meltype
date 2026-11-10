// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/** OSに依存しないブラウザー上の観測処理。 */
import { expect, type Locator, type Page } from '@playwright/test';
import type { FieldId, Recording } from '../../src/recorder';

export async function recording(page: Page): Promise<Recording> {
  return JSON.parse((await page.locator('#events').textContent())!) as Recording;
}
export async function compositionEvents(page: Page, field: FieldId) {
  return (await recording(page)).events.filter(event => event.field === field && event.type.startsWith('composition'));
}
export function text(input: Locator): Promise<string> {
  return input.evaluate(node => node instanceof HTMLInputElement || node instanceof HTMLTextAreaElement
    ? node.value : (node as HTMLElement).innerText);
}
/** DOMのtext node分割に依存せず、本文中の文字オフセットとして観測する。 */
export function caret(input: Locator): Promise<{ start: number; end: number }> {
  return input.evaluate(node => {
    if (node instanceof HTMLInputElement || node instanceof HTMLTextAreaElement)
      return { start: node.selectionStart!, end: node.selectionEnd! };
    const selection = window.getSelection()!;
    const range = selection.getRangeAt(0);
    const before = document.createRange();
    before.selectNodeContents(node); before.setEnd(range.startContainer, range.startOffset);
    const through = document.createRange();
    through.selectNodeContents(node); through.setEnd(range.endContainer, range.endOffset);
    return { start: before.toString().length, end: through.toString().length };
  });
}

export async function expectCompositionEnds(page: Page, field: FieldId, count: number): Promise<void> {
  await expect.poll(async () =>
    (await compositionEvents(page, field)).filter(event => event.type === 'compositionend').length,
  ).toBe(count);
}
