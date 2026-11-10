#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 souta
"""native/mozc/merge-ut-dictionary.py のテスト。

Mozc の代わりに、id.def と辞書だけを持つ小さな偽の Mozc を git で作り、そこで統合を試す。
本物の Mozc も辞書も要らない (fixtures/ の辞書はテストのために作ったもので、実際の Mozc-UT のデータではない)。

python3 native/mozc/tests/test_merge_ut_dictionary.py
"""

import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "merge-ut-dictionary.py"
FIXTURES = Path(__file__).resolve().parent / "fixtures"
SAMPLE = FIXTURES / "mozcdic-ut-sample.txt"

# 偽の Mozc の id.def に入れる品詞
POS_GENERAL = "名詞,一般,*,*,*,*,*"
POS_PROPER = "名詞,固有名詞,一般,*,*,*,*"
ID_GENERAL = 1851
ID_PROPER = 1849

BASE_LINE = "ああると\t1851\t1851\t7129\tアアルト\n"
BASE_LINE_LOW = "ほげ\t1851\t1851\t5000\tホゲ\n"


def load_module():
    spec = importlib.util.spec_from_file_location("merge_ut_dictionary", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


merge_ut = load_module()


def make_id_def(general_id=ID_GENERAL, proper_id=ID_PROPER, count=1900) -> str:
    lines = []
    for pos_id in range(count):
        if pos_id == general_id:
            lines.append(f"{pos_id} {POS_GENERAL}")
        elif pos_id == proper_id:
            lines.append(f"{pos_id} {POS_PROPER}")
        else:
            lines.append(f"{pos_id} 補助記号,一般,*,*,*,*,*")
    return "\n".join(lines) + "\n"


# テストは、その人の git の設定 (commit.gpgsign など) に左右されないようにする。
GIT_CONFIG = (
    "-c", "user.name=test",
    "-c", "user.email=test@example.com",
    "-c", "commit.gpgsign=false",
    "-c", "init.defaultBranch=main",
)


def git(root: Path, *args: str, check: bool = False) -> subprocess.CompletedProcess:
    result = subprocess.run(
        ["git", "-C", str(root), *GIT_CONFIG, *args],
        capture_output=True, text=True, encoding="utf-8", errors="replace", check=False,
    )
    if check and result.returncode != 0:
        raise AssertionError(f"git {' '.join(args)} が失敗しました: {result.stderr}")
    return result


def run_script(mozc_src, *args):
    """統合スクリプトを動かす。コンソールの文字コードに関係なく日本語を読めるよう、子は UTF-8 にする。"""
    environment = dict(os.environ, PYTHONIOENCODING="utf-8", PYTHONUTF8="1")
    return subprocess.run(
        [sys.executable, str(SCRIPT), "--mozc-src", str(mozc_src), *args],
        capture_output=True, text=True, encoding="utf-8", errors="replace",
        env=environment, check=False,
    )


# ビルドスクリプトが dictionary_ut.txt を足したあとの BUILD.bazel (base_dictionary_data の部分)
BUILD_REGISTERED = '    srcs = [\n        "dictionary09.txt",\n        "dictionary_ut.txt",\n    ],\n'


class FakeMozc:
    """テスト用の Mozc。src/data/dictionary_oss に id.def・辞書・BUILD.bazel だけを置く。"""

    def __init__(self, root: Path, lines=(BASE_LINE,), general_id=ID_GENERAL):
        self.root = root
        self.src = root / "src"
        self.directory = self.src / "data" / "dictionary_oss"
        self.directory.mkdir(parents=True)
        (self.directory / "id.def").write_text(
            make_id_def(general_id=general_id), encoding="utf-8")
        (self.directory / "BUILD.bazel").write_text(BUILD_REGISTERED, encoding="utf-8")
        self.base = self.directory / "dictionary00.txt"
        self.output = self.directory / "dictionary_ut.txt"
        self.write_base("".join(lines))
        for index in range(1, 10):
            (self.directory / f"dictionary{index:02d}.txt").write_text(
                "", encoding="utf-8")
        git(self.root, "init", "-q", check=True)
        git(self.root, "add", "-A", check=True)
        git(self.root, "commit", "-q", "-m", "mozc", check=True)

    def write_base(self, text: str) -> None:
        self.base.write_text(text, encoding="utf-8")

    def base_sha(self) -> str:
        return hashlib.sha256(self.base.read_bytes()).hexdigest()

    def output_lines(self) -> list[str]:
        return self.output.read_text(encoding="utf-8").splitlines()


class MergeTestBase(unittest.TestCase):
    def setUp(self):
        self._temp = tempfile.TemporaryDirectory()
        self.addCleanup(self._temp.cleanup)
        self.temp = Path(self._temp.name)
        self.mozc = FakeMozc(self.temp / "mozc")

    def write(self, name: str, content: str, encoding="utf-8") -> Path:
        path = self.temp / name
        path.write_text(content, encoding=encoding)
        return path

    def run_script(self, *args: str) -> subprocess.CompletedProcess:
        return run_script(self.mozc.src, *args)

    def merge(self, dictionary: Path, *args: str) -> subprocess.CompletedProcess:
        return self.run_script("--dictionary", str(dictionary), *args)


class MergeTest(MergeTestBase):
    def test_adds_entries_that_are_not_in_mozc(self):
        result = self.merge(SAMPLE)
        self.assertEqual(result.returncode, 0, result.stderr)
        lines = self.mozc.output_lines()
        self.assertEqual(len(lines), 4)  # 重複を除いた 4 件
        self.assertIn("がんまかんすう\t1851\t1851\t7400\tガンマ関数", lines)

    def test_same_word_in_mozc_is_not_added_twice(self):
        # 「あるふぁべっと / アルファベット研究」を Mozc の辞書に置いておく
        self.mozc.write_base(BASE_LINE + "あるふぁべっと\t1851\t1851\t7000\tアルファベット研究\n")
        result = self.merge(SAMPLE)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(self.mozc.output_lines()), 3)

    def test_mozc_dictionary_is_not_modified(self):
        # issue #961: 手元で書き換えた dictionary00.txt を、統合で消さない。
        custom = BASE_LINE + "じかせい\t1851\t1851\t5000\tジカセイ\n"
        self.mozc.write_base(custom)
        before = self.mozc.base_sha()
        self.assertEqual(self.merge(SAMPLE).returncode, 0)
        self.assertEqual(self.mozc.base_sha(), before)

    def test_duplicate_in_ut_keeps_lowest_cost(self):
        result = self.merge(SAMPLE)
        self.assertEqual(result.returncode, 0, result.stderr)
        costs = [
            line.split("\t")[3] for line in self.mozc.output_lines()
            if line.endswith("\tアルファベット研究")
        ]
        self.assertEqual(costs, ["7300"])

    def test_output_is_sorted(self):
        result = self.merge(SAMPLE)
        self.assertEqual(result.returncode, 0, result.stderr)
        keys = [line.split("\t") for line in self.mozc.output_lines()]
        self.assertEqual(keys, sorted(keys))

    def test_merging_twice_gives_the_same_output(self):
        self.assertEqual(self.merge(SAMPLE).returncode, 0)
        first = self.mozc.output.read_bytes()
        self.assertEqual(self.merge(SAMPLE).returncode, 0)
        self.assertEqual(self.mozc.output.read_bytes(), first)

    def test_output_is_rewritten(self):
        # 前の出力の中身は、次の出力に残らない。
        self.mozc.output.write_text("ふるい\t1851\t1851\t5000\t古い\n", encoding="utf-8")
        self.assertEqual(self.merge(SAMPLE).returncode, 0)
        self.assertNotIn("ふるい\t1851\t1851\t5000\t古い", self.mozc.output_lines())

    def test_check_does_not_write(self):
        result = self.merge(SAMPLE, "--check")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(self.mozc.output.exists())
        self.assertIn("書いていません", result.stdout)

    def test_output_must_be_in_the_build(self):
        # BUILD.bazel に無いと、書いた語がビルドに入らない。黙って書かずに止める。
        (self.mozc.directory / "BUILD.bazel").write_text(
            '    srcs = [\n        "dictionary09.txt",\n    ],\n', encoding="utf-8")
        result = self.merge(SAMPLE)
        self.assertEqual(result.returncode, 1)
        self.assertIn("dictionary_ut.txt がありません", result.stderr)
        self.assertFalse(self.mozc.output.exists())

    def test_report_is_written(self):
        report_path = self.temp / "report.json"
        result = self.merge(SAMPLE, "--report", str(report_path))
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        self.assertEqual(report["schema"], 2)
        self.assertEqual(report["mode"], "merge")
        self.assertEqual(report["dictionary"]["sha256"],
                         hashlib.sha256(SAMPLE.read_bytes()).hexdigest())
        self.assertEqual(report["mozc"]["pos_id"], ID_GENERAL)
        self.assertTrue(report["mozc"]["commit"])  # git の commit を記録する
        self.assertEqual(report["result"]["lines_added"], 4)
        self.assertEqual(report["result"]["output_sha256"],
                         hashlib.sha256(self.mozc.output.read_bytes()).hexdigest())
        self.assertEqual(report["removed"]["duplicate_in_ut"], 1)


    def test_license_notice_is_recorded(self):
        report_path = self.temp / "report.json"
        result = self.merge(SAMPLE, "--report", str(report_path))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("同梱しません", result.stdout)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        self.assertFalse(report["license"]["bundled_with_meltype"])
        self.assertIn("alt-cannadic", report["license"]["notice"])
        # UT の辞書の出所ごとのライセンスを、レポートに残す
        self.assertEqual(set(report["license"]["sources"]),
                         {"place_names", "personal_names", "sudachidict", "jawiki",
                          "neologd", "edict2", "skk_jisyo", "alt_cannadic"})


    def test_non_utf8_console_does_not_crash(self):
        # Windows のコンソール (cp1252 など) でも、日本語を出そうとして止まらないこと。
        environment = dict(os.environ, PYTHONIOENCODING="cp1252", PYTHONUTF8="0")
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--mozc-src", str(self.mozc.src),
             "--dictionary", str(SAMPLE), "--check"],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
            env=environment, check=False,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        # cp1252 で表せない文字は \uXXXX の形になる
        self.assertIn("\\u", result.stdout)


