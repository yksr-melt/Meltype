#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 souta-lab
"""IBus・Native なしで、補助テキストに出す案内の選び方 (ShowGuide) を検証する。

本体が返す view の hint (固定の操作ガイド)・contextHint (英字・予測などの文脈の案内)・
suggestion (もしかして) の組み合わせごとに、ShowGuide の ON / OFF で何が出るかを確かめる。
実 Native・Mozc・デーモンの検証は test_integration.py / test_daemon.py で行う。
"""

import ast
import unittest
from pathlib import Path
from unittest.mock import Mock


def load_engine():
    source = Path(__file__).resolve().parents[1] / "ibus-engine-meltype"
    parsed = ast.parse(source.read_text(encoding="utf-8"), filename=str(source))
    product = next(n for n in parsed.body if isinstance(n, ast.ClassDef) and n.name == "MeltypeEngine")
    names = {"_auxiliary_hint", "_apply"}
    methods = [n for n in product.body if isinstance(n, ast.FunctionDef) and n.name in names]
    engine = ast.ClassDef(name="Engine", bases=[], keywords=[], body=methods, decorator_list=[])
    if "type_params" in ast.ClassDef._fields:
        engine.type_params = []
    module = ast.fix_missing_locations(ast.Module(body=[engine], type_ignores=[]))
    namespace = {}
    exec(compile(module, str(source), "exec"), namespace)
    return namespace["Engine"]()


class GuideHintTests(unittest.TestCase):
    def setUp(self):
        self.engine = load_engine()

    def hint(self, view, show_guide):
        return self.engine._auxiliary_hint(view, show_guide)

    def test_guide_on_shows_fixed_hint(self):
        view = {"hint": "Space で変換", "contextHint": None}
        self.assertEqual("Space で変換", self.hint(view, True))

    def test_guide_off_hides_fixed_hint(self):
        view = {"hint": "Space で変換", "contextHint": None}
        self.assertEqual("", self.hint(view, False), "固定の操作ガイドだけの表示は消える (#1539)")

    def test_guide_off_keeps_context_hint(self):
        view = {"hint": "Tab → google (英字に)　Space で変換", "contextHint": "Tab → google (英字に)"}
        self.assertEqual("Tab → google (英字に)", self.hint(view, False),
                         "OFF でも英字・予測などの文脈の案内は残す (fcitx5 と同じ)")

    def test_guide_on_prefers_full_hint(self):
        view = {"hint": "Tab → google (英字に)　Space で変換", "contextHint": "Tab → google (英字に)"}
        self.assertEqual("Tab → google (英字に)　Space で変換", self.hint(view, True))

    def test_suggestion_is_always_shown_first(self):
        view = {"hint": "Space で変換", "contextHint": None, "suggestion": "もしかして: teh (Tab で修正)"}
        self.assertEqual("もしかして: teh (Tab で修正)　Space で変換", self.hint(view, True))
        self.assertEqual("もしかして: teh (Tab で修正)", self.hint(view, False),
                         "ガイドを消しても訂正の提案は残し、後ろに区切りだけ残らない")

    def test_old_native_without_context_hint_keeps_hint(self):
        view = {"hint": "Space で変換"}
        self.assertEqual("Space で変換", self.hint(view, False),
                         "案内を分けて返さない旧版の本体では、提案まで消さないよう従来の hint を使う")

    def test_no_hint(self):
        self.assertEqual("", self.hint({"hint": "", "contextHint": None}, True))
        self.assertEqual("", self.hint({"hint": "", "contextHint": None}, False))


class ShowGuideWiringTests(unittest.TestCase):
    """本体の結果 (SessionResult) の showGuide が、表示側まで届くことを確かめる (#1539 の再発防止)。"""

    def setUp(self):
        self.engine = load_engine()
        self.engine._show_preedit = Mock()
        self.engine._show_candidates = Mock()
        self.engine._hide_preedit = Mock()
        self.engine._hide_candidates = Mock()
        self.view = {"text": "かん", "hint": "Space で変換", "contextHint": None}

    def test_apply_forwards_show_guide_off(self):
        self.engine._apply({"commits": [], "view": self.view, "showGuide": False})
        self.engine._show_preedit.assert_called_once_with(self.view, False)

    def test_apply_forwards_show_guide_on(self):
        self.engine._apply({"commits": [], "view": self.view, "showGuide": True})
        self.engine._show_preedit.assert_called_once_with(self.view, True)

    def test_apply_without_show_guide_defaults_on(self):
        # showGuide を返さない旧版の本体では、従来どおり表示する
        self.engine._apply({"commits": [], "view": self.view})
        self.engine._show_preedit.assert_called_once_with(self.view, True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
