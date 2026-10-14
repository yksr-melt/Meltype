// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * DOMケースの操作と期待値を用途別に定義する。
 * 送信可能な欄や追記可能な欄を型で限定し、passwordを本文操作へ混ぜない。
 */

import type { ChatField, FieldId } from './fields';

export interface FocusCase {
  readonly id: string;
  /** 順にフォーカスする欄。passwordの値を読む操作は含めない。 */
  readonly fields: readonly FieldId[];
}
/** 変換中の送信抑止、確定後の送信、Shift+Enterによる改行を一続きで検査する。 */
export interface ChatCommitCase {
  readonly id: string;
  readonly operation: { readonly field: ChatField; readonly text: string; readonly nextLine: string; readonly addedKey: string };
  readonly expected: { readonly sendCount: number; readonly message: string; readonly multiline: string };
}
export interface AppendCase {
  readonly id: string;
  readonly operation: {
    readonly field: Exclude<FieldId, 'password'>;
    /** OSクリップボードを使わず、貼付済み状態として設定する本文。 */
    readonly initial: string;
    readonly added: string;
  };
  readonly expected: { readonly text: string };
}
/** 変換イベントの順序と、変換中に別のチャット欄へ移ったあとの送信を検査する。 */
export interface CompositionCase {
  readonly id: string;
  readonly operation: { readonly field: ChatField; readonly text: string; readonly nextText: string };
  readonly expected: { readonly sendCount: number; readonly message: string };
}
