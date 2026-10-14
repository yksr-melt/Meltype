// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: 入力に関する不具合報告から、DOM上で観測できる回帰条件を検査する。
 * 検証範囲: 入力操作に伴う本文の保持と、フォーカス・選択位置の状態。
 * 制約: 本文と変換イベントはテストで与える。OSの貼付処理や実IMEの再現は保証しない。
 */

import { test } from '@playwright/test';
import type { AppendCase } from '../../src/schemas/input';
import { append, composition, recording, withDomEvidence } from '../../src/runners/dom';
import { checkEqual, waitEqual } from '../../src/reporting/check';

{
  // Issue #516 / PR #526: 貼付済み本文が失われた報告。OSの貼付や再確定指示は対象外。
  const appendCases = (['textarea', 'contenteditable'] as const).map(field => ({
    id: 'D04', operation: { field, initial: '貼付済み 本文', added: 'あなた' }, expected: { text: '貼付済み 本文あなた' },
  } as const satisfies AppendCase));
  for (const item of appendCases) test(`[${item.id}/${item.operation.field}] 貼付済み本文への追記`, async ({ page }, info) => {
    await withDomEvidence(page, info, () => append(page, item));
  });
}

{
  // Issue #255 / #242: 確定後の再入力が止まった報告。合成DOMイベントと値保持を検査する。
  const variants = (['text', 'search'] as const).map(field => ({
    id: 'D07', operation: { field, initial: 'あいうえお', added: 'あいうえお' }, expected: { text: 'あいうえおあいうえお' },
  } as const satisfies AppendCase));
  for (const item of variants) test(`[${item.id}/${item.operation.field}] 再フォーカスして追加入力`, async ({ page }, info) => {
    await withDomEvidence(page, info, async () => {
      const target = page.locator('#' + item.operation.field);
      await target.fill(item.operation.initial);
      await composition(target, 'compositionstart'); await composition(target, 'compositionupdate'); await composition(target, 'compositionend');
      // DOM上の確定後状態を作り、別欄への移動を挟んでも元の本文へ追記できるか確認する。
      await target.press('Enter'); await page.locator('#password').focus(); await target.focus(); await target.press('End');
      await page.keyboard.insertText(item.operation.added);
      await waitEqual('本文', item.expected.text, () => target.inputValue());
      await waitEqual('現在欄', item.operation.field, () => page.locator('#focus').innerText());
      const events = (await recording(page)).events;
      checkEqual('フォーカス順', JSON.stringify([item.operation.field, 'password', item.operation.field]),
        JSON.stringify(events.filter(item => item.type === 'focus').map(item => item.field)));
      checkEqual('末尾イベント', JSON.stringify({ field: item.operation.field, type: 'input' }),
        JSON.stringify({ field: events.at(-1)!.field, type: events.at(-1)!.type }));
    });
  });
}

{
  // Issue #516 / PR #526: 改行を含む貼付済み本文とキャレットの条件。未マージのOS修正は検査しない。
  const variants = (['textarea', 'contenteditable'] as const).map(field => ({
    id: 'D08', operation: { field, initial: '貼付済み\n本文', added: 'って' }, expected: { text: '貼付済み\n本文って' },
  } as const satisfies AppendCase));
  for (const item of variants) test(`[${item.id}/${item.operation.field}] 複数行本文と末尾キャレット`, async ({ page }, info) => {
    await withDomEvidence(page, info, () => append(page, item, true));
  });
}
