# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""Regression checks for readable axes and complete off-scale reporting."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("plotter", Path(__file__).with_name("plot-windows-benchmark.py"))
plotter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(plotter)


class AxisTests(unittest.TestCase):
    def draw(self, groups, **kwargs):
        fig, ax = plotter.plt.subplots()
        self.addCleanup(plotter.plt.close, fig)
        stats = plotter.boxes(ax, groups, "ms", "Test", **kwargs)
        return ax, stats

    def test_remote_outliers_do_not_flatten_boxes_or_change_statistics(self):
        groups = {"csharp": [10, 11, 12, 13, 14] * 4 + [10000, 10000],
                  "rust": [8, 9, 10, 11, 12] * 4 + [.001]}
        ax, stats = self.draw(groups)
        self.assertEqual(ax.get_yscale(), "linear")
        self.assertLess(ax.get_ylim()[1], 30)
        self.assertGreater(ax.get_ylim()[0], 1)
        for version, values in groups.items():
            for key, value in plotter.distribution(values).items():
                self.assertEqual(stats[version][key], value)
        self.assertEqual(stats["csharp"]["off_scale"], [10000, 10000])
        self.assertEqual(stats["rust"]["off_scale"], [.001])
        table = "\n".join(plotter.off_scale_table({"speed": {"test": stats}, "resources": {}, "resource_units": {}}))
        self.assertIn("10000 × 2", table)
        self.assertIn("0.001 × 1", table)
        self.assertTrue(any("2 off-scale" in text.get_text() for text in ax.texts))

    def test_widely_different_versions_share_a_wave_break(self):
        ax, stats = self.draw({"csharp": [100, 110, 120], "rust": [.1, .11, .12]})
        self.assertEqual(ax.get_yscale(), "function")
        a, b = stats["rust"]["axis_break"]
        self.assertTrue(.12 < a < b < 100)
        self.assertTrue(any(line.get_gid() == "axis-break-wave" for line in ax.lines))
        ax.figure.canvas.draw()
        self.assertEqual(stats["csharp"]["axis_limits"], stats["rust"]["axis_limits"])
        self.assertFalse(stats["csharp"]["off_scale"])
        self.assertFalse(stats["rust"]["off_scale"])

    def test_constant_and_zero_samples_have_finite_nonempty_limits(self):
        for value in [0, 1e-6, 100]:
            with self.subTest(value=value):
                ax, stats = self.draw({"csharp": [value] * 10, "rust": [value] * 10})
                low, high = ax.get_ylim()
                self.assertTrue(plotter.np.isfinite([low, high]).all())
                self.assertLess(low, high)
                self.assertLessEqual(low, value)
                self.assertGreaterEqual(high, value)
                self.assertEqual(stats["csharp"]["off_scale"], [])

    def test_close_medians_are_separated_and_labelled(self):
        ax, _ = self.draw({"csharp": [100] * 10, "rust": [100.00001] * 10})
        lo, hi = ax.get_ylim()
        self.assertLess(hi - lo, 0.00002)
        self.assertIn("median 100.00001", ax.texts[1].get_text())

    def test_zoom_view_distinguishes_medians_inside_overlapping_distributions(self):
        ax, stats = self.draw({"csharp": [90, 100, 110], "rust": [90, 100.00001, 110]})
        self.assertGreater(ax.get_ylim()[1] - ax.get_ylim()[0], 20)
        plotter.zoom_medians(ax, stats)
        self.assertLess(ax.get_ylim()[1] - ax.get_ylim()[0], .00002)
        self.assertIn("median detail", ax.get_ylabel())

    def test_moderate_outlier_is_still_drawn(self):
        _, stats = self.draw({"csharp": [10, 11, 12, 13, 14] * 4 + [18], "rust": [10] * 20})
        self.assertIn(18, stats["csharp"]["fliers"])
        self.assertNotIn(18, stats["csharp"]["off_scale"])

    def test_zero_samples_disable_auto_log_and_explicit_log_rejects_them(self):
        groups = {"csharp": [0, 1, 2], "rust": [100, 110, 120]}
        ax, _ = self.draw(groups)
        self.assertEqual(ax.get_yscale(), "function")
        with self.assertRaises(ValueError):
            self.draw(groups, log=True)

    def test_rust_labels_show_actual_release_and_preview_numbers(self):
        for value, expected in [("flash-v0.1.0-preview.3", "Rust v0.1.0 pre3"),
                                ("0.1.0-pre.4", "Rust v0.1.0 pre4"),
                                ("flash-v0.1.0", "Rust v0.1.0"),
                                ("0.1.1-pre.1", "Rust v0.1.1 pre1")]:
            self.assertEqual(plotter.rust_label(value), expected)

    def test_invalid_measurements_are_rejected(self):
        for values in [[], [float("nan")], [float("inf")]]:
            with self.subTest(values=values), self.assertRaises(ValueError):
                plotter.distribution(values)

    def test_no_off_scale_values_are_reported_explicitly(self):
        _, stats = self.draw({"csharp": [10, 11, 12], "rust": [9, 10, 11]})
        table = "\n".join(plotter.off_scale_table({"speed": {"test": stats}, "resources": {}, "resource_units": {}}))
        self.assertIn("表示範囲外の測定値はありません", table)

    def test_complete_report_keeps_all_measurements_and_release_body_table(self):
        source = Path(__file__).parent / "performance/windows/results/20261021-boxplots/summary.json"
        report = json.loads(source.read_text(encoding="utf-8"))
        target = report["rows"][0]
        target["total_ms"] = 1e8
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            input_file = root / "report.json"
            input_file.write_text(json.dumps(report), encoding="utf-8")
            subprocess.run([sys.executable, str(Path(plotter.__file__)), str(input_file),
                            "--output", str(root / "charts"), "--github-summary", str(root / "step.md")],
                           check=True, capture_output=True, text=True, encoding="utf-8")
            saved = json.loads((root / "charts/summary.json").read_text(encoding="utf-8"))
            self.assertEqual(saved["rows"], report["rows"])
            stats = json.loads((root / "charts/boxplot-statistics.json").read_text(encoding="utf-8"))
            self.assertIn(1e8, stats["speed"][target["case"]][target["version"]]["off_scale"])
            notes = (root / "charts/README.md").read_text(encoding="utf-8")
            self.assertLess(notes.index("## 表示範囲外の測定値"), notes.index("## 全画像"))
            self.assertIn("100000000 × 1", notes)
            self.assertIn("表示範囲外の測定値", (root / "step.md").read_text(encoding="utf-8"))
            for name in plotter.IMAGES:
                self.assertTrue((root / "charts" / name).read_bytes().startswith(b"\x89PNG\r\n\x1a\n"))
                svg = Path(name).with_suffix(".svg")
                self.assertIn(f"]({svg})", notes)
                self.assertIn("<svg", (root / "charts" / svg).read_text(encoding="utf-8"))
            # CI releases compare two Rust versions rather than C# and Rust.
            report.update(comparison_kind="rust-versions", old_ref="flash-v0.1.0-preview.3",
                          new_ref="b" * 40, current_version="0.1.0-pre.4", samples=report["samples_per_round"],
                          warmups=report["warmups_per_case"])
            rename = {"csharp": "old", "rust": "new"}
            report["rows"] = [dict(row, version=rename[row["version"]]) for row in report["rows"]]
            report["process_resources"] = [dict(row, version=rename[row["version"]])
                                           for row in report["process_resources"]]
            report["results"] = [dict(case=row["case"], keys=row["keys"],
                medians_ms={rename[key]: value for key, value in row["median_total_ms"].items()},
                new_over_old=row["rust_over_csharp"], matching_projection=row["trace_matches"],
                eligible_for_speed_claim=row["eligible_for_speed_claim"]) for row in report["comparisons"]]
            input_file.write_text(json.dumps(report), encoding="utf-8")
            subprocess.run([sys.executable, str(Path(plotter.__file__)), str(input_file),
                            "--output", str(root / "charts-rust")],
                           check=True, capture_output=True, text=True, encoding="utf-8")
            notes = (root / "charts-rust/README.md").read_text(encoding="utf-8")
            self.assertIn("100000000 × 1", notes.split("## 全画像")[0])
            self.assertIn("Rust v0.1.0 pre3", notes)
            saved = json.loads((root / "charts-rust/summary.json").read_text(encoding="utf-8"))
            self.assertEqual(saved["rows"], report["rows"])
            # Three versions share the same panel; all resource counts stay per version.
            report.update(versions=["csharp", "old", "new"], csharp_ref="v1.3.0", csharp_runtime="NativeAOT",
                          rounds=len(report["order"]) // 2, current_source="tag/commit")
            report["order"] += ["csharp"] * report["rounds"]
            for collection in ("rows", "process_resources"):
                report[collection] += [dict(row, version="csharp") for row in report[collection] if row["version"] == "old"]
            for row in report["results"]:
                row["medians_ms"]["csharp"] = row["medians_ms"]["old"]
                row.update(new_over_csharp=row["new_over_old"], csharp_matching_projection=False,
                           eligible_for_csharp_speed_claim=False)
            input_file.write_text(json.dumps(report), encoding="utf-8")
            subprocess.run([sys.executable, str(Path(plotter.__file__)), str(input_file),
                            "--output", str(root / "charts-three")],
                           check=True, capture_output=True, text=True, encoding="utf-8")
            notes = (root / "charts-three/README.md").read_text(encoding="utf-8")
            self.assertIn("C# v1.3.0 (ms)|Rust v0.1.0 pre3 (ms)|Rust v0.1.0 pre4 (ms)", notes)
            self.assertIn("現在/C#|C#状態一致", notes)
            self.assertIn("--current-ref " + report["new_ref"], notes)
            self.assertIn("状態一致: 0/21", notes)
            stats = json.loads((root / "charts-three/boxplot-statistics.json").read_text(encoding="utf-8"))
            self.assertEqual(set(stats["speed"][target["case"]]), {"csharp", "old", "new"})
            self.assertEqual(len(list((root / "charts-three").glob("*.png"))), 10)
            self.assertEqual(set(stats["rust_only"]["speed"][target["case"]]), {"old", "new"})
            for version in ("old", "new"):
                full = stats["speed"][target["case"]][version]
                rust = stats["rust_only"]["speed"][target["case"]][version]
                for key in ("med", "q1", "q3", "fliers", "n"):
                    self.assertEqual(full[key], rust[key])
            self.assertIn("Rust前版・現在版（2者比較）", notes)
            self.assertIn("C#・Rust前版・現在版（3者比較）", notes)
            for name in plotter.IMAGES + plotter.RUST_IMAGES:
                self.assertIn(f"]({name})", notes)
                self.assertTrue((root / "charts-three" / name).read_bytes().startswith(b"\x89PNG\r\n\x1a\n"))

            # POSIX reports leave unsupported memory counters null, never zero.
            report.update(host_os="Darwin", backend="native")
            report["old_version"] = report["current_version"]
            for row in report["process_resources"]:
                row["sampled_mean_working_set_bytes"] = None
                row["sampled_peak_private_bytes"] = None
            input_file.write_text(json.dumps(report), encoding="utf-8")
            subprocess.run([sys.executable, str(Path(plotter.__file__)), str(input_file),
                            "--output", str(root / "charts-posix")],
                           check=True, capture_output=True, text=True, encoding="utf-8")
            notes = (root / "charts-posix/README.md").read_text(encoding="utf-8")
            self.assertIn("Darwin / native", notes)
            self.assertIn("Rust v0.1.0 pre4 (before)", notes)
            self.assertIn("Rust v0.1.0 pre4 (after)", notes)
            self.assertIn("|未測定|未測定|未測定|", notes)
            saved = json.loads((root / "charts-posix/summary.json").read_text(encoding="utf-8"))
            self.assertIsNone(saved["process_resources"][0]["sampled_peak_private_bytes"])



if __name__ == "__main__":
    unittest.main()
