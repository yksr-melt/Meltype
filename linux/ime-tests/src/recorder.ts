// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * DOMに依存しないイベント記録とJSON書き出し。
 * 受け取ったオブジェクトを丸ごと保存せず、許可した項目だけを新しい記録へ移す。
 */

export const fieldIds = ['text', 'search', 'textarea', 'contenteditable', 'password'] as const;
export type FieldId = typeof fieldIds[number];

export interface ObservedEvent {
  field: FieldId;
  type: string;
  key?: string;
  data?: string | null;
  value?: string;
  isComposing?: boolean;
  trusted?: boolean;
}
export interface RecordedEvent {
  sequence: number;
  field: FieldId;
  type: string;
  isComposing?: boolean;
  trusted?: boolean;
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
    if (event.trusted !== undefined) safe.trusted = event.trusted;
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
