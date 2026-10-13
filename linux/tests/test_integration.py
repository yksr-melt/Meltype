#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 0924haruto12
"""Test the packaged IBus engine with real GI, NativeAOT and Mozc.

python3 linux/tests/test_integration.py linux/build/Meltype-linux
Requires python3-gi and gir1.2-ibus-1.0. Outputs go to a synthetic client;
daemon event delivery and desktop IME registration are outside this test.
All settings and learning data are temporary.
"""
import argparse
import importlib.machinery
import importlib.util
import json
import os
import selectors
import subprocess
import tempfile
import unittest
from pathlib import Path

import gi

gi.require_version("IBus", "1.0")
from gi.repository import IBus  # noqa: E402


class IntegrationTests(unittest.TestCase):
    package = None

    @classmethod
    def setUpClass(cls):
        cls.data = tempfile.TemporaryDirectory(prefix="meltype-ibus-integration-")
        cls.addClassCleanup(cls.data.cleanup)
        os.environ["MELTYPE_DATA_DIR"] = cls.data.name
        (Path(cls.data.name) / "config.json").write_text(json.dumps({
            "SpaceAroundEnglish": True, "LiveConversion": False, "SigilWordsDirect": False}), encoding="utf-8")
        loader = importlib.machinery.SourceFileLoader(
            "meltype_integration_product", str(cls.package / "ibus-engine-meltype"))
        spec = importlib.util.spec_from_loader(loader.name, loader)
        cls.product = importlib.util.module_from_spec(spec)
        loader.exec_module(cls.product)
        IBus.init()

        class ClientEngine(cls.product.MeltypeEngine):
            __gtype_name__ = "MeltypeIntegrationClientEngine"

            def __init__(self):
                self.document = ""
                self.marked = ""
                self.commits = []
                self.visible_candidates = []
                super().__init__()

            def commit_text(self, text):
                value = text.get_text()
                self.document += value
                self.commits.append(value)

            def delete_surrounding_text(self, offset, count):
                start = len(self.document) + offset
                self.document = self.document[:start] + self.document[start + count:]

            def get_surrounding_text(self):
                return IBus.Text.new_from_string(self.document), len(self.document), len(self.document)

            def update_preedit_text_with_mode(self, text, cursor, visible, mode):
                self.marked = text.get_text() if visible else ""

            def update_lookup_table(self, table, visible):
                self.visible_candidates = [table.get_candidate(i).get_text()
                                           for i in range(table.get_number_of_candidates())] if visible else []

            def hide_lookup_table(self):
                self.visible_candidates = []

            def update_auxiliary_text(self, text, visible):
                pass

            def hide_auxiliary_text(self):
                pass

            def register_properties(self, properties):
                pass

        cls.engine_class = ClientEngine

    def setUp(self):
        self.engine = self.engine_class()
        self.assertTrue(self.engine._session)
        self.addCleanup(self.engine.destroy)

    def scalar(self, text, state=0):
        keyval = IBus.unicode_to_keyval(text)
        self.assertEqual([ord(text)], [ord(c) for c in IBus.keyval_to_unicode(keyval)])
        consumed = self.engine.do_process_key_event(keyval, 0, state)
        if not consumed and not state & (IBus.ModifierType.CONTROL_MASK | IBus.ModifierType.MOD1_MASK
                                         | IBus.ModifierType.SUPER_MASK | IBus.ModifierType.RELEASE_MASK):
            self.engine.document += text
        return consumed

    def type_text(self, text):
        for scalar in text:
            self.scalar(scalar)

    def special(self, key, state=0):
        return self.engine.do_process_key_event(key, 0, state)

    def test_helper_ready_and_real_candidates(self):
        with tempfile.TemporaryDirectory(prefix="meltype-mozc-protocol-") as profile:
            process = subprocess.Popen([str(self.package / "mozc/meltype_mozc_helper"), profile],
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            self.addCleanup(lambda: process.kill() if process.poll() is None else None)
            selector = selectors.DefaultSelector()
            self.addCleanup(selector.close)
            selector.register(process.stdout, selectors.EVENT_READ)

            def line():
                self.assertTrue(selector.select(timeout=30), "Mozc response timed out")
                return process.stdout.readline().decode("utf-8").removesuffix("\n").removesuffix("\r")

            self.assertEqual("READY", line())
            process.stdin.write("C\t\tきょう\n".encode("utf-8"))
            process.stdin.flush()
            clauses = [record.split("\x1f") for record in line().split("\x1e")]
            self.assertEqual("きょう", "".join(clause[0] for clause in clauses))
            self.assertIn("今日", [candidate for clause in clauses for candidate in clause[1:]])
            process.stdin.write(b"Q\n")
            process.stdin.flush()
            self.assertEqual(0, process.wait(timeout=30))
            process.stdin.close()
            process.stdout.close()
            process.stderr.close()

    def test_real_mozc_candidate_selection(self):
        self.type_text("kyou")
        self.assertTrue(self.special(IBus.KEY_space))
        self.assertIn("今日", self.engine.visible_candidates)
        index = self.engine.visible_candidates.index("今日")
        page_size = self.engine._lookup.get_page_size()
        while self.engine._lookup.get_cursor_pos() // page_size < index // page_size:
            self.special(IBus.KEY_Page_Down)
        self.engine.do_candidate_clicked(index % page_size, 1, 0)
        self.assertTrue(self.special(IBus.KEY_Return))
        self.assertEqual("今日", self.engine.document)
        self.assertEqual("", self.engine.marked)

    def test_protected_actions(self):
        for token in ("@kuraido", "https://sakura.jp/kana", "taro@example.com", "./shumire-shon", r"C:\tools\kana"):
            for action in (IBus.KEY_Return, IBus.KEY_space, IBus.KEY_Tab, None):
                with self.subTest(token=token, action=action):
                    self.engine.document = ""
                    self.type_text(token)
                    if action is None:
                        self.engine.do_focus_out()
                    else:
                        consumed = self.special(action)
                        self.assertEqual(action != IBus.KEY_Tab, consumed)
                    self.assertEqual(token + (" " if action == IBus.KEY_space else ""), self.engine.document)
                    self.assertEqual("", self.engine.marked)

    def test_mixed_protection(self):
        for token in ("@kuraido", "https://sakura.jp/kana", "taro@example.com", "./shumire-shon", r"C:\tools\kana", "@onegaishimsu"):
            for action in (IBus.KEY_Return, IBus.KEY_space, IBus.KEY_Tab, None):
                with self.subTest(token=token, action=action):
                    self.engine.document = ""
                    self.type_text("kyouha「" + token + "」ashita")
                    if action is None:
                        self.engine.do_focus_out()
                    else:
                        self.special(action)
                        if action == IBus.KEY_space:
                            self.special(IBus.KEY_Return)
                    self.assertIn("「" + token + "」", self.engine.document)
                    self.assertNotIn("kyouha", self.engine.document)
                    self.assertNotIn("ashita", self.engine.document)
                    self.assertEqual("", self.engine.marked)

    def test_backspace_protected_boundary(self):
        for raw in ("./z]", "./z[", "@z]", "@z["):
            with self.subTest(raw=raw):
                self.engine.document = ""
                self.type_text(raw)
                self.special(IBus.KEY_BackSpace)
                self.assertEqual(raw[:-1], self.engine.marked)
                self.special(IBus.KEY_Return)
                self.assertEqual(raw[:-1], self.engine.document)

    def test_unicode_pass_through_once(self):
        for raw, scalar, expected in (("e", "\u0301", "e\u0301"), ("ka", "\u3099", "か\u3099"),
                                      ("ka", "\ufe0f", "か\ufe0f"), ("ka", "\U000e0100", "か\U000e0100"),
                                      ("ka", "😀", "か😀"), ("ka", "𠮷", "か𠮷")):
            with self.subTest(scalar=ord(scalar)):
                self.engine.document = ""
                self.type_text(raw)
                self.assertFalse(self.scalar(scalar))
                self.assertEqual([ord(c) for c in expected], [ord(c) for c in self.engine.document])
                self.assertEqual("", self.engine.marked)

    def test_focus_commits_once(self):
        self.type_text("@kuraido")
        self.engine.do_focus_out()
        self.engine.do_focus_out()
        self.assertEqual(["@kuraido"], self.engine.commits)

    def test_reconversion_does_not_delete_changed_pasted_text(self):
        self.engine.document = "pasted sentence"
        self.engine._apply({"commits": [{"deleteBefore": 4, "expect": "word", "text": "って"}], "view": None})
        self.assertEqual("pasted sentence", self.engine.document)

    def test_reconversion_replaces_matching_previous_text(self):
        self.engine.document = "previous"
        self.engine._apply({"commits": [{"deleteBefore": 8, "expect": "previous", "text": "replacement"}], "view": None})
        self.assertEqual("replacement", self.engine.document)

    def test_manual_width_and_kana(self):
        self.type_text("@kuraido")
        self.special(IBus.KEY_F9)
        self.special(IBus.KEY_Return)
        self.assertEqual("＠ｋｕｒａｉｄｏ", self.engine.document)
        self.engine.document = ""
        self.type_text("e")
        self.special(IBus.KEY_F6)
        self.assertFalse(self.scalar("\u0301"))
        self.assertEqual("え\u0301", self.engine.document)

    def test_direct_and_kana_modes(self):
        self.special(IBus.KEY_Eisu_toggle)
        self.type_text("ka")
        self.assertEqual("ka", self.engine.document)
        self.special(IBus.KEY_Hiragana_Katakana)
        self.type_text("ka")
        self.special(IBus.KEY_F6)
        self.special(IBus.KEY_Return)
        self.assertEqual("ka か", self.engine.document)

    def test_release_ignored_and_shortcut_passed_once(self):
        self.type_text("@kuraido")
        self.assertFalse(self.scalar("a", IBus.ModifierType.RELEASE_MASK))
        self.assertEqual("@kuraido", self.engine.marked)
        self.assertFalse(self.scalar("a", IBus.ModifierType.CONTROL_MASK))
        self.assertEqual("@kuraido", self.engine.document)
        self.assertEqual(["@kuraido"], self.engine.commits)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    IntegrationTests.package = args.package.resolve(strict=True)
    unittest.main(argv=[__file__], verbosity=2)
