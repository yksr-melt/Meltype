// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 入力画面と観測データで共通に使う欄の識別子を定義する。
 * 送信・改行を扱う欄はChatFieldで限定する。識別子は画面の要素IDと保存データの検査で一致させる。
 */

export const fieldIds = ['text', 'search', 'textarea', 'contenteditable', 'password'] as const;
export type FieldId = typeof fieldIds[number];
/** 送信・改行を扱えるチャット欄。 */
export type ChatField = 'textarea' | 'contenteditable';
