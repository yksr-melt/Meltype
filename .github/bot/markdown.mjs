// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

const oneLine = text => String(text ?? '').replace(/\r\n|[\r\n]/g, ' ');

// 表の区切り・リンク・HTML・メンションとして読まれないようにする。\ 自体もエスケープする。
export const cell = text => oneLine(text).replace(/[!-/:-?[-`{-~]/g, '\\$&').replace(/@/g, '@\u200b');

// コードスパン内では \ のエスケープが効かないため、表のコードは HTML の文字参照で表す。
// ASCII の記号をすべて文字参照にし、元の \ や | を表示したまま Markdown の構文を作らせない。
export const codeCell = text => '<code>' + oneLine(text).replace(/[!-/:-@[-`{-~]/g, c => `&#${c.codePointAt(0)};`) + '</code>';
