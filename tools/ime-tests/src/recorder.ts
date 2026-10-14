// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * DOMに依存しないイベント記録と保存JSONの読取り。
 * 受け取ったオブジェクトを丸ごと保存せず、許可した項目だけを新しい記録へ移す。
 */

import type { FieldId } from './schemas/fields';

export interface ObservedEvent {
  field: FieldId;
  type: string;
  key?: string;
  data?: string | null;
  value?: string;
  isComposing?: boolean;
}
export interface RecordedEvent {
  sequence: number;
  field: FieldId;
  type: string;
  isComposing?: boolean;
  redacted?: true;
  key?: string;
  data?: string | null;
  value?: string;
}
export class Recorder {
  enabled = true;
  // 変更後に届くイベントだけに適用する。OFFにしても既存の記録は消さない。
  saveText = false;
  private events: RecordedEvent[] = [];
  record(event: ObservedEvent): void {
    if (!this.enabled) return;
    const safe: RecordedEvent = { sequence: this.events.length + 1, field: event.field, type: event.type };
    if (event.isComposing !== undefined) safe.isComposing = event.isComposing;
    // 生イベントは保持しない。passwordは文字保存がONでも常に除外する。
    if (event.field === 'password') safe.redacted = true;
    else if (this.saveText) {
      if (event.key !== undefined) safe.key = event.key;
      if (event.data !== undefined) safe.data = event.data;
      if (event.value !== undefined) safe.value = event.value;
    }
    this.events.push(safe);
  }
  // 呼び出し元が結果を書き換えても、内部の記録と連番を変えられないよう複製する。
  snapshot(): RecordedEvent[] { return this.events.map(event => ({ ...event })); }
  clear(): void { this.events = []; }
  json(): string { return JSON.stringify({ version: 1, events: this.snapshot() }, null, 2); }
}

export interface Recording {
  readonly version: 1;
  readonly events: readonly RecordedEvent[];
}
function isField(value: unknown): value is FieldId {
  return value === 'text' || value === 'search' || value === 'textarea' || value === 'contenteditable' || value === 'password';
}
// 外部JSONを型アサーションで信用しない。passwordに内容があれば黙って削らず拒否する。
function recordedEvent(value: unknown): RecordedEvent {
  if (typeof value !== 'object' || value === null ||
      !('sequence' in value) || typeof value.sequence !== 'number' || !Number.isInteger(value.sequence) || value.sequence < 1 ||
      !('field' in value) || !isField(value.field) || !('type' in value) || typeof value.type !== 'string')
    throw new Error('記録イベントの形式が不正です');
  const event: RecordedEvent = { sequence: value.sequence, field: value.field, type: value.type };
  if ('isComposing' in value) {
    if (typeof value.isComposing !== 'boolean') throw new Error('変換状態の形式が不正です');
    event.isComposing = value.isComposing;
  }
  if (value.field === 'password') {
    if ('key' in value || 'data' in value || 'value' in value || 'clipboard' in value ||
        !('redacted' in value) || value.redacted !== true) throw new Error('機密欄の内容が記録されています');
    event.redacted = true;
    return event;
  }
  if ('key' in value) { if (typeof value.key !== 'string') throw new Error('キーの形式が不正です'); event.key = value.key; }
  if ('data' in value) { if (value.data !== null && typeof value.data !== 'string') throw new Error('入力の形式が不正です'); event.data = value.data; }
  if ('value' in value) { if (typeof value.value !== 'string') throw new Error('文字列の形式が不正です'); event.value = value.value; }
  return event;
}
/** 形式と各イベントを検査する。連番やイベント順序の正しさはケース側で別途比較する。 */
export function parseRecording(json: string): Recording {
  const value: unknown = JSON.parse(json);
  if (typeof value !== 'object' || value === null || !('version' in value) || value.version !== 1 ||
      !('events' in value) || !Array.isArray(value.events)) throw new Error('記録の形式が不正です');
  return { version: 1, events: value.events.map((event: unknown) => recordedEvent(event)) };
}
