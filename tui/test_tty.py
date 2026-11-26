#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
"""meltype-tty の、端末のバイト列をキーに分ける処理と重ねる表示を、共有ライブラリなしで確かめる。"""

import importlib.machinery
import importlib.util
import unittest
from pathlib import Path

source = Path(__file__).resolve().parent / "meltype-tty"
loader = importlib.machinery.SourceFileLoader("meltype_tty", str(source))
tty_front = importlib.util.module_from_spec(importlib.util.spec_from_loader("meltype_tty", loader))
loader.exec_module(tty_front)


def keys_of(data):
    keys, rest = tty_front.split_keys(data)
    assert rest == "", rest
    return keys


class SplitKeysTests(unittest.TestCase):
    def test_characters_and_controls(self):
        self.assertEqual(keys_of("a \r\x7f\x03"), [
            ("a", {"ch": ord("a")}), (" ", {"vk": 0x20, "ch": 0x20}), ("\r", {"vk": 0x0D}),
            ("\x7f", {"vk": 0x08}), ("\x03", {"vk": ord("C"), "ctrl": True})])

    def test_escape_sequences(self):
        self.assertEqual(keys_of("\x1b[D\x1b[Z\x1b[3~"), [
            ("\x1b[D", {"vk": 0x25, "shift": False}), ("\x1b[Z", {"vk": 0x09, "shift": True}),
            ("\x1b[3~", {"vk": 0x2E, "shift": False})])

    def test_unknown_sequence_is_passed_as_is(self):
        self.assertEqual(keys_of("\x1b[15;5~"), [("\x1b[15;5~", None)])

    def test_paste_is_passed_as_is(self):
        self.assertEqual(keys_of("\x1b[200~kyouha\x1b[201~a"), [("\x1b[200~kyouha\x1b[201~", None), ("a", {"ch": ord("a")})])

    def test_split_reads_are_joined(self):
        keys, rest = tty_front.split_keys("a\x1b[")
        self.assertEqual((keys, rest), ([("a", {"ch": ord("a")})], "\x1b["))
        self.assertEqual(keys_of(rest + "D"), [("\x1b[D", {"vk": 0x25, "shift": False})])
        keys, rest = tty_front.split_keys("\x1b[200~kyou")
        self.assertEqual((keys, rest), ([], "\x1b[200~kyou"))

    def test_mouse_report_is_passed_as_is(self):
        self.assertEqual(keys_of("\x1b[M !!a"), [("\x1b[M !!", None), ("a", {"ch": ord("a")})])

    def test_escape_and_alt(self):
        self.assertEqual(keys_of("\x1b"), [("\x1b", {"vk": 0x1B})])
        self.assertEqual(keys_of("\x1bx"), [("\x1bx", {"ch": ord("x"), "alt": True})])


class OverlayTests(unittest.TestCase):
    def test_width_counts_wide_characters(self):
        view = {"text": "かな", "clauses": [], "converting": False, "candidates": None,
                "typedKeys": "", "spellingSuggestion": None, "hint": None}
        self.assertEqual(tty_front.overlay(view)[1], 4)

    def test_candidates(self):
        view = {"text": "漢字", "clauses": ["漢字"], "selectedClause": 0, "converting": True,
                "candidates": {"items": ["漢字", "感じ"], "numbered": True, "selected": 0}}
        self.assertEqual(tty_front.overlay(view)[1], 4 + len(" 1.") + 4 + len(" 2.") + 4)


if __name__ == "__main__":
    unittest.main()