class PosIdTest(MergeTestBase):
    def test_pos_id_of_another_mozc_version_is_remapped(self):
        # 辞書を作った Mozc では ID 1849 が「名詞,一般」だった (ビルドする Mozc では固有名詞)
        dictionary = self.write(
            "drifted.txt",
            "あるふぁべっと\t1849\t1849\t7600\tアルファベット研究\n",
        )
        result = self.merge(dictionary, "--report", str(self.temp / "report.json"))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(f"1849 (名詞,固有名詞,一般,*,*,*,*) → {ID_GENERAL}", result.stdout)
        self.assertIn(
            f"あるふぁべっと\t{ID_GENERAL}\t{ID_GENERAL}\t7600\tアルファベット研究",
            self.mozc.output_lines(),
        )
        report = json.loads((self.temp / "report.json").read_text(encoding="utf-8"))
        self.assertTrue(report["dictionary"]["pos_drift"])
        self.assertEqual(report["dictionary"]["pos_remap"],
                         {"1849": ID_GENERAL})

    def test_pos_id_not_in_id_def_is_remapped(self):
        dictionary = self.write(
            "unknown-id.txt",
            "あるふぁべっと\t60000\t60000\t7600\tアルファベット研究\n",
        )
        result = self.merge(dictionary)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(
            f"あるふぁべっと\t{ID_GENERAL}\t{ID_GENERAL}\t7600\tアルファベット研究",
            self.mozc.output_lines(),
        )

    def test_multiple_known_pos_ids_are_kept(self):
        dictionary = self.write(
            "mixed.txt",
            f"あるふぁべっと\t{ID_PROPER}\t{ID_PROPER}\t7600\tアルファベット研究\n"
            f"べーたせいり\t{ID_GENERAL}\t{ID_GENERAL}\t7800\tベータ整理\n",
        )
        result = self.merge(dictionary)
        self.assertEqual(result.returncode, 0, result.stderr)
        lines = self.mozc.output_lines()
        self.assertIn(f"あるふぁべっと\t{ID_PROPER}\t{ID_PROPER}\t7600\tアルファベット研究", lines)
        self.assertIn(f"べーたせいり\t{ID_GENERAL}\t{ID_GENERAL}\t7800\tベータ整理", lines)

    def test_keep_pos_ids_rejects_unknown_id(self):
        dictionary = self.write(
            "unknown-id.txt",
            "あるふぁべっと\t60000\t60000\t7600\tアルファベット研究\n",
        )
        result = self.merge(dictionary, "--keep-pos-ids")
        self.assertEqual(result.returncode, 2)
        self.assertIn("id.def にありません", result.stderr)

    def test_pos_option_selects_the_target_pos(self):
        dictionary = self.write(
            "drifted.txt",
            "あるふぁべっと\t1849\t1849\t7600\tアルファベット研究\n",
        )
        result = self.merge(dictionary, "--pos", POS_PROPER)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(
            f"あるふぁべっと\t{ID_PROPER}\t{ID_PROPER}\t7600\tアルファベット研究",
            self.mozc.output_lines(),
        )


