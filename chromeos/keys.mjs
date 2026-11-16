// SPDX-License-Identifier: GPL-3.0-or-later
const named = {Backspace: 8, Tab: 9, Enter: 13, NumpadEnter: 13, ShiftLeft: 16, ShiftRight: 16,
  ControlLeft: 17, ControlRight: 17, AltLeft: 18, AltRight: 18, Escape: 27,
  Space: 32, PageUp: 33, PageDown: 34, End: 35, Home: 36, ArrowLeft: 37,
  ArrowUp: 38, ArrowRight: 39, ArrowDown: 40, Delete: 46,
  Semicolon: 186, Equal: 187, Comma: 188, Minus: 189, Period: 190, Slash: 191,
  Backquote: 192, BracketLeft: 219, Backslash: 220, BracketRight: 221,
  Quote: 222, IntlBackslash: 226, IntlYen: 220, IntlRo: 226};
const modeKeys = {ZenkakuHankaku: "toggle", KanjiMode: "toggle", Hankaku: "direct", Zenkaku: "japanese",
  Eisu: "direct", Alphanumeric: "direct", Hiragana: "japanese", Katakana: "japanese",
  HiraganaKatakana: "japanese", KanaMode: "japanese"};
const modeCodes = {Lang1: "japanese", Lang2: "direct", Lang3: "japanese", Lang4: "japanese",
  Lang5: "toggle", KanaMode: "japanese"};
export function translateKey(event) {
  const code = event.code ?? "";
  const modifiers = (event.shiftKey ? 1 : 0) | (event.ctrlKey ? 2 : 0) |
    (event.altKey || event.altgrKey ? 4 : 0) | (event.metaKey ? 8 : 0);
  // JISの半角/全角はBackquoteと同じ位置だが、文字の ` とは別のキー。
  const mode = modeKeys[event.key] ?? modeCodes[code] ??
    // ChromeOS may report the JIS 半角/全角 key as key=Unidentified/Process with
    // code=Backquote. Treat only those IME names as the mode key; a real ` key
    // still keeps its character.
    (code === "Backquote" && ["Unidentified", "Process", "Compose", ""].includes(event.key ?? "") ? "toggle" : undefined) ??
    (code === "Backquote" && event.altKey && !event.altgrKey && modifiers === 4 ? "toggle" : undefined);
  if (mode) return {vk: 243, character: 0, modifiers, mode};
  let vk = named[code];
  if (/^Key[A-Z]$/.test(code)) vk = code.charCodeAt(3);
  else if (/^Digit[0-9]$/.test(code)) vk = code.charCodeAt(5);
  else if (/^F([1-9]|1[0-2])$/.test(code)) vk = 111 + Number(code.slice(1));
  else if (/^Numpad[0-9]$/.test(code)) vk = 96 + Number(code.slice(6));
  if (vk === undefined) return null;
  const scalars = [...(event.key ?? "")];
  return {vk, character: scalars.length === 1 ? scalars[0].codePointAt(0) : 0,
    modifiers};
}
