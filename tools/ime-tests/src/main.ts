// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 入力テスト画面のイベントを記録し、チャットの送信・改行を制御する。
 * 実IMEの変換処理は持たず、記録のON/OFFと入力操作の状態を独立させる。
 */

import { fieldIds, type FieldId } from './schemas/fields';
import { Recorder } from './recorder';
import './style.css';

function element<T extends HTMLElement>(id: string): T {
  const value = document.getElementById(id);
  if (!value) throw new Error(`入力画面に ${id} がありません`);
  return value as T;
}
const recorder = new Recorder();
// KeyboardEventのフラグだけに依存せず、欄ごとのcomposition開始・終了も追跡する。
const composing = new Set<FieldId>();
let activeChat: 'textarea' | 'contenteditable' = 'textarea';
let sendCount = 0;
function renderLog(): void {
  element('events').textContent = recorder.json();
  element('event-count').textContent = String(recorder.snapshot().length);
}
function read(field: FieldId): string {
  const target = element(field);
  return target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement ? target.value : target.innerText;
}
// ボタン送信にも同じ変換中ガードを適用し、最後に選んだチャット欄の本文を送る。
function send(): void {
  if (composing.size) return;
  const target = element(activeChat);
  const message = document.createElement('li');
  message.textContent = read(activeChat);
  element('messages').append(message);
  if (target instanceof HTMLTextAreaElement) target.value = '';
  else target.textContent = '';
  element('send-count').textContent = String(++sendCount);
}
for (const field of fieldIds) {
  const target = element(field);
  for (const type of ['focus', 'blur', 'keydown', 'keyup', 'beforeinput', 'input', 'compositionstart', 'compositionupdate', 'compositionend', 'paste']) {
    target.addEventListener(type, event => {
      if (type === 'focus') {
        element('focus').textContent = field;
        if (field === 'textarea' || field === 'contenteditable') activeChat = field;
      }
      if (type === 'blur') element('focus').textContent = 'なし';
      if (type === 'compositionstart') composing.add(field);
      // 欄を離れたあとに古い変換状態で別の欄の送信を止めない。
      if (type === 'compositionend' || type === 'blur') composing.delete(field);
      // 機密欄の値・キー・compositionデータ・クリップボードを読み取らない。
      const observed = { field, type, isComposing: composing.has(field) ||
        ((event instanceof KeyboardEvent || event instanceof InputEvent) && event.isComposing) };
      recorder.record(field === 'password' ? observed : {
        ...observed,
        key: event instanceof KeyboardEvent ? event.key : undefined,
        data: event instanceof InputEvent || event instanceof CompositionEvent ? event.data : undefined,
        value: recorder.saveText ? read(field) : undefined,
      });
      renderLog();
      if (event instanceof KeyboardEvent && type === 'keydown' && event.key === 'Enter' &&
          (field === 'textarea' || field === 'contenteditable')) {
        // 確定用Enterを送信に流さない。Shift+Enterはブラウザー標準の改行へ渡す。
        if (event.isComposing || event.keyCode === 229 || composing.has(field)) return;
        if (!event.shiftKey) { event.preventDefault(); send(); }
      }
    });
  }
}
element('chat').addEventListener('submit', event => { event.preventDefault(); send(); });
element<HTMLInputElement>('record').addEventListener('change', event => {
  recorder.enabled = (event.currentTarget as HTMLInputElement).checked;
});
element<HTMLInputElement>('save-text').addEventListener('change', event => {
  recorder.saveText = (event.currentTarget as HTMLInputElement).checked;
});
element('clear').addEventListener('click', () => { recorder.clear(); renderLog(); });
element('export').addEventListener('click', () => {
  const url = URL.createObjectURL(new Blob([recorder.json()], { type: 'application/json' }));
  const link = document.createElement('a');
  link.href = url; link.download = 'meltype-events.json'; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 0);
});
renderLog();
