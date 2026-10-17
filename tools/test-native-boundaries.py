#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 0924haruto12
"""NativeAOT の C ABI を検証する。変換コールバックはスタブ、設定・学習は一時ディレクトリ。

python3 tools/test-native-boundaries.py path/to/MeltypeNative.dylib
Linux の libMeltypeNative.so でも同じテストを実行できる。
"""

import argparse
import ctypes
import json
import os
import tempfile
import unittest
from pathlib import Path


class NativeBoundaryTests(unittest.TestCase):
    library_path = None

    @classmethod
    def setUpClass(cls):
        cls.data = tempfile.TemporaryDirectory(prefix="meltype-native-boundaries-")
        cls.addClassCleanup(cls.data.cleanup)
        # 起動時の旧データ移行も避けるため、既に存在する空のディレクトリを渡す。
        os.environ["MELTYPE_DATA_DIR"] = cls.data.name
        data = Path(cls.data.name)
        (data / "config.json").write_text(
            json.dumps({"SpaceAroundEnglish": True}), encoding="utf-8")
        # 学習データはプロセスで初回だけ読む（#558）。meltype_create より前に置く。
        (data / "languages.json").write_text(
            json.dumps({
                "sushi": {
                    "English": True,
                    "Count": 2,
                    "Explicit": True,
                    "Used": "2026-10-07T00:00:00Z",
                }
            }),
            encoding="utf-8",
        )
        cls.lib = ctypes.CDLL(str(cls.library_path))
        cls.lib.meltype_init.argtypes = [ctypes.c_void_p] * 3
        cls.lib.meltype_init.restype = ctypes.c_int
        cls.lib.meltype_create.restype = ctypes.c_void_p
        cls.lib.meltype_destroy.argtypes = [ctypes.c_void_p]
        cls.lib.meltype_handle_key.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int,
                                              ctypes.c_char_p, ctypes.c_char_p]
        cls.lib.meltype_handle_key.restype = ctypes.c_void_p
        cls.lib.meltype_free.argtypes = [ctypes.c_void_p]
        cls.lib.meltype_set_direct.argtypes = [ctypes.c_void_p, ctypes.c_int]
        cls.lib.meltype_set_code_input.argtypes = [ctypes.c_void_p, ctypes.c_int]
        assert cls.lib.meltype_init(None, None, None) == 1

    def setUp(self):
        self.session = self.lib.meltype_create()
        self.assertTrue(self.session, "native session creation")
        self.addCleanup(self.lib.meltype_destroy, self.session)
        self.assertFalse((Path(self.data.name) / "config.json.broken").exists(), "synthetic config could not be loaded")
        self.assertIn("SettingsVersion", json.loads((Path(self.data.name) / "config.json").read_text()),
                      "legacy synthetic config was not migrated and saved")

    def key(self, scalar, vk=None, after=None, before=None):
        if vk is None:
            vk = scalar - 0x20 if ord("a") <= scalar <= ord("z") else 0x07
        pointer = self.lib.meltype_handle_key(self.session, vk, scalar, 0, None if before is None else before.encode("utf-8"),
                                             None if after is None else after.encode("utf-8"))
        self.assertTrue(pointer, "C ABI returned NULL")
        try:
            return json.loads(ctypes.string_at(pointer).decode("utf-8"))
        finally:
            self.lib.meltype_free(pointer)

    def type_text(self, text, after=None):
        for char in text:
            self.key(ord(char), after=after)

    def assert_pass(self, result, text=None):
        self.assertFalse(result["consumed"])
        self.assertEqual([] if text is None else [{"deleteBefore": 0, "text": text}], result["commits"])
        self.assertIsNone(result["view"])

    def test_bmp_accent_keeps_raw_base(self):
        self.type_text("e")
        result = self.key(0x0301)
        self.assert_pass(result, "e")
        # アプリが元イベントを 1 回受け取った場合のテキスト。IME は mark を挿入しない。
        self.assertEqual("e\u0301", result["commits"][0]["text"] + chr(0x0301))
        self.assert_pass(self.key(0x0300))

    def test_accent_after_word(self):
        self.type_text("cafe")
        self.assert_pass(self.key(0x0301), "cafe")

    def test_kana_combining_mark_keeps_display(self):
        self.type_text("ka")
        self.assert_pass(self.key(0x3099), "か")

    def test_variation_selector_keeps_display(self):
        self.type_text("ka")
        self.assert_pass(self.key(0xFE0F), "か")

    def test_supplementary_variation_selector_keeps_display(self):
        self.type_text("ka")
        self.assert_pass(self.key(0xE0100), "か")

    def test_explicit_kana_mode_wins(self):
        self.type_text("e")
        self.key(0, vk=0x75)  # F6
        self.assert_pass(self.key(0x0301), "え")

    def test_supplementary_emoji_keeps_japanese(self):
        self.type_text("kyouha")
        self.assert_pass(self.key(0x1F60A), "きょうは")

    def test_supplementary_kanji_keeps_japanese(self):
        self.type_text("kyouha")
        self.assert_pass(self.key(0x20BB7), "きょうは")

    def test_invalid_scalars_do_not_change_state(self):
        initial = self.key(ord("e"))
        for scalar in (-1, 0xD800, 0xDFFF, 0x110000, 0x7FFFFFFF):
            with self.subTest(scalar=scalar):
                result = self.key(scalar)
                self.assertFalse(result["consumed"])
                self.assertEqual([], result["commits"])
                self.assertEqual(initial["view"], result["view"])

    def test_empty_composition_passes_marks_and_emoji(self):
        for scalar in (0x0301, 0x3099, 0xFE0F, 0x1F60A, 0xE0100):
            with self.subTest(scalar=scalar):
                self.assert_pass(self.key(scalar))

    def test_next_word_still_composes_after_pass_through(self):
        self.type_text("e")
        self.assert_pass(self.key(0x0301), "e")
        self.type_text("kyouha")
        enter = self.key(0, vk=0x0D)
        self.assertTrue(enter["consumed"])
        self.assertEqual([{"deleteBefore": 0, "text": "きょうは"}], enter["commits"])
        self.assertIsNone(enter["view"])

    def test_direct_mode_passes_accent_without_commit(self):
        self.lib.meltype_set_direct(self.session, 1)
        self.assert_pass(self.key(ord("e")))
        self.assert_pass(self.key(0x0301))

    def test_code_hash_directives_pass_every_key(self):
        self.lib.meltype_set_code_input(self.session, 1)
        for surrounding in (True, False):
            for text in ("#include", "#region", "r#type", "r#match"):
                with self.subTest(surrounding=surrounding, text=text):
                    self.lib.meltype_set_code_input(self.session, 1)
                    before = ""
                    for char in text:
                        self.assert_pass(self.key(ord(char), before=before if surrounding else None))
                        before += char
                    self.assert_pass(self.key(0, vk=0x0D, before=before if surrounding else None))

    def test_code_hash_comment_still_composes_japanese(self):
        self.lib.meltype_set_code_input(self.session, 1)
        self.assert_pass(self.key(ord("#")))
        self.key(ord(" "), vk=0x20)
        self.type_text("kyouha")
        committed = self.key(0, vk=0x0D)["commits"]
        self.assertEqual([{"deleteBefore": 0, "text": "きょうは"}], committed)

    def test_kana_combining_mark_before_english(self):
        self.type_text("ka", after="world")
        self.assert_pass(self.key(0x3099), "か")

    def test_variation_selector_before_english(self):
        self.type_text("ka", after="world")
        self.assert_pass(self.key(0xFE0F), "か")

    def test_supplementary_variation_selector_before_english(self):
        self.type_text("ka", after="world")
        self.assert_pass(self.key(0xE0100), "か")

    def test_explicit_kana_accent_before_english(self):
        self.type_text("e", after="world")
        self.key(0, vk=0x75)
        self.assert_pass(self.key(0x0301), "え")

    def test_normal_enter_keeps_english_spacing(self):
        self.type_text("ka", after="world")
        result = self.key(0, vk=0x0D)
        self.assertTrue(result["consumed"])
        self.assertEqual([{"deleteBefore": 0, "text": "か "}], result["commits"])

    def test_name_context_keeps_unknown_name(self):
        for char in "taro":
            self.key(ord(char), before="my name is ")
        self.assertEqual("taro", self.key(0, vk=0x0D)["commits"][0]["text"])

    def test_english_term_particle_tail(self):
        self.type_text("apinoerror")
        self.assertEqual("api の error", self.key(0, vk=0x0D)["commits"][0]["text"])

    def test_mixed_protection_preserves_tokens_and_japanese(self):
        for token in ("@kuraido", "https://sakura.jp/kana", "taro@example.com", "./shumire-shon", r"C:\tools\kana"):
            for action in (0x0D, 0x20, 0x09):
                with self.subTest(token=token, action=action):
                    self.type_text("kyouha「" + token + "」ashita")
                    result = self.key(0, vk=action)
                    if action == 0x20:
                        result = self.key(0, vk=0x0D)
                    self.assertEqual([{"deleteBefore": 0, "text": "きょうは「" + token + "」あした"}], result["commits"])
                    self.assertIsNone(result["view"])

    def test_backspace_keeps_protected_char_of_crossing_unit(self):
        for raw in ("./z]", "./z[", "@z]", "@z["):
            with self.subTest(raw=raw):
                self.type_text(raw)
                result = self.key(0, vk=0x08)
                self.assertEqual(raw[:-1], result["view"]["text"])
                self.assertEqual(raw[:-1], self.key(0, vk=0x0D)["commits"][0]["text"])

    def test_mixed_protected_typo_is_not_autocorrected(self):
        self.type_text("kyouha「@onegaishimsu」ashita")
        self.assertEqual("きょうは「@onegaishimsu」あした", self.key(0, vk=0x0D)["commits"][0]["text"])

    def test_language_choice_is_shared_across_sessions(self):
        # #558 以降、学習はプロセスで共有される。setUpClass で置いた languages.json が
        # 別セッションでも効くことを確かめる（作成後の再読込はしない）。
        restored = self.lib.meltype_create()
        self.assertTrue(restored)
        original = self.session
        self.session = restored
        try:
            self.type_text("sushi")
            self.assertEqual("sushi", self.key(0, vk=0x0D)["commits"][0]["text"])
        finally:
            self.session = original
            self.lib.meltype_destroy(restored)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path)
    args = parser.parse_args()
    NativeBoundaryTests.library_path = args.library.resolve(strict=True)
    unittest.main(argv=[__file__], verbosity=2)
