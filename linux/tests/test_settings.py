#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 aster-void
"""設定画面 (meltype-settings) の config.json の読み書きと項目の並びを、GTK なしで確かめる。"""

import importlib.machinery
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SOURCE = Path(__file__).resolve().parents[1] / "meltype-settings"
loader = importlib.machinery.SourceFileLoader("meltype_settings", str(SOURCE))
settings = importlib.util.module_from_spec(importlib.util.spec_from_loader(loader.name, loader))
loader.exec_module(settings)

SCHEMA = {
    "properties": {
        "$schema": {"type": ["string", "null"]},
        "Enabled": {"type": "boolean", "title": "有効", "x-category": "1. 全般", "default": True},
        "ShowGuide": {"type": "boolean", "title": "操作ガイド", "default": True},
        "DetectionLevel": {"enum": ["Aggressive", "Balanced"], "title": "強さ", "x-category": "2. 判定", "default": "Balanced"},
        "Punctuation": {"enum": ["Japanese"], "title": "句読点", "x-category": "1. 全般", "default": "Japanese"},
        "AppRules": {"type": "array", "title": "アプリ別設定", "x-category": "10. アプリ"},
        "CompositionTheme": {"enum": ["Dark"], "title": "変換ボックスの色", "x-category": "8. 表示", "x-platforms": ["Windows"]},
        "UseZenzai": {"type": "boolean", "title": "Zenzai を使う", "x-category": "2. 判定", "x-platforms": ["Windows", "Mac"]},
        "Learning": {"type": "boolean", "title": "学習", "x-category": "2. 判定", "x-platforms": ["Mac", "Linux"]},
        "SettingsVersion": {"type": "integer", "default": 12},
    }
}


class ConfigFileTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.directory = Path(self.temp.name)
        self.path = self.directory / "config.json"

    def tearDown(self):
        self.temp.cleanup()

    def test_save_keeps_other_keys_and_reads_defaults(self):
        self.path.write_text('{"SettingsVersion": 12, "AppRules": [{"Process": "code"}], "Unknown": 1}', encoding="utf-8")
        config = settings.ConfigFile(self.directory, SCHEMA)
        self.assertEqual("Balanced", config.get("DetectionLevel"), "書いていない項目は schema の既定値")
        config.set("DetectionLevel", "Aggressive")
        config.save()
        written = json.loads(self.path.read_text(encoding="utf-8"))
        self.assertEqual({"SettingsVersion": 12, "AppRules": [{"Process": "code"}], "Unknown": 1, "DetectionLevel": "Aggressive"}, written,
                         "画面で編集しない項目・知らない項目を消さない")

    def test_set_does_not_write_until_save(self):
        original = '{"SettingsVersion": 12}'
        self.path.write_text(original, encoding="utf-8")
        config = settings.ConfigFile(self.directory, SCHEMA)
        config.set("Enabled", False)
        self.assertEqual(original, self.path.read_text(encoding="utf-8"), "キャンセルで捨てられるように、save まで書かない")

    def test_missing_config_is_written_with_current_settings_version(self):
        config = settings.ConfigFile(self.directory, SCHEMA)
        config.set("Enabled", False)
        config.save()
        written = json.loads(self.path.read_text(encoding="utf-8"))
        self.assertEqual(12, written["SettingsVersion"], "Meltype が古い版の設定として移行しないように、今の版を書く")
        self.assertEqual("./config.schema.json", written["$schema"])

    def test_save_without_edits_does_not_create_config(self):
        settings.ConfigFile(self.directory, SCHEMA).save()
        self.assertFalse(self.path.exists())

    def test_edit_back_to_opened_value_does_not_overwrite_external_change(self):
        self.path.write_text('{"SettingsVersion": 12, "Enabled": true}', encoding="utf-8")
        config = settings.ConfigFile(self.directory, SCHEMA)
        config.set("Enabled", False)
        config.set("Enabled", True)
        external = '{"SettingsVersion": 12, "Enabled": false}'
        self.path.write_text(external, encoding="utf-8")
        config.save()
        self.assertEqual(external, self.path.read_text(encoding="utf-8"), "開いたときの値に戻した項目は編集していない扱い")

    def test_save_merges_edits_onto_external_change(self):
        self.path.write_text('{"SettingsVersion": 12, "DetectionLevel": "Balanced"}', encoding="utf-8")
        config = settings.ConfigFile(self.directory, SCHEMA)
        self.path.write_text('{"SettingsVersion": 12, "DetectionLevel": "Aggressive", "Punctuation": "Japanese"}', encoding="utf-8")
        config.set("Enabled", False)
        config.save()
        self.assertEqual({"SettingsVersion": 12, "DetectionLevel": "Aggressive", "Punctuation": "Japanese", "Enabled": False},
                         json.loads(self.path.read_text(encoding="utf-8")), "編集していない項目は、ほかで書き換えた中身を残す")

    def test_save_refuses_config_broken_after_opening(self):
        config = settings.ConfigFile(self.directory, SCHEMA)
        broken = '{\n  // コメント\n  "SettingsVersion": 12,\n}'
        self.path.write_text(broken, encoding="utf-8")
        config.set("Enabled", False)
        with self.assertRaises(settings.SettingsError):
            config.save()
        self.assertEqual(broken, self.path.read_text(encoding="utf-8"), "読めない中身を上書きしない")

    def test_refuses_to_open_config_with_comments(self):
        self.path.write_text('{\n  // コメント\n  "SettingsVersion": 12,\n}', encoding="utf-8")
        with self.assertRaises(settings.SettingsError):
            settings.ConfigFile(self.directory, SCHEMA)


class ItemTests(unittest.TestCase):
    def test_groups_titled_items_by_category_with_other_last(self):
        groups = [(category, [key for key, _ in items]) for category, items in settings.load_items(SCHEMA)]
        self.assertEqual([("1. 全般", ["Enabled", "Punctuation"]), ("2. 判定", ["DetectionLevel", "Learning"]), ("10. アプリ", ["AppRules"]),
                          ("その他", ["ShowGuide"])], groups,
                         "番号は数の順 (10 は 2 の後)。Linux で使わない項目は出さない (項目が無くなった分類も出さない)")
        self.assertEqual("全般", settings.category_label("1. 全般"))

    def test_kind(self):
        self.assertEqual("string", settings.kind({"type": ["string", "null"]}))
        self.assertEqual("enum", settings.kind({"enum": ["A", None]}))
        self.assertIsNone(settings.kind({"type": ["array", "null"]}), "配列は画面で編集しない")


if __name__ == "__main__":
    unittest.main()
