#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 0924haruto12
"""Test packaged Meltype through a private IBus daemon and synthetic input context.

Run inside an existing isolated Xvfb and D-Bus session:
    xvfb-run -a dbus-run-session -- python3 linux/tests/test_daemon.py PACKAGE

Requires python3-gi, gir1.2-ibus-1.0 and ibus-daemon. The packaged engine,
NativeAOT library and Mozc helper are real. Desktop application GUI and normal
desktop IME registration are outside this test. Settings and learning are temporary.
"""

import argparse
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path


class SyntheticClient:
    """Capture IBus client signals without treating preedit as committed text."""

    def __init__(self):
        self.clear()

    def clear(self):
        self.document = ""
        self.preedit = ""
        self.candidates = []
        self.commits = []
        self.errors = []

    def committed(self, context, text):
        value = text.get_text()
        self.document += value
        self.commits.append(value)
        # 実際のクライアント (GTK / Qt / Chromium) は commit-text で変換中の表示を消す
        self.preedit = ""

    def preedit_updated(self, context, text, cursor, visible, *mode):
        self.preedit = text.get_text() if visible else ""

    def preedit_hidden(self, context):
        self.preedit = ""

    def lookup_updated(self, context, table, visible):
        self.candidates = [table.get_candidate(i).get_text()
                           for i in range(table.get_number_of_candidates())] if visible else []

    def lookup_hidden(self, context):
        self.candidates = []

    def surrounding_deleted(self, context, offset, count):
        start = len(self.document) + offset
        if not 0 <= start <= start + count <= len(self.document):
            # Exceptions in GI signal callbacks do not propagate to unittest.
            self.errors.append("IBus requested deletion outside the synthetic document")
            return
        self.document = self.document[:start] + self.document[start + count:]

    def describe(self):
        return json.dumps({"document": self.document, "preedit": self.preedit,
                           "candidates": self.candidates, "commits": self.commits}, ensure_ascii=True)


