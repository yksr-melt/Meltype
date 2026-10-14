// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 HarukiKubota

/**
 * 目的: ケース操作の型が機密欄や不要な参照メタデータを受け付けないことを型検査で確認する。
 * 範囲: tscでの静的検査のみ。実行時テストとしては登録しない。
 */

import type { ChatCommitCase } from '../../src/schemas/input';

// 下の型エラー指定は直後の行の型エラーを必須にする。型が緩んで許可されるとtscが失敗する。
// ディレクティブと検査対象の宣言の間に、別の宣言を挟まない。
// @ts-expect-error チャット操作で機密欄を指定できない
const invalidField: ChatCommitCase['operation']['field'] = 'password';
// @ts-expect-error 参照番号の別登録はスキーマに持たない
const redundant = { id: 'X', operation: { field: 'textarea', text: 'あ', nextLine: '前', addedKey: 'b' }, expected: { sendCount: 1, message: 'あ', multiline: '前\nb' }, references: { issues: [1] } } as const satisfies ChatCommitCase;
void [invalidField, redundant];
