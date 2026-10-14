// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 入力画面への操作を実行し、本文・送信結果・イベント記録を比較する。
 * ケースを登録せず、呼び出し元から受け取った操作と期待値に基づいて検査する。
 * 合成compositionイベントは画面の制御を検査するためのもので、OSのIMEを起動しない。
 */

import type { Page, Locator, TestInfo } from '@playwright/test';
import { parseRecording } from '../recorder';
import { jsonEvidence, withEvidence } from '../reporting/evidence';
import { checkEqual, waitEqual } from '../reporting/check';
import type { ChatCommitCase, AppendCase, CompositionCase } from '../schemas/input';

/** イベントだけを配送する。本文は変更しないため、必要な文字列は呼び出し側で設定する。 */
export async function composition(target: Locator, type: 'compositionstart' | 'compositionupdate' | 'compositionend'): Promise<void> {
  await target.evaluate((node, type) => node.dispatchEvent(new CompositionEvent(type, { bubbles: true, data: 'あなた' })), type);
}
export function inputText(target: Locator): Promise<string> {
  return target.evaluate(node => node instanceof HTMLInputElement || node instanceof HTMLTextAreaElement ? node.value : (node as HTMLElement).innerText);
}
export async function recording(page: Page) {
  return parseRecording((await page.locator('#events').textContent())!);
}
export async function withDomEvidence(page: Page, info: TestInfo, run: () => Promise<void>): Promise<void> {
  let events: Awaited<ReturnType<typeof recording>> | null = null;
  await withEvidence(info, async () => {
    let failed = false;
    try { await page.goto('/'); await run(); }
    catch (error) { failed = true; throw error; }
    finally {
      // これはイベント添付用の読取り。比較失敗の観測値を取り直して置き換えるものではない。
      try { events = await recording(page); }
      catch (error) {
        if (!failed) throw error;
        info.annotations.push({ type: 'evidence-error', description: String(error) });
      }
    }
  }, () => [jsonEvidence('入力イベント', events)]);
}
export async function chatCommit(page: Page, item: ChatCommitCase): Promise<void> {
  const target = page.locator('#' + item.operation.field);
  await target.fill(item.operation.text);
  await composition(target, 'compositionstart');
  // フラグがfalseでも、compositionの継続中は送信を抑止する必要がある。
  await target.dispatchEvent('keydown', { key: 'Enter', isComposing: false });
  await waitEqual('変換中の送信回数', '0', () => page.locator('#send-count').innerText());
  await composition(target, 'compositionend');
  const events = (await recording(page)).events;
  checkEqual('変換イベント順序', JSON.stringify(['compositionstart', 'compositionend']),
    JSON.stringify(events.filter(item => item.type.startsWith('composition')).map(item => item.type)));
  checkEqual('記録の連番', JSON.stringify(events.map((_, i) => i + 1)), JSON.stringify(events.map(item => item.sequence)));
  await target.dispatchEvent('keydown', { key: 'Enter', isComposing: true });
  await target.dispatchEvent('keydown', { key: 'Enter', keyCode: 229 });
  await waitEqual('IMEキーの送信回数', '0', () => page.locator('#send-count').innerText());
  await target.fill(item.operation.text); await target.press('Enter');
  await waitEqual('送信回数', String(item.expected.sendCount), () => page.locator('#send-count').innerText());
  await waitEqual('送信本文', item.expected.message, () => page.locator('#messages li').innerText());
  await target.fill(item.operation.nextLine); await target.press('End'); await target.press('Shift+Enter'); await target.press(item.operation.addedKey);
  await waitEqual('Shift+Enter後の本文', item.expected.multiline, () => inputText(target));
  await waitEqual('改行後の送信回数', String(item.expected.sendCount), () => page.locator('#send-count').innerText());
}
export async function append(page: Page, item: AppendCase, multiline = false): Promise<void> {
  const target = page.locator('#' + item.operation.field);
  await target.fill(item.operation.initial);
  // 複数行のEndは行末を指すため、本文末尾の選択位置をDOM APIで明示する。
  if (multiline) await target.evaluate(node => {
    if (node instanceof HTMLTextAreaElement) node.setSelectionRange(node.value.length, node.value.length);
    else {
      const range = document.createRange(); range.selectNodeContents(node); range.collapse(false);
      const selection = window.getSelection()!; selection.removeAllRanges(); selection.addRange(range);
    }
  });
  else await target.press('End');
  await page.keyboard.insertText(item.operation.added);
  await waitEqual('本文', item.expected.text, () => inputText(target));
  if (multiline) {
    const atEnd = await target.evaluate(node => {
      if (node instanceof HTMLTextAreaElement) return node.selectionStart === node.value.length && node.selectionEnd === node.value.length;
      const selection = window.getSelection()!;
      let last: Node = node; while (last.lastChild) last = last.lastChild;
      return selection.isCollapsed && selection.anchorNode === last && selection.anchorOffset === last.textContent!.length;
    });
    checkEqual('末尾キャレット', true, atEnd);
  }
}
export async function compositionAndMove(page: Page, item: CompositionCase): Promise<void> {
  const target = page.locator('#' + item.operation.field);
  await target.fill(item.operation.text);
  await composition(target, 'compositionstart'); await composition(target, 'compositionupdate'); await composition(target, 'compositionend');
  const events = (await recording(page)).events.filter(item => item.type.startsWith('composition'));
  checkEqual('変換イベント', JSON.stringify([
    { type: 'compositionstart', isComposing: true }, { type: 'compositionupdate', isComposing: true }, { type: 'compositionend', isComposing: false },
  ]), JSON.stringify(events.map(item => ({ type: item.type, isComposing: item.isComposing }))));
  // compositionendを送らず欄を移り、blurで古い変換状態が解消されることを検査する。
  await composition(target, 'compositionstart');
  const otherField = item.operation.field === 'textarea' ? 'contenteditable' : 'textarea';
  await page.locator('#' + otherField).fill(item.operation.nextText); await page.locator('#' + otherField).press('Enter');
  await waitEqual('送信回数', String(item.expected.sendCount), () => page.locator('#send-count').innerText());
  await waitEqual('送信本文', item.expected.message, () => page.locator('#messages li').innerText());
  await waitEqual('現在欄', otherField, () => page.locator('#focus').innerText());
}
