#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 HarukiKubota
"""Run the product IBus engine and Chromium in a private Linux desktop session.

The browser creates the input context. Only XTEST sends input; this module never
calls Core test cases, creates a synthetic IBus client, or injects composition.
"""

import argparse
import codecs
import hashlib
import json
import os
import platform
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time


ROOT = Path(__file__).resolve().parents[1]
PRODUCT_FILES = ("ibus-engine-meltype", "libMeltypeNative.so", "mozc/meltype_mozc_helper")
KEYS = {"Return", "space", "Escape", "BackSpace", "Tab", "Shift+Tab", "Shift+Return",
        "Left", "Right", "Up", "Down", "Home", "End", "ctrl+a"}
SETTINGS = {"LiveConversion": False, "LearningEnabled": False, "SpaceAroundEnglish": False,
            "SigilWordsDirect": False, "TabConversion": False}


def package_files(directory):
    package = Path(directory).resolve(strict=True)
    files = {name: package / name for name in PRODUCT_FILES}
    for name, path in files.items():
        if not path.is_file():
            raise RuntimeError("Product file is missing: " + name)
    if not os.access(files["mozc/meltype_mozc_helper"], os.X_OK):
        raise RuntimeError("Mozc helper is not executable")
    return package, files


def validate_operation(value):
    if not isinstance(value, dict):
        raise ValueError("Input operation must be an object")
    action = value.get("action")
    if action in ("select", "focus"):
        expected = {"action", "title"} if action == "select" else {"action", "title", "password"}
        if set(value) != expected or not isinstance(value["title"], str) or not re.fullmatch(
                r"Meltype IME E2E [a-f0-9-]{36}", value["title"]):
            raise ValueError("Invalid browser title")
        if action == "focus" and not isinstance(value["password"], bool):
            raise ValueError("Invalid password mode")
    elif action in ("type", "key"):
        field = "text" if action == "type" else "key"
        if set(value) != {"action", "windowId", "context", "password", field}:
            raise ValueError("Unexpected input operation fields")
        if not isinstance(value["windowId"], str) or not re.fullmatch(r"[1-9][0-9]*", value["windowId"]):
            raise ValueError("Invalid X11 window")
        if not isinstance(value["password"], bool):
            raise ValueError("Invalid password mode")
        if not (value["password"] and value["context"] is None):
            if not isinstance(value["context"], str) or not value["context"].startswith("/org/freedesktop/IBus/"):
                raise ValueError("Invalid IBus context")
        if action == "type" and (not isinstance(value[field], str) or not re.fullmatch(r"[\x20-\x7e]{1,300}", value[field])):
            raise ValueError("Only physical ASCII key sequences are accepted")
        if action == "key" and (not isinstance(value[field], str) or value[field] not in KEYS):
            raise ValueError("Unsupported key")
    else:
        raise ValueError("Unsupported input operation")
    return value


def command(args):
    return subprocess.check_output(args, text=True, timeout=10).strip()


def wait_for(read, description, pump, seconds=10):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        result = read()
        if result:
            return result
        pump()
        time.sleep(0.02)
    raise TimeoutError("Timed out waiting for " + description)


def stop_processes(processes):
    # Signal only process groups created by this invocation. Never use pkill/ibus exit.
    for process in reversed(processes):
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            process.wait(timeout=5)
            continue
        try:
            process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            pass
        # The leader may exit while a descendant ignores SIGTERM. Clean up the
        # remaining owned group too, not just the leader tracked by Popen.
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=5)


def stream_process(process, path, pump):
    # Read a regular log file, so a quiet child never blocks IBus pumping.
    decoder = codecs.getincrementaldecoder("utf-8")()
    with path.open("rb") as output:
        while True:
            finished = process.poll() is not None
            sys.stdout.write(decoder.decode(output.read(), final=finished))
            sys.stdout.flush()
            if finished:
                return process.returncode
            pump()
            time.sleep(0.05)


def record_product_settings():
    # The initial config has no SettingsVersion. Settings.Load migrates and saves
    # it on the first product input context, dropping unrecognized property names.
    # This is evidence of product deserialization, not a live settings API.
    settings = json.loads((Path(os.environ["MELTYPE_DATA_DIR"]) / "config.json").read_text())
    path = Path(os.environ["MELTYPE_IME_ARTIFACTS"]) / "session.json"
    manifest = json.loads(path.read_text())
    manifest["settings"]["productSaved"] = settings
    path.write_text(json.dumps(manifest, indent=2))
    if type(settings.get("SettingsVersion")) is not int:
        raise RuntimeError("Product did not migrate the test settings")
    for key, value in SETTINGS.items():
        if type(settings.get(key)) is not bool or settings[key] != value:
            raise RuntimeError("Product test setting is missing or changed: " + key)