class InvalidDictionaryTest(MergeTestBase):
    def assert_invalid(self, content: str, message: str) -> None:
        dictionary = self.write("invalid.txt", content)
        result = self.merge(dictionary)
        self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
        self.assertIn(message, result.stderr)
        self.assertFalse(self.mozc.output.exists())  # 途中まで書かない

    def test_wrong_column_count(self):
        self.assert_invalid("あるふぁべっと\t1851\t1851\t7600\n", "5 列ではありません")

    def test_too_many_columns(self):
        self.assert_invalid(
            "あるふぁべっと\t1851\t1851\t7600\tアルファベット研究\tSPELLING_CORRECTION\n",
            "5 列ではありません",
        )

    def test_empty_column(self):
        self.assert_invalid("あるふぁべっと\t1851\t1851\t\tアルファベット研究\n", "空の列")

    def test_cost_is_not_a_number(self):
        self.assert_invalid("あるふぁべっと\t1851\t1851\tやすい\tアルファベット研究\n", "整数ではありません")

    def test_cost_out_of_range(self):
        self.assert_invalid("あるふぁべっと\t1851\t1851\t70000\tアルファベット研究\n", "範囲外")

    def test_id_out_of_range(self):
        self.assert_invalid("あるふぁべっと\t70000\t70000\t7600\tアルファベット研究\n", "範囲外")

    def test_not_utf8(self):
        dictionary = self.temp / "sjis.txt"
        dictionary.write_bytes("あるふぁべっと\t1851\t1851\t7600\tアルファベット研究\n".encode("cp932"))
        result = self.merge(dictionary)
        self.assertEqual(result.returncode, 2)
        self.assertIn("UTF-8", result.stderr)
        self.assertFalse(self.mozc.output.exists())

    def test_bom_is_rejected(self):
        dictionary = self.temp / "bom.txt"
        dictionary.write_bytes(
            b"\xef\xbb\xbf" + "あるふぁべっと\t1851\t1851\t7600\tアルファベット研究\n".encode()
        )
        result = self.merge(dictionary)
        self.assertEqual(result.returncode, 2)
        self.assertIn("BOM", result.stderr)

    def test_nul_byte_is_rejected(self):
        dictionary = self.temp / "nul.txt"
        dictionary.write_bytes(b"\x00" + "あるふぁべっと\t1851\t1851\t7600\tx\n".encode())
        result = self.merge(dictionary)
        self.assertEqual(result.returncode, 2)
        self.assertIn("NUL", result.stderr)

    def test_empty_file_is_rejected(self):
        self.assert_invalid("", "空です")

    def test_missing_file_is_reported(self):
        result = self.merge(self.temp / "no-such-file.txt")
        self.assertEqual(result.returncode, 1)
        self.assertIn("見つかりません", result.stderr)


