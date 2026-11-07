#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 KoheiTakada
"""Check that grouped instrumentation reports cannot pass with missing cases."""
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


REPORTER = Path(__file__).with_name("instrumentation-report.py")


def run_output(classname, method, *, passed=True):
    status = 0 if passed else -2
    summary = "OK (1 test)" if passed else "FAILURES!!!\nTests run: 1, Failures: 1"
    return (
        f"INSTRUMENTATION_STATUS: class={classname}\n"
        f"INSTRUMENTATION_STATUS: test={method}\n"
        "INSTRUMENTATION_STATUS_CODE: 1\n"
        f"INSTRUMENTATION_STATUS: class={classname}\n"
        f"INSTRUMENTATION_STATUS: test={method}\n"
        f"INSTRUMENTATION_STATUS_CODE: {status}\n"
        f"INSTRUMENTATION_RESULT: stream=\n\n{summary}\n\n"
        "INSTRUMENTATION_CODE: -1\n"
    )


class GroupedReportTest(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.sources = []
        for name in ("First", "Second"):
            path = self.root / f"{name}Test.kt"
            path.write_text(f"package example\nclass {name}Test {{ @Test fun works() {{}} }}\n")
            self.sources.append(path)
        (self.root / "device.txt").write_text("api=35\n")

    def report(self, raw, *, runs=2, sources=None):
        log = self.root / "instrumentation.log"
        log.write_text(raw)
        command = [
            sys.executable, str(REPORTER), "--log", str(log),
            "--device", str(self.root / "device.txt"),
            "--sources", *(str(p) for p in (sources or self.sources)),
            "--output", str(self.root / "report"),
        ]
        if runs is not None:
            command.extend(["--expected-runs", str(runs)])
        return subprocess.run(command, capture_output=True, text=True)

    def test_two_complete_runs_are_combined(self):
        raw = run_output("example.FirstTest", "works") + run_output("example.SecondTest", "works")
        result = self.report(raw)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        suite = ET.parse(self.root / "report" / "instrumentation.xml").getroot()
        self.assertEqual("2", suite.get("tests"))
        self.assertEqual("0", suite.get("failures"))

    def test_missing_run_does_not_pass(self):
        result = self.report(run_output("example.FirstTest", "works"))
        self.assertNotEqual(0, result.returncode)

    def test_failed_or_duplicate_case_does_not_pass(self):
        first = run_output("example.FirstTest", "works")
        for second in (run_output("example.SecondTest", "works", passed=False), first):
            with self.subTest(second=second):
                self.assertNotEqual(0, self.report(first + second).returncode)

    def test_single_run_remains_supported(self):
        result = self.report(run_output("example.FirstTest", "works"), runs=None, sources=self.sources[:1])
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
