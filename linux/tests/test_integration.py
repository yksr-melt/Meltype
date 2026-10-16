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
                # (メソッド, 値) の発行順。送信順序 (確定と preedit 更新の前後) の検証に使う。
                self.events = []
                self.visible_candidates = []
                super().__init__()

            def commit_text(self, text):
                value = text.get_text()
                self.document += value
                self.commits.append(value)
                # 実際のクライアント (GTK / Qt / Chromium) は commit-text で変換中の表示を消す
                self.marked = ""
                self.events.append(("commit", value))

            def delete_surrounding_text(self, offset, count):
                start = len(self.document) + offset
                self.document = self.document[:start] + self.document[start + count:]
                self.events.append(("delete_surrounding", count))

            def get_surrounding_text(self):
                return IBus.Text.new_from_string(self.document), len(self.document), len(self.document)

            def update_preedit_text_with_mode(self, text, cursor, visible, mode):
                self.marked = text.get_text() if visible else ""
                self.events.append(("preedit", self.marked))

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

    def test_no_empty_preedit_before_commit(self):
        # 確定 (commit-text) の直前に「変換中の文字を消すだけ」の preedit 更新を送らない。
        # 送ると Chromium 系 (Wayland text-input-v3) が変換中の文字を確定と解釈し、
        # 続く commit-text で同じ文字が二重に入る (fcitx5 側の issue #486 と同じ問題)。
        self.type_text("kyouha")
        self.assertTrue(self.special(IBus.KEY_Return))
        self.assertEqual(1, len(self.engine.commits))
        for index, (kind, value) in enumerate(self.engine.events):
            if kind == "commit":
                if index > 0 and self.engine.events[index - 1][0] == "preedit":
                    self.assertNotEqual("", self.engine.events[index - 1][1],
                                        "確定の直前に空の preedit 更新が送られている")

    def test_preedit_cleared_after_commit(self):
        # 確定のあとに preedit を消す更新を送る。送らないと、確定と送信が同時に起きたあと
        # 入力欄を再度フォーカスしたとき、コンポジターが残った変換中の文字を再表示する
        # (Electron (Discord) で確定前のかなが再入力される問題)。
        self.type_text("kyouha")
        self.assertTrue(self.special(IBus.KEY_Return))
        self.assertEqual("", self.engine.marked)
        commit = max(i for i, (kind, _) in enumerate(self.engine.events) if kind == "commit")
        tail = self.engine.events[commit:]
        self.assertTrue(any(kind == "preedit" and value == "" for kind, value in tail),
                        "確定のあとに preedit を消す更新が送られていない")

    def test_recommit_deletes_without_hiding_preedit(self):
        # 確定し直し (deleteBefore あり) でも、確定の前に preedit を消す更新を送らない。
        # 送ると Chromium 系 (Wayland text-input-v3) が変換中の文字を確定と解釈して挿入し (issue #486)、
        # 続く削除がずれた位置を消して壊れる (い + want → いwanI want)。
        # 変換中の文字は surrounding の削除対象にならないので、消してから入れてよい。
        # 確定のあとの preedit クリアは下の else 側が送る (test_preedit_cleared_after_commit)。
        self.type_text("i")
        self.special(IBus.KEY_space)
        self.special(IBus.KEY_Return)
        self.type_text("want")
        self.special(IBus.KEY_Return)
        self.assertIn("I want", self.engine.commits)
        events = self.engine.events
        last_commit = max(i for i, (kind, _) in enumerate(events) if kind == "commit")
        delete = next(i for i, (kind, _) in enumerate(events) if kind == "delete_surrounding")
        self.assertLess(delete, last_commit, "確定し直しの削除がコミットの前にある")
        # 「want」の preedit 表示の後、「I want」のコミットまでの間に空の preedit 更新が無い。
        shown = max(i for i in range(last_commit)
                    if events[i][0] == "preedit" and events[i][1] != "")
        middle = events[shown + 1:last_commit]
        self.assertFalse(any(kind == "preedit" and value == "" for kind, value in middle),
                         "確定し直しで、preedit を消す更新が確定の前に送られている")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    args = parser.parse_args()
    IntegrationTests.package = args.package.resolve(strict=True)
    unittest.main(argv=[__file__], verbosity=2)