class ReadingTest(MergeTestBase):
    def test_non_hiragana_reading_is_a_warning(self):
        dictionary = self.write(
            "katakana.txt",
            "アルファベット\t1851\t1851\t7600\tアルファベット研究\n",
        )
        result = self.merge(dictionary, "--report", str(self.temp / "report.json"))
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads((self.temp / "report.json").read_text(encoding="utf-8"))
        self.assertEqual(len(report["warnings"]), 1)
        self.assertIn("ひらがなではありません", report["warnings"][0])

    def test_strict_readings_makes_it_an_error(self):
        dictionary = self.write(
            "katakana.txt",
            "アルファベット\t1851\t1851\t7600\tアルファベット研究\n",
        )
        result = self.merge(dictionary, "--strict-readings")
        self.assertEqual(result.returncode, 2)
        self.assertIn("ひらがなではありません", result.stderr)
        self.assertFalse(self.mozc.output.exists())


class EnvironmentTest(MergeTestBase):
    def test_mozc_src_must_point_at_src(self):
        result = run_script(self.temp, "--dictionary", str(SAMPLE))
        self.assertEqual(result.returncode, 1)
        self.assertIn("dictionary_oss", result.stderr)

    def test_dictionary_cannot_be_the_output(self):
        self.mozc.output.write_text(BASE_LINE, encoding="utf-8")
        result = self.merge(self.mozc.output)
        self.assertEqual(result.returncode, 1)
        self.assertIn("出力先", result.stderr)

    def test_dictionary_is_required(self):
        result = self.run_script()
        self.assertEqual(result.returncode, 1)
        self.assertIn("--dictionary", result.stderr)


class ModuleTest(unittest.TestCase):
    def test_hiragana_pattern(self):
        self.assertTrue(merge_ut.HIRAGANA.match("あるふぁべっと"))
        self.assertTrue(merge_ut.HIRAGANA.match("がっこうー"))
        self.assertTrue(merge_ut.HIRAGANA.match("ゝゞ"))
        self.assertFalse(merge_ut.HIRAGANA.match("アルファベット"))
        self.assertFalse(merge_ut.HIRAGANA.match("がっこう!"))
        self.assertFalse(merge_ut.HIRAGANA.match(""))

    def test_duplicates_are_counted(self):
        entries = [
            merge_ut.Entry("よみ", 1, 1, 5000, "語", 1),
            merge_ut.Entry("よみ", 1, 1, 6000, "語", 2),
            merge_ut.Entry("よみ", 1, 1, 7000, "語", 3),
        ]
        kept, in_ut, in_mozc = merge_ut.remove_duplicates(entries, set())
        self.assertEqual(len(kept), 1)
        self.assertEqual(kept[0].cost, 5000)
        self.assertEqual(in_ut, 2)
        self.assertEqual(in_mozc, 0)


if __name__ == "__main__":
    unittest.main()