def provenance(package, files, settings):
    return {"version": 1,
            "repositoryRevision": command(["git", "-C", str(ROOT), "rev-parse", "HEAD"]),
            "repositoryDirty": bool(command(["git", "-C", str(ROOT), "status", "--porcelain"])),
            "pullRequestHead": os.environ.get("PR_HEAD_SHA") or None,
            "github": {"runId": os.environ.get("GITHUB_RUN_ID"),
                       "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
                       "runnerImage": os.environ.get("ImageOS"),
                       "runnerImageVersion": os.environ.get("ImageVersion")},
            "platform": platform.platform(),
            "node": command(["node", "--version"]),
            "dotnetSdk": command(["dotnet", "--version"]),
            "mozcCommit": (ROOT.parent.parent / "native/mozc/MOZC_COMMIT").read_text().strip(),
            "linuxPackages": {name: command(["dpkg-query", "-W", "-f=${Version}", name])
                              for name in ("ibus", "xdotool", "xvfb")},
            "playwright": command(["node", str(ROOT / "node_modules/playwright/cli.js"), "--version"]),
            "browser": command(["node", "--input-type=module", "-e",
                "import { chromium } from 'playwright'; import { execFileSync } from 'node:child_process'; "
                "process.stdout.write(execFileSync(chromium.executablePath(), ['--version'], {encoding:'utf8'}))"]),
            "package": str(package),
            "settings": {"requested": settings, "productSaved": None},
            "sha256": {name: hashlib.sha256(path.read_bytes()).hexdigest() for name, path in files.items()}}


def ibus_api():
    import gi
    gi.require_version("IBus", "1.0")
    from gi.repository import GLib, IBus
    IBus.init()
    return GLib, IBus


def control(operation):
    operation = validate_operation(operation)
    runtime = json.loads(Path(os.environ["MELTYPE_IME_SESSION"]).read_text())
    if runtime.get("version") != 1 or runtime.get("ibusAddress") != os.environ.get("IBUS_ADDRESS"):
        raise RuntimeError("Private IBus session does not match")
    for name in ("daemonPid", "productPid"):
        pid = runtime.get(name)
        if not isinstance(pid, int) or isinstance(pid, bool) or pid <= 0:
            raise RuntimeError("Invalid product process")
        os.kill(pid, 0)
    GLib, IBus = ibus_api()
    bus = IBus.Bus()
    if not bus.is_connected():
        raise RuntimeError("Private IBus is disconnected")

    def pump():
        main = GLib.MainContext.default()
        while main.pending():
            main.iteration(False)

    def focused_context():
        try:
            return bus.current_input_context()
        except GLib.Error:
            # Chromium may have no IBus context in a password field.
            return None

    password = operation.get("password", False)
    if operation["action"] in ("select", "focus"):
        windows = command(["xdotool", "search", "--sync", "--onlyvisible", "--name", "^" + re.escape(operation["title"])]).splitlines()
        if len(windows) != 1 or not re.fullmatch(r"[1-9][0-9]*", windows[0]):
            raise RuntimeError("Expected exactly one test browser window")
        window = windows[0]
        command(["xdotool", "windowfocus", "--sync", window])
        context_path = focused_context() if password else wait_for(focused_context, "Chromium input context", pump)
        # Initial selection only. Never repair focus/password recovery by reselecting.
        if operation["action"] == "select" and not bus.set_global_engine("meltype"):
            raise RuntimeError("Could not select product Meltype engine")
    else:
        window, context_path = operation["windowId"], operation["context"]

    # InputContext methods are restricted to the Chromium connection that owns it.
    # Inspect the public bus selection and focused-context path instead.
    def selected_engine():
        engine = bus.get_global_engine()
        return engine and engine.get_name() == "meltype"

    wait_for(selected_engine, "global Meltype selection", pump)
    if command(["xdotool", "getwindowfocus"]) != window or (not password and focused_context() != context_path):
        raise RuntimeError("OS focus or IBus context changed before key delivery")
    if operation["action"] in ("select", "focus") and not password:
        record_product_settings()
    if operation["action"] == "type":
        # No --window or xdotool command chaining: use XTEST, never XSendEvent.
        command(["xdotool", "type", "--clearmodifiers", "--delay", "30", "--", operation["text"]])
    elif operation["action"] == "key":
        command(["xdotool", "key", "--clearmodifiers", "--", operation["key"]])
    # 入力した文字は返さない。password は欄の種類を表す真偽値だけ。
    print(json.dumps({"windowId": window, "context": context_path, "password": True if password else False}))


