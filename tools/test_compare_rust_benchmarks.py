# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("comparison", Path(__file__).with_name("compare-rust-benchmarks.py"))
comparison = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comparison)


class ComparisonTests(unittest.TestCase):
    def test_verify_reads_rust_and_upstream_csharp_edit_protocols(self):
        benchmark = comparison.benchmark
        keys = [[0, ord("あ"), 0], [0, 0, 0]]
        typed = json.dumps({"consumed": False, "commits": [], "view": None})
        rust = json.dumps({"consumed": True, "commits": [
            {"text": "亜", "deleteBefore": 1, "expect": "あ"}], "view": None})
        csharp = json.dumps({"consumed": True, "commits": [
            {"text": "亜", "removeBefore": "あ", "currentWord": None,
             "moveCaretBack": 0, "insertByPaste": False}], "view": None})
        rust_document, rust_trace = benchmark.verify([typed, rust], keys)
        csharp_document, csharp_trace = benchmark.verify([typed, csharp], keys)
        self.assertEqual(rust_document, "亜")
        self.assertEqual(csharp_document, "亜")
        # 同じ意味の書き換えは、どちらの JSON でも同じ記録になる (三者比較を比べられる)
        self.assertEqual(rust_trace, csharp_trace)
        self.assertEqual(rust_trace[1]["commits"], [dict(deleteBefore=1, text="亜", expect="あ")])

    def test_version_order_includes_previews_and_stable(self):
        versions = ["flash-v0.1.0-preview.2", "flash-v0.1.0-preview.10", "flash-v0.1.0", "flash-v0.1.1-preview.1"]
        self.assertEqual(sorted(reversed(versions), key=comparison.version_key), versions)
        self.assertIsNone(comparison.version_key("random"))

    def test_previous_release_excludes_same_newer_and_csharp_tags(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "Cargo.toml").write_text('[workspace.package]\nversion="0.1.0-pre.4"', encoding="utf-8")
            tags = "flash-v0.1.0-preview.2\nflash-v0.1.0-preview.3\nflash-v0.1.0-preview.4\nflash-v0.1.0\nv1.3.0"
            with patch.object(comparison, "ROOT", root), patch.object(comparison.benchmark, "command", return_value=tags):
                self.assertEqual(comparison.previous_release(), "flash-v0.1.0-preview.3")
            with patch.object(comparison.benchmark, "command", side_effect=[
                '[workspace.package]\nversion="0.1.0"', tags]):
                self.assertEqual(comparison.previous_release("flash-v0.1.0"), "flash-v0.1.0-preview.4")
            with patch.object(comparison, "ROOT", root), patch.object(comparison.benchmark, "command", return_value="v1.3.0"):
                with self.assertRaises(ValueError):
                    comparison.previous_release()

    def test_stable_rust_predecessor_includes_preview_of_same_version(self):
        with patch.object(comparison.benchmark, "command", side_effect=[
            '[workspace.package]\nversion="1.2.0"',
            "flash-v1.1.0\nflash-v1.2.0-preview.2\nflash-v1.2.0-preview.10\nflash-v1.2.0\nflash-v1.3.0"]):
            self.assertEqual(comparison.previous_release("flash-v1.2.0"), "flash-v1.2.0-preview.10")

    def test_next_minor_preview_uses_previous_stable_release(self):
        with patch.object(comparison.benchmark, "command", side_effect=[
            '[workspace.package]\nversion="0.1.1-pre.1"',
            "flash-v0.1.0-preview.3\nflash-v0.1.0-preview.4\nflash-v0.1.0\nflash-v0.1.1-preview.1"]):
            self.assertEqual(comparison.previous_release("flash-v0.1.1-preview.1"), "flash-v0.1.0")

    def test_first_stable_flash_uses_pre7_as_automatic_baseline(self):
        tags = "flash-v0.1.0-preview.6\nflash-v0.1.0-preview.7\nflash-v1.0.0\nflash-v1.0.1-preview.1\nv1.4.0"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "Cargo.toml").write_text('[workspace.package]\nversion="1.0.0"', encoding="utf-8")
            with patch.object(comparison, "ROOT", root), patch.object(comparison.benchmark, "command", return_value=tags):
                self.assertEqual(comparison.previous_release(), "flash-v0.1.0-preview.7")
        with patch.object(comparison.benchmark, "command", side_effect=['[workspace.package]\nversion="1.0.0"', tags]):
            self.assertEqual(comparison.previous_release("flash-v1.0.0"), "flash-v0.1.0-preview.7")

    def test_csharp_auto_uses_latest_stable_not_rust_or_preview(self):
        with patch.object(comparison.benchmark, "command", return_value=
            "v1.9.0\nv1.10.0\nv2.0.0-pre.1\nflash-v9.0.0\nrust-v8.0.0"):
            self.assertEqual(comparison.csharp_release(), "v1.10.0")

    def test_every_version_occupies_every_run_position(self):
        versions = ("csharp", "old", "new")
        orders = [comparison.measurement_order(versions, i) for i in range(3)]
        for position in range(3):
            self.assertEqual({row[position] for row in orders}, set(versions))

    def test_build_products_do_not_change_source_hashes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "src/Meltype.Core").mkdir(parents=True)
            (root / "src/Meltype.Core/input.cs").write_text("source", encoding="utf-8")
            before = comparison.source_hashes(root)
            (root / "src/Meltype.Core/obj").mkdir()
            (root / "src/Meltype.Core/obj/generated.cs").write_text("generated", encoding="utf-8")
            self.assertEqual(before, comparison.source_hashes(root))
            (root / "src/Meltype.Core/input.cs").write_text("changed", encoding="utf-8")
            self.assertNotEqual(before, comparison.source_hashes(root))

    def test_three_way_runner_keeps_pairwise_mismatches_and_plots(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            binaries = {}
            for label in ("old", "new", "csharp"):
                binaries[label] = root / (label + ".exe")
                binaries[label].write_bytes(label.encode())
            case = {"name": "short_test", "keys": [[13, 0, 0]]}
            orders = []
            def measure(program, *, cwd, env, stdout):
                label = Path(program[0]).stem
                orders.append(label)
                response = json.dumps({"consumed": True, "commits": [], "view": None})
                # Each version is internally deterministic, but C# differs.
                if label == "csharp":
                    response = json.dumps({"consumed": False, "commits": [], "view": None})
                for sample in range(3):
                    stdout.write(json.dumps({"case": "short_test", "sample": sample,
                        "total_ms": {"csharp": 3, "old": 2, "new": 1}[label], "responses": [response]}) + "\n")
                return {"cpu_seconds": 1}
            output = root / "result"
            args = ["compare-rust-benchmarks.py", "--old", str(binaries["old"]), "--new", str(binaries["new"]),
                    "--old-ref", "old-tag", "--new-ref", "new-tag", "--csharp", str(binaries["csharp"]),
                    "--csharp-ref", "v1.3.0", "--samples", "3", "--output", str(output), "--plot"]
            with patch.object(sys, "argv", args), patch.object(comparison.benchmark, "fixtures", return_value=[case]), \
                 patch.object(comparison.benchmark, "measure_process", side_effect=measure), \
                 patch.object(comparison.benchmark, "command", return_value="rustc test"), \
                 patch.object(comparison.importlib.util, "find_spec", return_value=object()), \
                 patch.object(comparison.platform, "system", return_value="Windows"), \
                 patch.object(comparison.platform, "platform", return_value="Windows-test"), \
                 patch.object(comparison.platform, "processor", return_value="test CPU"), \
                 patch.object(comparison.subprocess, "run") as runner, patch("builtins.print"):
                comparison.main()
            report = json.loads((output / "report.json").read_text(encoding="utf-8"))
            self.assertEqual(report["versions"], ["csharp", "old", "new"])
            self.assertEqual(len(report["rows"]), 27)
            self.assertEqual(report["rounds"], 3)
            self.assertEqual(orders, ["csharp", "old", "new", "old", "new", "csharp", "new", "csharp", "old"])
            result = report["results"][0]
            self.assertTrue(result["eligible_for_speed_claim"])
            self.assertFalse(result["eligible_for_csharp_speed_claim"])
            self.assertEqual(result["new_over_old"], .5)
            self.assertEqual(result["new_over_csharp"], 1/3)
            self.assertIn("plot-windows-benchmark.py", runner.call_args.args[0][1])


if __name__ == "__main__":
    unittest.main()
