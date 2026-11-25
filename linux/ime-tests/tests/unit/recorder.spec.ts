// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

import { test, expect } from '@playwright/test';
import { Recorder } from '../../src/recorder';

test('記録OFFとclearが記録順序を壊さない', () => {
  const recorder = new Recorder();
  recorder.record({ field: 'text', type: 'input' });
  recorder.enabled = false;
  recorder.record({ field: 'text', type: 'ignored' });
  recorder.enabled = true;
  recorder.record({ field: 'text', type: 'blur' });
  expect(recorder.snapshot()).toEqual([
    { sequence: 1, field: 'text', type: 'input' }, { sequence: 2, field: 'text', type: 'blur' },
  ]);
  const copy = recorder.snapshot(); copy[0]!.type = 'changed';
  expect(recorder.snapshot()[0]!.type).toBe('input');
  recorder.clear();
  expect(JSON.parse(recorder.json())).toEqual({ version: 1, events: [] });
});

test('文字保存OFFとpasswordは文字情報を保持しない', () => {
  const recorder = new Recorder();
  const raw = { field: 'text' as const, type: 'input', key: 'dummy-key', data: 'dummy-data', value: 'dummy-value', clipboard: 'dummy-clipboard' };
  recorder.record(raw);
  expect(recorder.json()).not.toContain('dummy-');
  recorder.saveText = true;
  recorder.record({ ...raw, field: 'password', trusted: true });
  expect(recorder.json()).not.toContain('dummy-');
  expect(recorder.snapshot()[1]).toEqual({ sequence: 2, field: 'password', type: 'input', trusted: true, redacted: true });
  recorder.record(raw);
  expect(recorder.snapshot()[2]).toMatchObject({ key: 'dummy-key', data: 'dummy-data', value: 'dummy-value' });
  expect(recorder.json()).not.toContain('dummy-clipboard');
});
