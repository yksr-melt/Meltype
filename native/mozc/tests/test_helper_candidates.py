#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 HarukiKubota
"""本物の Mozc ヘルパーで、文字の候補と設定を操作する候補の扱いを確かめる。

MELTYPE_MOZC=/path/to/meltype_mozc_helper python3 native/mozc/tests/test_helper_candidates.py
"""

import os
import subprocess
import tempfile
import unittest


UNIT = "\x1f"
RECORD = "\x1e"
USER_WORD = "\x1d"
COMMANDS = {
    "シークレットモードをオン",
    "シークレットモードをオフ",
    "サジェスト機能の一時停止",
    "サジェスト機能を元に戻す",
}


class HelperCandidatesTests(unittest.TestCase):
    def request(self, *requests):
        # 学習や設定の影響を受けないよう、呼び出しごとに新しいプロファイルを使う。
        with tempfile.TemporaryDirectory(prefix="meltype-mozc-candidates-") as profile:
            result = subprocess.run(
                [os.environ["MELTYPE_MOZC"], profile],
                input="\n".join((*requests, "Q", "")),
                text=True, encoding="utf-8", capture_output=True,
                check=True, timeout=60,
            )
        lines = result.stdout.split("\n")
        self.assertEqual(lines[0], "READY", result.stderr)
        self.assertEqual(len(lines), len(requests) + 2, result.stdout)
        return lines[1:-1]

    def candidates(self, answer):
        candidates = []
        for segment in answer.split(RECORD):
            words = segment.split(UNIT)[1:]
            self.assertTrue(words, answer)
            candidates.extend(words)
        return candidates

    def test_incognito_commands_are_not_text_candidates(self):
        for reading, expected in (
            ("ひみつ", "秘密"),
            ("しーくれっと", "シークレット"),
            ("しーくれっともーど", "シークレットモード"),
            ("ぷらいばしー", "プライバシー"),
            ("ぷらいべーと", "プライベート"),
            ("こまんど", "コマンド"),
        ):
            with self.subTest(reading=reading):
                candidates = self.candidates(self.request(f"C\t\t{reading}")[0])
                self.assertIn(expected, candidates)
                self.assertTrue(COMMANDS.isdisjoint(candidates), candidates)

    def test_suggestion_commands_are_not_text_candidates(self):
        for reading in (
            "さじぇすと", "ぷれぜんてーしょん", "ぷれぜん", "よそく",
            "よそくにゅうりょく", "よそくへんかん", "すいそくこうほ",
        ):
            with self.subTest(reading=reading):
                candidates = self.candidates(self.request(f"C\t\t{reading}")[0])
                self.assertTrue(COMMANDS.isdisjoint(candidates), candidates)

    def test_ordinary_conversion_candidates_remain(self):
        for reading, expected in (("にほんご", "日本語"), ("かんじ", "漢字")):
            with self.subTest(reading=reading):
                self.assertIn(expected, self.candidates(self.request(f"C\t\t{reading}")[0]))

    def test_user_word_with_command_text_remains(self):
        # 操作用と同じ文言でも、利用者が入力するために登録した語は候補に残す。
        word = "シークレットモードをオン"
        status, answer = self.request(
            f"D\tひみつ{UNIT}{word}{UNIT}名詞", "C\t\tひみつ",
        )
        self.assertEqual(status, "OK")
        candidates = self.candidates(answer)
        self.assertIn(USER_WORD + word, candidates)
        self.assertTrue(COMMANDS.isdisjoint(candidates), candidates)


if __name__ == "__main__":
    unittest.main()