class PrivateDaemonTests(unittest.TestCase):
    package = None
    saved_logs = []

    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="meltype-private-daemon-")
        cls.root = Path(cls.temporary.name)
        cls.processes = []
        cls.log_files = []
        cls.previous_environment = {}
        cls.addClassCleanup(cls.cleanup_private_processes)

        for variable, leaf in (("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache"),
                               ("XDG_CONFIG_HOME", "config"), ("XDG_RUNTIME_DIR", "runtime"),
                               ("MELTYPE_DATA_DIR", "settings")):
            directory = cls.root / leaf
            directory.mkdir(mode=0o700)
            cls.set_environment(variable, str(directory))
        cls.socket = cls.root / "ibus.socket"
        cls.set_environment("IBUS_ADDRESS", "unix:path=" + str(cls.socket))
        (cls.root / "settings/config.json").write_text(json.dumps({
            "LiveConversion": False, "SpaceAroundEnglish": True, "SigilWordsDirect": False}), encoding="utf-8")

        # Set the private addresses before GI or the native library is initialized.
        import gi
        gi.require_version("IBus", "1.0")
        from gi.repository import GLib, IBus
        cls.GLib, cls.IBus = GLib, IBus
        IBus.init()
        cls.daemon = cls.start_process("IBus daemon", [
            "ibus-daemon", "-s", "-p", "disable", "-c", "disable", "-E", "disable",
            "-a", os.environ["IBUS_ADDRESS"], "--cache=none", "--timeout=3000"])
        cls.wait_for(lambda: cls.socket.exists(), "private IBus socket")
        cls.bus = IBus.Bus()
        cls.wait_for(cls.bus.is_connected, "private IBus connection")
        cls.product = cls.start_process("packaged Meltype", [
            sys.executable, str(cls.package / "ibus-engine-meltype")])

        cls.context = cls.bus.create_input_context("meltype-private-synthetic-client")
        if cls.context is None:
            raise RuntimeError("IBus did not create the synthetic input context")
        cls.context.set_capabilities(
            IBus.Capabilite.PREEDIT_TEXT | IBus.Capabilite.AUXILIARY_TEXT |
            IBus.Capabilite.LOOKUP_TABLE | IBus.Capabilite.FOCUS | IBus.Capabilite.SURROUNDING_TEXT)
        cls.client = SyntheticClient()
        for name, callback in (("commit-text", cls.client.committed),
                               ("update-preedit-text", cls.client.preedit_updated),
                               ("hide-preedit-text", cls.client.preedit_hidden),
                               ("update-lookup-table", cls.client.lookup_updated),
                               ("hide-lookup-table", cls.client.lookup_hidden),
                               ("delete-surrounding-text", cls.client.surrounding_deleted)):
            cls.context.connect(name, callback)

        # A non-focused context cannot select this dynamically registered engine.
        cls.context.focus_in()
        cls.pump()

        def select_engine():
            cls.context.set_engine("meltype")
            cls.pump(0.1)
            description = cls.context.get_engine()
            return description is not None and description.get_name() == "meltype"

        cls.wait_for(select_engine, "packaged engine factory registration")
        cls.context.focus_in()
        cls.pump()

    @classmethod
    def set_environment(cls, name, value):
        cls.previous_environment[name] = os.environ.get(name)
        os.environ[name] = value

    @classmethod
    def start_process(cls, label, command):
        path = cls.root / ("daemon.log" if label == "IBus daemon" else "product.log")
        output = path.open("wb")
        cls.log_files.append((label, path, output))
        process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        cls.processes.append((label, process))
        return process

    @classmethod
    def check_processes(cls):
        for label, process in cls.processes:
            if process.poll() is not None:
                raise RuntimeError(f"{label} exited with status {process.returncode}")
        client = getattr(cls, "client", None)
        if client is not None and client.errors:
            raise RuntimeError("; ".join(client.errors))

    @classmethod
    def pump(cls, seconds=0.05):
        deadline = time.monotonic() + seconds
        main_context = cls.GLib.MainContext.default()
        while time.monotonic() < deadline:
            cls.check_processes()
            while main_context.pending():
                main_context.iteration(False)
            time.sleep(0.002)

    @classmethod
    def wait_for(cls, predicate, description, timeout=10):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            cls.check_processes()
            if predicate():
                return
            cls.pump(0.05)
        raise TimeoutError("Timed out waiting for " + description)

    @classmethod
    def cleanup_private_processes(cls):
        # Only processes/groups created by this test are stopped; no global ibus exit.
        try:
            for label, process in reversed(cls.processes):
                try:
                    os.killpg(process.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    try:
                        os.killpg(process.pid, signal.SIGKILL)
                    except ProcessLookupError:
                        pass
                    process.wait(timeout=5)
        finally:
            cls.saved_logs = []
            for label, path, output in cls.log_files:
                output.close()
                cls.saved_logs.append((label, path.read_text(encoding="utf-8", errors="replace")))
            for name, previous in cls.previous_environment.items():
                if previous is None:
                    os.environ.pop(name, None)
                else:
                    os.environ[name] = previous
            cls.temporary.cleanup()

    def setUp(self):
        self.reset_client()

    def reset_client(self):
        self.context.focus_in()
        self.context.reset()
        self.pump()
        self.client.clear()
        self.context.set_surrounding_text(self.IBus.Text.new_from_string(""), 0, 0)
        self.pump()

    def key(self, keyval, scalar=None, state=0):
        consumed = self.context.process_key_event(keyval, 0, state)
        self.pump()
        modifiers = self.IBus.ModifierType
        shortcut_or_release = state & (modifiers.CONTROL_MASK | modifiers.MOD1_MASK |
                                      modifiers.SUPER_MASK | modifiers.MOD4_MASK | modifiers.RELEASE_MASK)
        if not consumed and scalar is not None and not shortcut_or_release:
            # Model the application receiving the original event exactly once.
            self.client.document += scalar
        self.context.set_surrounding_text(self.IBus.Text.new_from_string(self.client.document),
                                          len(self.client.document), len(self.client.document))
        return consumed

    def scalar(self, value):
        self.assertEqual(1, len(value), "one Unicode scalar per IBus key event")
        keyval = self.IBus.unicode_to_keyval(value)
        self.assertEqual([ord(value)], [ord(c) for c in self.IBus.keyval_to_unicode(keyval)])
        return self.key(keyval, value)

    def type_text(self, value):
        for scalar in value:
            self.scalar(scalar)

    def assert_document(self, expected):
        self.assertEqual(expected, self.client.document, self.client.describe())
        self.assertEqual("", self.client.preedit, self.client.describe())

    def test_private_factory_event_delivery_and_protected_space(self):
        self.type_text("@kuraido")
        self.assertEqual("@kuraido", self.client.preedit, self.client.describe())
        self.assertTrue(self.key(self.IBus.KEY_space))
        self.assert_document("@kuraido ")
        self.assertEqual(["@kuraido "], self.client.commits)

    def test_boundary_backspace_preserves_protected_character(self):
        for raw in ("./z]", "./z[", "@z]", "@z["):
            with self.subTest(raw=raw):
                self.reset_client()
                self.type_text(raw)
                self.assertTrue(self.key(self.IBus.KEY_BackSpace))
                self.assertEqual(raw[:-1], self.client.preedit, self.client.describe())
                self.assertTrue(self.key(self.IBus.KEY_Return))
                self.assert_document(raw[:-1])

    def test_combining_scalar_passes_once(self):
        self.type_text("e")
        self.assertFalse(self.scalar("\u0301"))
        self.assert_document("e\u0301")
        self.assertEqual(["e"], self.client.commits)

    def test_supplementary_emoji_passes_once(self):
        self.type_text("ka")
        self.assertFalse(self.scalar("\U0001f600"))
        self.assert_document("か\U0001f600")
        self.assertEqual(["か"], self.client.commits)

    def test_real_mozc_candidates_and_commit(self):
        self.type_text("kyou")
        self.assertTrue(self.key(self.IBus.KEY_space))
        self.assertIn("今日", self.client.candidates, self.client.describe())
        for _ in range(len(self.client.candidates) + 1):
            if self.client.preedit == "今日":
                break
            self.assertTrue(self.key(self.IBus.KEY_Down))
        self.assertEqual("今日", self.client.preedit, self.client.describe())
        self.assertTrue(self.key(self.IBus.KEY_Return))
        self.assert_document("今日")

    def test_focus_out_commits_once(self):
        self.type_text("@kuraido")
        self.context.focus_out()
        self.pump(0.2)
        self.context.focus_out()
        self.pump(0.2)
        self.assert_document("@kuraido")
        self.assertEqual(["@kuraido"], self.client.commits)

    def mixed_protection(self, action):
        for token in ("@kuraido", "./shumire-shon"):
            with self.subTest(token=token, action=action):
                self.reset_client()
                self.type_text("kyouha「" + token + "」ashita")
                if action == "focus":
                    self.context.focus_out()
                    self.pump(0.2)
                else:
                    consumed = self.key({"Enter": self.IBus.KEY_Return,
                                         "Space": self.IBus.KEY_space,
                                         "Tab": self.IBus.KEY_Tab}[action])
                    self.assertEqual(action != "Tab", consumed)
                    if action == "Space":
                        self.assertTrue(self.key(self.IBus.KEY_Return))
                if action == "Space":
                    protected = "「" + token + "」"
                    self.assertEqual(1, self.client.document.count(protected), self.client.describe())
                    before, after = self.client.document.split(protected)
                    self.assertTrue(before and after, self.client.describe())
                    self.assertNotIn("kyouha", before)
                    self.assertNotIn("ashita", after)
                    self.assertEqual("", self.client.preedit, self.client.describe())
                else:
                    self.assert_document("きょうは「" + token + "」あした")
                if action == "focus":
                    self.assertEqual(1, len(self.client.commits), self.client.describe())

    def test_mixed_protection_enter(self):
        self.mixed_protection("Enter")

    def test_mixed_protection_space_then_enter(self):
        self.mixed_protection("Space")

    def test_mixed_protection_tab(self):
        self.mixed_protection("Tab")

    def test_mixed_protection_focus(self):
        self.mixed_protection("focus")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path, help="existing Meltype-linux package directory")
    args = parser.parse_args()
    if not os.environ.get("DISPLAY") or not os.environ.get("DBUS_SESSION_BUS_ADDRESS"):
        parser.error("run inside xvfb-run -a dbus-run-session; this test does not start an outer session")
    try:
        package = args.package.resolve(strict=True)
    except FileNotFoundError:
        parser.error("package directory does not exist")
    for relative in ("ibus-engine-meltype", "libMeltypeNative.so", "mozc/meltype_mozc_helper"):
        if not (package / relative).is_file():
            parser.error("package is missing " + relative)
    PrivateDaemonTests.package = package
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(PrivateDaemonTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    if not result.wasSuccessful():
        for label, output in PrivateDaemonTests.saved_logs:
            print(f"\n--- {label} log ---\n{output or '(empty)'}", file=sys.stderr, flush=True)
        return 1
    print("PASS: private IBus daemon and synthetic input context; desktop application GUI NOT_RUN")
    return 0


if __name__ == "__main__":
    sys.exit(main())