def isolate_environment(root):
    for variable, leaf in (("HOME", "home"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache"),
                           ("XDG_CONFIG_HOME", "config"), ("XDG_RUNTIME_DIR", "runtime"),
                           ("MELTYPE_DATA_DIR", "settings"), ("TMPDIR", "tmp")):
        directory = root / leaf
        directory.mkdir(mode=0o700)
        os.environ[variable] = str(directory)
    settings = SETTINGS.copy()
    (root / "settings/config.json").write_text(json.dumps(settings))
    address = "unix:path=" + str(root / "ibus.socket")
    os.environ.update({"IBUS_ADDRESS": address, "GTK_IM_MODULE": "ibus",
                       "QT_IM_MODULE": "ibus", "XMODIFIERS": "@im=ibus", "GDK_BACKEND": "x11",
                       "GSETTINGS_BACKEND": "memory", "NO_AT_BRIDGE": "1"})
    # GTK checks presence, so even IBUS_USE_PORTAL=0 would bypass the private bus.
    os.environ.pop("IBUS_USE_PORTAL", None)
    os.environ["MELTYPE_IME_SESSION"] = str(root / "linux-runtime.json")
    return address, settings


def session(playwright_args):
    package, files = package_files(os.environ["MELTYPE_PACKAGE_DIR"])
    artifacts = Path(os.environ["MELTYPE_IME_ARTIFACTS"]).resolve()
    artifacts.mkdir(parents=True, exist_ok=True)
    processes, outputs = [], []
    with tempfile.TemporaryDirectory(prefix="meltype-real-ime-") as temporary:
        root = Path(temporary)
        address, settings = isolate_environment(root)

        def start(name, args):
            output = (artifacts / (name + ".log")).open("w")
            outputs.append(output)
            process = subprocess.Popen(args, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            processes.append(process)
            return process

        def check():
            for process in processes[:2]:
                if process.poll() is not None:
                    raise RuntimeError("Private IBus or product engine exited: " + str(process.returncode))

        try:
            command(["setxkbmap", "-layout", "us"])
            daemon = start("ibus", ["ibus-daemon", "-s", "-p", "disable", "-c", "disable", "-E", "disable",
                                    "-a", address, "--cache=none"])
            wait_for(lambda: (root / "ibus.socket").exists(), "private IBus socket", check)
            GLib, IBus = ibus_api()
            bus = IBus.Bus()
            wait_for(bus.is_connected, "private IBus connection", check)
            product = start("meltype", [sys.executable, str(files["ibus-engine-meltype"])])
            wait_for(lambda: any(e.get_name() == "meltype" for e in bus.list_active_engines()),
                     "product engine registration", check)
            runtime = {"version": 1, "ibusAddress": address, "daemonPid": daemon.pid, "productPid": product.pid}
            Path(os.environ["MELTYPE_IME_SESSION"]).write_text(json.dumps(runtime))
            manifest = provenance(package, files, settings)
            (artifacts / "session.json").write_text(json.dumps(manifest, indent=2))
            tests = start("playwright", ["node", str(ROOT / "node_modules/playwright/cli.js"), "test",
                                         "--config", str(ROOT / "playwright.ime.linux.config.ts"), *playwright_args])

            def pump():
                check()
                main = GLib.MainContext.default()
                while main.pending():
                    main.iteration(False)

            result = stream_process(tests, artifacts / "playwright.log", pump)
            check()
            return result
        finally:
            stop_processes(processes)
            for output in outputs:
                output.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("run", "session", "control"))
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if sys.platform != "linux":
        parser.error("Real IME execution requires Linux; no DOM-only substitute is used")
    if args.action == "control":
        if len(args.arguments) != 1:
            parser.error("control requires one JSON operation")
        control(json.loads(args.arguments[0]))
        return 0
    if args.action == "session":
        def interrupted(*_):
            raise KeyboardInterrupt()
        signal.signal(signal.SIGTERM, interrupted)
        return session(args.arguments)
    package_files(os.environ["MELTYPE_PACKAGE_DIR"])
    for tool in ("xvfb-run", "dbus-run-session", "ibus-daemon", "xdotool", "setxkbmap", "node", "dotnet", "dpkg-query"):
        if shutil.which(tool) is None:
            parser.error("Required executable is missing: " + tool)
    # Resolve the installed Playwright browser cache before isolating HOME.
    if "PLAYWRIGHT_BROWSERS_PATH" not in os.environ:
        os.environ["PLAYWRIGHT_BROWSERS_PATH"] = str(Path.home() / ".cache/ms-playwright")
    os.environ["MELTYPE_IME_ARTIFACTS"] = str(Path(os.environ.get("MELTYPE_IME_ARTIFACTS", ROOT / "artifacts/real-ime")).resolve())
    return subprocess.call(["xvfb-run", "-a", "-s", "-screen 0 1280x900x24 -nolisten tcp",
                            "dbus-run-session", "--", sys.executable, str(Path(__file__).resolve()), "session",
                            *args.arguments], cwd=ROOT)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (KeyError, ValueError, OSError, RuntimeError, subprocess.SubprocessError) as error:
        print("Real IME infrastructure error: " + str(error), file=sys.stderr)
        raise SystemExit(1)
