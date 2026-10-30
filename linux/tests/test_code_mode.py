#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 kuraneko1
"""IBus・Native なしで実際のエンジンの切り替え処理を検証する。

本体の判定を再実装せず、API の戻り値を指定して呼び出し元の分岐を確かめる。
実 Native・Mozc・デーモンの検証は test_integration.py / test_daemon.py で行う。
"""

import ast
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock


class CodeModeTests(unittest.TestCase):
    def setUp(self):
        source = Path(__file__).resolve().parents[1] / "ibus-engine-meltype"
        parsed = ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
        product = next(n for n in parsed.body if isinstance(n, ast.ClassDef) and n.name == "MeltypeEngine")
        names = {"_switch_mode", "_switch_code_mode", "_notify_caret_moved", "_reset_code_japanese",
                 "do_set_content_type", "do_focus_out", "do_reset"}
        methods = [n for n in product.body if isinstance(n, ast.FunctionDef) and n.name in names]
        engine = ast.ClassDef(name="Engine", bases=[], keywords=[], body=methods, decorator_list=[])
        if "type_params" in ast.ClassDef._fields:
            engine.type_params = []
        self.lib = SimpleNamespace(meltype_commit=Mock(return_value=None), meltype_set_direct=Mock())
        self.set_line = Mock(return_value=1)
        self.english_at = Mock(return_value=1)
        self.caret = Mock()
        self.set_input = Mock()
        self.set_terminal = Mock()
        self.namespace = {
            "_lib": self.lib, "_result": lambda value: value,
            "_set_code_line": self.set_line, "_code_english_at": self.english_at,
            "_caret_moved": self.caret, "_set_code_input": self.set_input,
            "_set_code_terminal": self.set_terminal,
            "ALWAYS_DIRECT_KEYS": ("eisu", "muhenkan"), "ZENKAKU_HANKAKU_KEYS": ("toggle",),
            "IBus": SimpleNamespace(InputPurpose=SimpleNamespace(TERMINAL="terminal")),
        }
        module = ast.fix_missing_locations(ast.Module(body=[engine], type_ignores=[]))
        exec(compile(module, str(source), "exec"), self.namespace)
        self.engine = self.namespace["Engine"]()
        self.engine._session = 123
        self.engine._direct = False
        self.engine._code_input = True
        self.engine._preedit_visible = False
        self.engine._apply = Mock()
        self.engine._surrounding_text = Mock(return_value=(None, None))
        self.engine._finalize_on_focus_loss = Mock()
        self.engine._hide_preedit = Mock()
        self.engine._hide_candidates = Mock()

    def test_toggle_reads_native_each_time(self):
        self.english_at.side_effect = [1, 0]
        self.engine._switch_mode("toggle")
        self.engine._switch_mode("toggle")
        self.assertEqual([((123, 1, None),), ((123, 0, None),)], self.set_line.call_args_list)
        self.assertEqual(2, self.english_at.call_count)
        self.lib.meltype_set_direct.assert_not_called()

    def test_native_change_between_switches_is_authoritative(self):
        # 本体で日本語指定が解除された場合、次も「日本語にする」操作になる。
        self.engine._switch_mode("toggle")
        self.engine._switch_mode("toggle")
        self.assertEqual([((123, 1, None),), ((123, 1, None),)], self.set_line.call_args_list)

    def test_caret_notification_does_not_predict_native_state(self):
        self.engine._notify_caret_moved(0x25, False)
        self.caret.assert_called_once_with(123, 0x25, 0)
        self.set_line.assert_not_called()
        self.engine._switch_mode("toggle")
        self.english_at.assert_called_once_with(123, None)

    def test_native_query_and_switch_receive_same_utf8_context(self):
        self.engine._surrounding_text.return_value = ('echo "日本語😊', None)
        self.engine._switch_mode("toggle")
        expected = 'echo "日本語😊'.encode()
        self.english_at.assert_called_once_with(123, expected)
        self.set_line.assert_called_once_with(123, 1, expected)

    def test_string_falls_back_to_direct_and_back(self):
        self.english_at.return_value = 0
        self.set_line.return_value = 0
        self.engine._switch_mode("toggle")
        self.assertTrue(self.engine._direct)
        self.engine._switch_mode("toggle")
        self.assertFalse(self.engine._direct)
        self.assertEqual([((123, 1),), ((123, 0),), ((123, 0),)],
                         self.lib.meltype_set_direct.call_args_list)

    def test_direct_mode_returns_to_japanese_code(self):
        self.engine._direct = True
        self.engine._switch_mode("toggle")
        self.assertFalse(self.engine._direct)
        self.lib.meltype_set_direct.assert_called_once_with(123, 0)
        self.set_line.assert_called_once_with(123, 1, None)

    def test_eisu_and_muhenkan_always_enter_direct(self):
        for key in ("eisu", "muhenkan"):
            for handled in (0, 1):
                with self.subTest(key=key, handled=handled):
                    self.engine._direct = False
                    self.set_line.return_value = handled
                    self.engine._switch_mode(key)
                    self.assertTrue(self.engine._direct)
                    self.assertEqual((123, 0, None), self.set_line.call_args.args)
        self.english_at.assert_not_called()

    def test_eisu_already_direct_does_not_query_code(self):
        self.engine._direct = True
        self.engine._switch_mode("eisu")
        self.set_line.assert_not_called()
        self.english_at.assert_not_called()
        self.assertTrue(self.engine._direct)

    def test_hiragana_and_henkan_request_japanese(self):
        for key in ("hiragana", "henkan"):
            with self.subTest(key=key):
                self.engine._switch_mode(key)
                self.assertEqual((123, 1, None), self.set_line.call_args.args)
                self.assertFalse(self.engine._direct)
        self.english_at.assert_not_called()

    def test_missing_native_api_uses_normal_ime_switch(self):
        for name in ("_set_code_input", "_set_code_line", "_code_english_at"):
            with self.subTest(name=name):
                original = self.namespace[name]
                self.namespace[name] = None
                self.engine._code_input = False
                self.engine._direct = False
                self.engine.do_set_content_type("terminal", 0)
                self.assertFalse(self.engine._code_input)
                self.engine._switch_mode("toggle")
                self.assertTrue(self.engine._direct)
                self.namespace[name] = original
        self.set_line.assert_not_called()

    def test_same_content_type_does_not_reset_native(self):
        self.engine.do_set_content_type("terminal", 0)
        self.engine.do_set_content_type("terminal", 0)
        self.set_input.assert_not_called()
        self.set_line.assert_not_called()

    def test_changed_content_type_resets_native(self):
        self.engine.do_set_content_type("free", 0)
        self.engine.do_set_content_type("terminal", 0)
        self.assertEqual([((123, 0),), ((123, 1),)], self.set_input.call_args_list)

    def test_reset_preserves_explicit_japanese(self):
        self.engine.do_reset()
        self.engine._finalize_on_focus_loss.assert_called_once()
        self.set_line.assert_not_called()
        self.set_input.assert_not_called()

    def test_focus_out_asks_native_to_clear_japanese(self):
        self.engine.do_focus_out()
        self.engine._finalize_on_focus_loss.assert_called_once()
        self.set_line.assert_called_once_with(123, 0, None)


if __name__ == "__main__":
    unittest.main(verbosity=2)
