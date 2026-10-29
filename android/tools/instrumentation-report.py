#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 KoheiTakada
"""Generate case-by-case JUnit XML / Markdown from an actual Android instrumentation run.

No external Python packages. Explicitly fails on skipped, failed, missing,
unexpected or duplicated test names and on incomplete runner output.
"""
import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def expected_cases(paths):
    result = []
    for path in paths:
        source = path.read_text(encoding="utf-8")
        pkg = re.search(r"(?m)^package\s+([\w.]+)", source)
        cls = re.search(r"\bclass\s+(\w+Test)\b", source)
        if pkg is None or cls is None:
            raise ValueError("Unrecognized Kotlin test class: " + str(path))
        classname = pkg.group(1) + "." + cls.group(1)
        for method in re.findall(r"@Test\s+fun\s+(\w+)\s*\(", source):
            result.append((classname, method))
    if not result or len(result) != len(set(result)):
        raise ValueError("No tests or duplicate tests found")
    return result


def parse_log(data):
    bundle = {}
    results = {}
    duplicates = []
    last_key = None
    for line in data.splitlines():
        line = line.strip("\r")
        if line.startswith("INSTRUMENTATION_STATUS: "):
            key, separator, val = line[len("INSTRUMENTATION_STATUS: "):].partition("=")
            if separator:
                bundle[key] = val
                last_key = key
        elif line.startswith("INSTRUMENTATION_STATUS_CODE: "):
            try:
                code = int(line[len("INSTRUMENTATION_STATUS_CODE: "):].strip())
            except ValueError:
                bundle = {}
                continue
            case = (bundle.get("class"), bundle.get("test"))
            if all(case) and code != 1:
                if case in results:
                    duplicates.append(case)
                results[case] = (code, bundle.get("stack", bundle.get("stream", "")))
            bundle = {}
            last_key = None
        elif line.startswith(" ") and last_key in ("stack", "stream"):
            bundle[last_key] = bundle.get(last_key, "") + "\n" + line
    return results, duplicates


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--log", required=True, type=Path)
    ap.add_argument("--device", required=True, type=Path)
    ap.add_argument("--sources", required=True, nargs="+", type=Path)
    ap.add_argument("--output", required=True, type=Path)
    a = ap.parse_args()
    a.output.mkdir(parents=True, exist_ok=True)
    expected = expected_cases(a.sources)
    raw = a.log.read_text(encoding="utf-8", errors="replace")
    cases, duplicates = parse_log(raw)
    missing = set(expected) - set(cases)
    unexpected = set(cases) - set(expected)
    passes = sum(cases.get(c, (None,))[0] == 0 for c in expected)
    # InstrumentationStatus 0 means pass, but only when runner also confirms OK.
    match = re.search(r"(?m)^OK \((\d+) tests?\)\s*$", raw)
    runner_ok = bool(match and int(match.group(1)) == len(expected)
                     and "FAILURES!!!" not in raw)
    successful = (passes == len(expected) and runner_ok and
                  not unexpected and not duplicates)
    suite = ET.Element("testsuite", name="MeltypeAndroidInstrumentation",
                       tests=str(len(expected)),
                       failures=str(len(expected)-passes+len(unexpected)+len(duplicates)
                                    + (0 if runner_ok else 1)))
    entries = ["# Android 15 instrumentation — case results", "",
               "| Test | Result |", "| --- | --- |"]
    for classname, method in expected:
        code, detail = cases.get((classname, method), (None, "Test was not executed"))
        elt = ET.SubElement(suite, "testcase", classname=classname, name=method)
        status = "PASS" if code == 0 else "MISSING" if code is None else "FAIL (code " + str(code) + ")"
        if code != 0:
            ET.SubElement(elt, "failure", message=status).text = detail
        entries.append("| " + classname.rsplit(".", 1)[-1] + "." + method + " | **" + status + "** |")
    for c in sorted(unexpected):
        entries.append("| " + ".".join(c) + " | **UNEXPECTED** |")
    details = ["", "Expected: " + str(len(expected)),
               "Passed: " + str(passes),
               "Missing: " + str(len(missing)),
               "Unexpected: " + str(len(unexpected)),
               "Duplicate: " + str(len(duplicates)),
               "Runner OK: " + str(runner_ok), "",
               "## Device", "", "    " +
               a.device.read_text(encoding="utf-8", errors="replace").replace("\n", "\n    ")]
    ET.ElementTree(suite).write(a.output / "instrumentation.xml",
                                encoding="utf-8", xml_declaration=True)
    (a.output / "test-cases.md").write_text("\n".join(entries+details)+"\n", encoding="utf-8")
    summary = ["# Meltype Android QA: " + ("PASS" if successful else "FAIL"),
               "", "Expected: " + str(len(expected)), "Passed: " + str(passes),
               "Runner OK: " + str(runner_ok), "",
               "The full reproducible cases and expectations are in android/docs/QA-TESTING.md.",
               "See test-cases.md, instrumentation.xml and instrumentation.log in the Artifact.",
               "", *entries]
    (a.output / "summary.md").write_text("\n".join(summary)+"\n", encoding="utf-8")
    print("\n".join(summary[:8]))
    return 0 if successful else 1


if __name__ == "__main__":
    sys.exit(main())
