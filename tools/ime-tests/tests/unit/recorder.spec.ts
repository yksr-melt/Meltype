// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: 記録の順序と機密情報の除外を維持し、不正な保存データを拒否することを確認する。
 * 検証範囲: Recorderへ直接渡した観測データと、保存JSONの解析結果。
 * 制約: DOMを使用せず、ブラウザーによるイベントの配送は検査しない。
 */

import { test, expect } from '@playwright/test';
import { Recorder, parseRecording } from '../../src/recorder';

test('[recorder.order] OFF中は記録せず再開時も順序と連番を保持する', () => {
  const recorder = new Recorder();
  for (const type of ['compositionstart', 'beforeinput', 'input', 'compositionend']) recorder.record({ field: 'text', type });
  recorder.enabled = false; recorder.record({ field: 'text', type: 'ignored' });
  recorder.enabled = true; recorder.record({ field: 'text', type: 'blur' });
  expect(recorder.snapshot().map(item => item.type)).toEqual(['compositionstart', 'beforeinput', 'input', 'compositionend', 'blur']);
  expect(recorder.snapshot().map(item => item.sequence)).toEqual([1, 2, 3, 4, 5]);
  const copy = recorder.snapshot(); copy[0]!.type = 'changed';
  expect(recorder.snapshot()[0]!.type).toBe('compositionstart');
  recorder.clear(); expect(recorder.snapshot()).toEqual([]);
});
test('[recorder.privacy] 通常欄の既定とpasswordの機密除外を守る', () => {
  const recorder = new Recorder();
  // 呼び出し元が余分な機密項目を渡しても、保存する項目の選別で漏らさないことを確かめる。
  const raw = { field: 'text' as const, type: 'input', data: 'secret-data', key: 'secret-key', value: 'secret-value', clipboard: 'secret-clipboard' };
  recorder.record(raw); expect(recorder.json()).not.toContain('secret-');
  recorder.saveText = true; recorder.record({ ...raw, field: 'password' });
  expect(recorder.json()).not.toContain('secret-');
  expect(recorder.snapshot()[1]).toEqual({ sequence: 2, field: 'password', type: 'input', redacted: true });
  recorder.record(raw); expect(recorder.json()).toContain('secret-value'); expect(recorder.json()).not.toContain('secret-clipboard');
  expect(parseRecording(recorder.json()).events).toEqual(recorder.snapshot());
});
test('[recorder.reject-json] 型不正と機密プロパティを読み取り時も拒否する', () => {
  for (const json of ['null', '{}', '{"version":1,"events":[{"sequence":1,"type":"input","field":"unknown"}]}',
    '{"version":1,"events":[{"sequence":1,"type":"input","field":"password","redacted":true,"value":"secret"}]}'])
    expect(() => parseRecording(json)).toThrow();
});
