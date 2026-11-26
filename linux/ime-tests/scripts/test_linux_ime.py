#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 HarukiKubota
"""Platform-independent checks for the Linux runner's input and isolation boundary."""

from pathlib import Path
from contextlib import redirect_stdout
import io
import json
import os
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


import linux_ime as runner


class LinuxImeRunnerTests(unittest.TestCase):
    def test_private_environment_removes_portal_and_isolates_persistent_data(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, {"IBUS_USE_PORTAL": "0"}):
            root = Path(temporary)
            address, settings = runner.isolate_environment(root)
            self.assertNotIn("IBUS_USE_PORTAL", os.environ)
            self.assertEqual("unix:path=" + str(root / "ibus.socket"), address)
            for variable in ("HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "MELTYPE_DATA_DIR", "TMPDIR"):
                self.assertTrue(Path(os.environ[variable]).is_relative_to(root), variable)
            self.assertFalse(settings["LearningEnabled"])
            self.assertFalse(json.loads((root / "settings/config.json").read_text())["LiveConversion"])

    def test_rejects_non_key_text_and_arbitrary_commands(self):
        target = {"windowId": "12", "context": "/org/freedesktop/IBus/InputContext_1", "password": False}
        for text in ("日本語", "a\nb", "", "a" * 301):
            with self.subTest(text=text[:10]), self.assertRaises(ValueError):
                runner.validate_operation({"action": "type", **target, "text": text})
        for key in ("--window", "exec", "a; echo bad"):
            with self.subTest(key=key), self.assertRaises(ValueError):
                runner.validate_operation({"action": "key", **target, "key": key})
        with self.assertRaises(ValueError):
            runner.validate_operation({"action": "type", **target, "text": "a", "window": "other"})
        runner.validate_operation({"action": "type", **target, "text": "aiueo"})
        runner.validate_operation({"action": "key", **target, "key": "Return"})
        with self.assertRaises(ValueError):
            runner.validate_operation({"action": "type", **target, "context": None, "text": "a"})
        runner.validate_operation({"action": "type", **target, "password": True, "context": None, "text": "dummy"})

    def test_rejects_incomplete_product_instead_of_using_a_test_double(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "ibus-engine-meltype").write_text("engine")
            with self.assertRaisesRegex(RuntimeError, "libMeltypeNative.so"):
                runner.package_files(root)
            (root / "libMeltypeNative.so").write_bytes(b"native")
            (root / "mozc").mkdir()
            helper = root / "mozc/meltype_mozc_helper"
            helper.write_bytes(b"mozc")
            with self.assertRaisesRegex(RuntimeError, "not executable"):
                runner.package_files(root)
            helper.chmod(0o700)
            self.assertEqual(root.resolve(), runner.package_files(root)[0])

    def test_timeout_is_a_failure(self):
        with self.assertRaisesRegex(TimeoutError, "input context"):
            runner.wait_for(lambda: None, "input context", lambda: None, seconds=0.01)

    def test_run_forwards_playwright_filters_through_the_linux_session(self):
        with patch.object(sys, "argv", ["linux_ime.py", "run", "--grep", "text: 入力"]), \
                patch.object(sys, "platform", "linux"), \
                patch.dict(os.environ, {"MELTYPE_PACKAGE_DIR": "/product"}), \
                patch.object(runner, "package_files"), \
                patch.object(runner.shutil, "which", return_value="/tool"), \
                patch.object(runner.subprocess, "call", return_value=7) as call:
            self.assertEqual(7, runner.main())
        arguments = call.call_args.args[0]
        self.assertEqual(["session", "--grep", "text: 入力"], arguments[-3:])
        self.assertEqual("xvfb-run", arguments[0])

    def test_streams_before_exit_and_preserves_failure_status(self):
        with tempfile.TemporaryDirectory() as temporary, redirect_stdout(io.StringIO()) as console:
            path = Path(temporary) / "playwright.log"
            release = Path(temporary) / "release"
            with path.open("w") as output:
                child = subprocess.Popen([sys.executable, "-c",
                    "import sys,time; from pathlib import Path\n"
                    "print('started',flush=True)\n"
                    "deadline=time.monotonic()+5\n"
                    "while not Path(sys.argv[1]).exists() and time.monotonic()<deadline: time.sleep(0.01)\n"
                    "print('failed'); sys.exit(7)", str(release)],
                    stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
                observed = []
                def pump():
                    observed.append(console.getvalue())
                    if console.getvalue() == "started\n":
                        release.touch()
                try:
                    status = runner.stream_process(child, path, pump)
                finally:
                    runner.stop_processes([child])
            self.assertEqual(7, status)
            self.assertIn("started\n", observed)
            self.assertEqual("started\nfailed\n", console.getvalue())
            self.assertEqual(console.getvalue(), path.read_text())

    def test_provenance_keeps_versions_and_requests_separate_from_runtime(self):
        env = {"GITHUB_RUN_ID": "123", "GITHUB_RUN_ATTEMPT": "2",
               "ImageOS": "ubuntu24", "ImageVersion": "20261015.1"}
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, env), \
                patch.object(runner, "command", side_effect=lambda args: "version:" + " ".join(args)):
            product = Path(temporary) / "product"
            product.write_bytes(b"product")
            manifest = runner.provenance(Path(temporary), {"product": product}, runner.SETTINGS)
        self.assertEqual("123", manifest["github"]["runId"])
        self.assertEqual("2", manifest["github"]["runAttempt"])
        self.assertEqual("20261015.1", manifest["github"]["runnerImageVersion"])
        self.assertEqual("version:dotnet --version", manifest["dotnetSdk"])
        self.assertEqual({"ibus", "xdotool", "xvfb"}, set(manifest["linuxPackages"]))
        self.assertRegex(manifest["mozcCommit"], r"^[a-f0-9]{40}$")
        self.assertRegex(manifest["sha256"]["product"], r"^[a-f0-9]{64}$")
        self.assertEqual(runner.SETTINGS, manifest["settings"]["requested"])
        self.assertIsNone(manifest["settings"]["productSaved"])
        self.assertTrue({"ibusAddress", "daemonPid", "productPid"}.isdisjoint(manifest))

    def test_product_settings_must_be_deserialized_with_every_requested_key(self):
        with tempfile.TemporaryDirectory() as temporary, patch.dict(os.environ, {
                "MELTYPE_DATA_DIR": temporary, "MELTYPE_IME_ARTIFACTS": temporary}):
            root = Path(temporary)
            manifest = root / "session.json"
            manifest.write_text(json.dumps({"settings": {"requested": runner.SETTINGS, "productSaved": None}}))
            config = root / "config.json"
            config.write_text(json.dumps(runner.SETTINGS))
            with self.assertRaisesRegex(RuntimeError, "migrate"):
                runner.record_product_settings()
            settings = {**runner.SETTINGS, "SettingsVersion": 5}
            for key in runner.SETTINGS:
                for value in (None, True, 0):
                    with self.subTest(key=key, value=value):
                        config.write_text(json.dumps({**settings, key: value}))
                        with self.assertRaisesRegex(RuntimeError, key):
                            runner.record_product_settings()
                        self.assertEqual(value, json.loads(manifest.read_text())["settings"]["productSaved"][key])
            config.write_text(json.dumps(settings))
            runner.record_product_settings()
            self.assertEqual(settings, json.loads(manifest.read_text())["settings"]["productSaved"])

    def test_cleanup_stops_only_its_own_process_group(self):
        child = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"], start_new_session=True)
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"], start_new_session=True)
        try:
            runner.stop_processes([child])
            self.assertIsNotNone(child.poll())
            self.assertIsNone(unrelated.poll())
            os.kill(os.getpid(), 0)
        finally:
            runner.stop_processes([child, unrelated])

    def test_cleanup_stops_descendants_after_the_leader_has_exited(self):
        descendant_code = "import signal,time; signal.signal(signal.SIGTERM, signal.SIG_IGN); print('ready',flush=True); time.sleep(60)"
        leader_code = ("import subprocess,sys,time; "
                       "p=subprocess.Popen([sys.executable,'-c',sys.argv[1]],stdout=subprocess.PIPE,text=True); "
                       "p.stdout.readline(); print(p.pid,flush=True); time.sleep(60)")
        leader = subprocess.Popen([sys.executable, "-c", leader_code, descendant_code],
                                  stdout=subprocess.PIPE, text=True, start_new_session=True)
        try:
            descendant = int(leader.stdout.readline())
            runner.stop_processes([leader])
            def stopped():
                state = subprocess.run(["ps", "-o", "stat=", "-p", str(descendant)],
                                       capture_output=True, text=True, timeout=2).stdout.strip()
                return not state or state.startswith("Z")
            runner.wait_for(stopped, "terminated descendant", lambda: None, seconds=2)
        finally:
            runner.stop_processes([leader])
            leader.stdout.close()

    def test_x_test_targets_only_the_selected_os_focus_without_foreign_context_calls(self):
        commands = []
        focused = {"window": "12", "context": "/org/freedesktop/IBus/InputContext_1"}
        context_path = "/org/freedesktop/IBus/InputContext_1"
        # Deliberately no InputContext API: another client's context cannot be called.
        bus = SimpleNamespace(is_connected=lambda: True, current_input_context=lambda: focused["context"],
                              get_global_engine=lambda: SimpleNamespace(get_name=lambda: "meltype"))
        ibus = SimpleNamespace(Bus=lambda: bus)
        def command(args):
            commands.append(args)
            return "12" if args[:2] == ["xdotool", "search"] else focused["window"] if args == ["xdotool", "getwindowfocus"] else ""
        with tempfile.TemporaryDirectory() as temporary:
            manifest = Path(temporary) / "session.json"
            manifest.write_text(json.dumps({"version": 1, "ibusAddress": "unix:path=test",
                                            "daemonPid": os.getpid(), "productPid": os.getpid()}))
            with patch.dict(os.environ, {"MELTYPE_IME_SESSION": str(manifest), "IBUS_ADDRESS": "unix:path=test"}), \
                    patch.object(runner, "ibus_api", return_value=(None, ibus)), \
                    patch.object(runner, "command", side_effect=command), \
                    patch.object(runner, "record_product_settings") as record_settings:
                operation = {"action": "type", "windowId": "12", "context": context_path, "password": False, "text": "aiueo"}
                with redirect_stdout(io.StringIO()) as output:
                    runner.control(operation)
                self.assertFalse(json.loads(output.getvalue())["password"])
                self.assertEqual(["xdotool", "type", "--clearmodifiers", "--delay", "30", "--", "aiueo"], commands[-1])
                commands.clear()
                focused["window"] = "13"
                with self.assertRaisesRegex(RuntimeError, "focus"):
                    runner.control(operation)
                self.assertEqual([["xdotool", "getwindowfocus"]], commands)
                # No set_global_engine method exists on this fake bus: a focus
                # change must observe the selection, not silently repair it.
                focused["window"] = "12"
                title = "Meltype IME E2E " + "a" * 36
                with redirect_stdout(io.StringIO()):
                    runner.control({"action": "focus", "title": title, "password": False})
                record_settings.assert_called_once_with()
                focused["context"] = None
                commands.clear()
                with self.assertRaisesRegex(RuntimeError, "focus"):
                    runner.control(operation)
                with redirect_stdout(io.StringIO()) as output:
                    runner.control({"action": "focus", "title": title, "password": True})
                record_settings.assert_called_once_with()
                self.assertIsNone(json.loads(output.getvalue())["context"])
                with redirect_stdout(io.StringIO()):
                    runner.control({**operation, "password": True, "context": None, "text": "dummy8472"})
                focused["window"] = "13"
                with self.assertRaisesRegex(RuntimeError, "focus"):
                    runner.control({**operation, "password": True, "context": None})


if __name__ == "__main__":
    unittest.main()
