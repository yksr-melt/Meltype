// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: 入力テスト画面が、入力操作とイベント記録を両立することを確認する。
 * 検証範囲: Chromium上でのDOM操作と、画面・書き出しJSONに現れる結果。
 * 制約: 変換イベントは合成し、機密欄にはダミー値だけを使う。
 * 実IMEの変換結果、OSの入力ソース切替、実候補の確定は保証しない。
 */

import { test } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { fieldIds } from '../../src/schemas/fields';
import type { FocusCase, ChatCommitCase, CompositionCase } from '../../src/schemas/input';
import { parseRecording } from '../../src/recorder';
import { chatCommit, compositionAndMove, withDomEvidence } from '../../src/runners/dom';
import { checkEqual, waitEqual } from '../../src/reporting/check';

{
  // Issue #474 / #511、PR #527: 通常欄と機密欄の境界。ここではDOM属性とフォーカスだけを検査する。
  const item = { id: 'D01', fields: fieldIds } as const satisfies FocusCase;
  test(`[${item.id}] 入力欄の種類とフォーカス`, async ({ page }, info) => {
    await withDomEvidence(page, info, async () => {
      for (const field of item.fields) {
        const target = page.locator('#' + field);
        if (field === 'text' || field === 'search' || field === 'password')
          await waitEqual('入力の種類', field, () => target.getAttribute('type'));
        else if (field === 'textarea') checkEqual('要素の種類', 'TEXTAREA', await target.evaluate(node => node.tagName));
        else await waitEqual('編集属性', 'true', () => target.getAttribute('contenteditable'));
        await target.focus();
        await waitEqual('フォーカス', true, () => target.evaluate(node => node === document.activeElement));
        await waitEqual('現在欄', field, () => page.locator('#focus').innerText());
      }
    });
  });
}

{
  // Issue #474 / #511、PR #527: 通常欄と機密欄の境界。DOM上の往復を検査する。
  const focus = { id: 'D02', fields: ['text', 'password', 'text'] } as const satisfies FocusCase;
  test(`[${focus.id}] 通常欄と機密欄を往復する`, async ({ page }, info) => {
    await withDomEvidence(page, info, async () => {
      for (const field of focus.fields) {
        const target = page.locator('#' + field); await target.focus();
        await waitEqual('現在欄', field, () => page.locator('#focus').innerText());
        await waitEqual('フォーカス', true, () => target.evaluate(node => node === document.activeElement));
      }
    });
  });
}

{
  // 同じ送信契約を持つ2種類の欄を比較する。変種のIDには欄名を残す。
  const commitCases = (['textarea', 'contenteditable'] as const).map(field => ({
    id: 'D03', operation: { field, text: 'あなた', nextLine: '前', addedKey: 'b' },
    expected: { sendCount: 1, message: 'あなた', multiline: '前\nb' },
  } as const satisfies ChatCommitCase));
  for (const item of commitCases) test(`[${item.id}/${item.operation.field}] 確定と送信・改行`, async ({ page }, info) => {
    await withDomEvidence(page, info, () => chatCommit(page, item));
  });
}

{
  // 記録停止が入力制御まで止めないことと、停止中に記録を増やさないことを同時に確認する。
  const recordCase = { id: 'D05', operation: { text: '同じ本文' }, expected: { sends: 2, messages: ['同じ本文', '同じ本文'] } } as const;
  test(`[${recordCase.id}] 記録ON/OFFで入力と送信が変わらない`, async ({ page }, info) => {
    await withDomEvidence(page, info, async () => {
      const target = page.locator('#textarea');
      await target.fill(recordCase.operation.text); await target.press('Enter');
      checkEqual('記録が存在する', true, Number(await page.locator('#event-count').innerText()) > 0);
      await page.locator('#record').uncheck(); const before = await page.locator('#events').innerText();
      await target.fill(recordCase.operation.text); await target.press('Enter');
      await waitEqual('送信本文', JSON.stringify(recordCase.expected.messages), async () => JSON.stringify(await page.locator('#messages li').allTextContents()));
      await waitEqual('送信回数', String(recordCase.expected.sends), () => page.locator('#send-count').innerText());
      await waitEqual('OFF中の記録', before, () => page.locator('#events').innerText());
    });
  });
}

{
  // Issue #474 / PR #527: 機密欄の内容を記録しない。実IMEのInputScope判定とは別。
  const secretCase = { id: 'D06', operation: { dummy: 'DUMMY-private-8472' } } as const;
  test(`[${secretCase.id}] passwordの内容を記録しない`, async ({ page }, info) => {
    await withDomEvidence(page, info, async () => {
      await page.locator('#save-text').check();
      const target = page.locator('#password'); await target.focus(); await target.pressSequentially(secretCase.operation.dummy);
      await target.dispatchEvent('compositionupdate', { data: secretCase.operation.dummy });
      await target.dispatchEvent('beforeinput', { data: secretCase.operation.dummy, inputType: 'insertText' });
      await target.dispatchEvent('paste');
      // テスト自身はダミー値の入力を確認するが、記録画面と書き出しJSONには内容を許可しない。
      await waitEqual('ダミー入力', secretCase.operation.dummy, () => target.inputValue());
      checkEqual('詳細表示の機密内容', false, (await page.locator('#events').innerText()).includes(secretCase.operation.dummy));
      const pending = page.waitForEvent('download'); await page.locator('#export').click();
      const download = await pending; const path = await download.path();
      checkEqual('保存ファイル', true, path !== null);
      const json = await readFile(path!, 'utf8');
      checkEqual('保存データの機密内容', false, json.includes(secretCase.operation.dummy));
      const events = parseRecording(json).events.filter(item => item.field === 'password');
      checkEqual('機密欄のイベント', true, events.length > 0);
      checkEqual('キー記録', true, events.some(item => item.type === 'keydown'));
      checkEqual('変換記録', true, events.some(item => item.type === 'compositionupdate'));
      for (const event of events) {
        checkEqual('機密欄の除外印', true, event.redacted === true);
        checkEqual('保存プロパティ', false, Object.keys(event).some(key => ['key', 'data', 'value', 'clipboard'].includes(key)));
      }
    });
  });
}

{
  const compositionCases = (['textarea', 'contenteditable'] as const).map(field => ({
    id: 'D09', operation: { field, text: 'あなた', nextText: '次の欄' }, expected: { sendCount: 1, message: '次の欄' },
  } as const satisfies CompositionCase));
  for (const item of compositionCases) test(`[${item.id}/${item.operation.field}] 変換イベントと欄移動`, async ({ page }, info) => {
    await withDomEvidence(page, info, () => compositionAndMove(page, item));
  });
}
