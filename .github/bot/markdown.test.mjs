// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cell, codeCell } from './markdown.mjs';

test('表のセルはバックスラッシュ・改行・リンク・メンションを無害化する', () => {
  assert.equal(cell('a\\|b\r\nc\rd\ne'), 'a\\\\\\|b c d e');
  assert.equal(cell('[x](https://example.com) <img> @all'), '\\[x\\]\\(https\\:\\/\\/example\\.com\\) \\<img\\> @\u200ball');
});

test('コードのセルは元の記号を文字参照で表示し、表やコードを途中で閉じない', () => {
  assert.equal(codeCell('C:\\tmp\\|`</code> & @all\r\n次'), '<code>C&#58;&#92;tmp&#92;&#124;&#96;&#60;&#47;code&#62; &#38; &#64;all 次</code>');
  assert.equal(codeCell(null), '<code></code>');
});
