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
        "CompositionTheme": {"enum": ["Dark"], "title": "変換ボックスの色", "x-category": "8. 表示", "x-windows-only": True},
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

    def test_set_keeps_other_keys_and_reads_defaults(self):
        self.path.write_text('{"SettingsVersion": 12, "AppRules": [{"Process": "code"}], "Unknown": 1}', encoding="utf-8")
        config = settings.ConfigFile(self.directory, SCHEMA)
        self.assertEqual("Balanced", config.get("DetectionLevel"), "書いていない項目は schema の既定値")
        config.set("DetectionLevel", "Aggressive")
        written = json.loads(self.path.read_text(encoding="utf-8"))
        self.assertEqual({"SettingsVersion": 12, "AppRules": [{"Process": "code"}], "Unknown": 1, "DetectionLevel": "Aggressive"}, written,
                         "画面で編集しない項目・知らない項目を消さない")
        config.set("Enabled", False)
        self.assertEqual(False, json.loads(self.path.read_text(encoding="utf-8"))["Enabled"], "自分で書いた後も続けて保存できる")

    def test_missing_config_is_written_with_current_settings_version(self):
        config = settings.ConfigFile(self.directory, SCHEMA)
        config.set("Enabled", False)
        written = json.loads(self.path.read_text(encoding="utf-8"))
        self.assertEqual(12, written["SettingsVersion"], "Meltype が古い版の設定として移行しないように、今の版を書く")
        self.assertEqual("./config.schema.json", written["$schema"])

    def test_refuses_to_overwrite_external_change(self):
        self.path.write_text('{"SettingsVersion": 12}', encoding="utf-8")
        config = settings.ConfigFile(self.directory, SCHEMA)
        self.path.write_text('{"SettingsVersion": 12, "Punctuation": "Japanese"}', encoding="utf-8")
        with self.assertRaises(settings.SettingsError):
            config.set("Enabled", False)
        self.assertEqual('{"SettingsVersion": 12, "Punctuation": "Japanese"}', self.path.read_text(encoding="utf-8"),
                         "ほかで書き換えた中身を上書きしない")

    def test_refuses_to_overwrite_config_created_after_opening(self):
        config = settings.ConfigFile(self.directory, SCHEMA)
        self.path.write_text('{"SettingsVersion": 12}', encoding="utf-8")
        with self.assertRaises(settings.SettingsError):
            config.set("Enabled", False)

    def test_refuses_to_open_config_with_comments(self):
        self.path.write_text('{\n  // コメント\n  "SettingsVersion": 12,\n}', encoding="utf-8")
        with self.assertRaises(settings.SettingsError):
            settings.ConfigFile(self.directory, SCHEMA)


class ItemTests(unittest.TestCase):
    def test_groups_titled_items_by_category_with_other_last(self):
        groups = [(category, [key for key, _ in items]) for category, items in settings.load_items(SCHEMA)]
        self.assertEqual([("1. 全般", ["Enabled", "Punctuation"]), ("2. 判定", ["DetectionLevel"]), ("10. アプリ", ["AppRules"]),
                          ("その他", ["ShowGuide"])], groups,
                         "番号は数の順 (10 は 2 の後)。Windows のみの項目は出さない (項目が無くなった分類も出さない)")
        self.assertEqual("全般", settings.category_label("1. 全般"))

    def test_kind(self):
        self.assertEqual("string", settings.kind({"type": ["string", "null"]}))
        self.assertEqual("enum", settings.kind({"enum": ["A", None]}))
        self.assertIsNone(settings.kind({"type": ["array", "null"]}), "配列は画面で編集しない")


if __name__ == "__main__":
    unittest.main()
