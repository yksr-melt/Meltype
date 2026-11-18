#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""A real isolated IBus daemon/client exercises the Rust engine executable.

Run with: xvfb-run -a dbus-run-session -- python3 rust/ibus/tests/integration.py PATH_TO_ENGINE
No desktop input-source registration or user IBus daemon is modified.
"""
import json
import ctypes
import os
from pathlib import Path
import subprocess
import shutil
import sys
import tempfile
import time
import unittest
import gi

gi.require_version("IBus", "1.0")
from gi.repository import GLib, IBus

# PyGObject 3.56はシグナル引数のfloating参照をPython側で引き取る。
# IBusがシグナル配送後に参照を確認するまで、ラッパーを生存させる。
_signal_objects = []

def signal_handler(callback):
    def invoke(*args):
        _signal_objects.extend(arg for arg in args if isinstance(arg, IBus.Serializable))
        return callback(*args)
    return invoke

def pump(seconds=0.05):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        while GLib.MainContext.default().pending():
            GLib.MainContext.default().iteration(False)
            _signal_objects.clear()
        time.sleep(0.002)

def wait(predicate, description, timeout=15):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if predicate():
            return
        pump()
    raise AssertionError("timed out: " + description)

class EngineTests(unittest.TestCase):
    executable = Path(sys.argv[1]).resolve()

    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory(prefix="meltype-flash-ibus-")
        cls.addClassCleanup(cls.directory.cleanup)
        cls.root = Path(cls.directory.name)
        cls.installed = os.environ.get("MELTYPE_IBUS_TEST_INSTALLED") == "1"
        for variable, leaf in [("XDG_DATA_HOME", "xdg-data"), ("XDG_CACHE_HOME", "cache"), ("XDG_CONFIG_HOME", "config"), ("XDG_RUNTIME_DIR", "runtime")]:
            directory = cls.root / leaf
            directory.mkdir(mode=0o700)
            os.environ[variable] = str(directory)
        os.environ["IBUS_ADDRESS"] = "unix:path=" + str(cls.root / "ibus-bus")
        os.environ["MELTYPE_RUST_DATA_DIR"] = str(cls.root / "data")
        components = cls.root / "xdg-data/ibus/component"
        if cls.installed:
            bundle = cls.root / "bundle"
            bundle.mkdir()
            shutil.copy2(cls.executable, bundle / "meltype-ibus")
            shutil.copy2(Path(__file__).resolve().parents[3] / "linux/icon.png", bundle / "icon.png")
            for name in ["meltype-flash", "meltype-rs"]:
                (bundle / name).write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
            script = Path(__file__).resolve().parents[1] / "install.sh"
            shutil.copy2(script, bundle / "install.sh")
            prefix = cls.root / "日本語 & installed' path"
            subprocess.run(["bash", str(bundle / "install.sh"), "--system", "--prefix", str(prefix)], check=True, capture_output=True)
            components = prefix / "share/ibus/component"
        components.mkdir(parents=True, exist_ok=True)
        os.environ["IBUS_COMPONENT_PATH"] = str(components)
        (cls.root / "data").mkdir()
        (cls.root / "data/config.json").write_text(json.dumps({"LiveConversion":False, "PredictiveCandidates":False, "SigilWordsDirect":True, "CursorIntoBrackets":True, "FullWidthParentheses":False}), encoding="utf-8")
        (cls.root / "data/state.json").write_text(json.dumps({"preferences":{"user_dictionary":{"めるたいぷ":[f"候補{i}" for i in range(18)],"ちな":["千奈"]}}}), encoding="utf-8")
        cls.daemon_log = (cls.root / "daemon.log").open("w+")
        cls.daemon = subprocess.Popen(["ibus-daemon", "--single", "--panel", "disable", "--config", "disable", "--emoji-extension", "disable", "--address", os.environ["IBUS_ADDRESS"], "--cache=none", "--timeout=3000"], stdout=cls.daemon_log, stderr=subprocess.STDOUT)
        cls.addClassCleanup(cls.stop, cls.daemon)
        IBus.init()
        wait(lambda: (cls.root / "ibus-bus").exists(), "isolated IBus socket")
        cls.bus = IBus.Bus.new()
        wait(cls.bus.is_connected, "IBus connection")
        cls.panel = IBus.PanelService.new(cls.bus.get_connection())
        cls.bus.request_name("org.freedesktop.IBus.Panel", 4)
        cls.addClassCleanup(cls.panel.destroy)
        cls.engine_log = (cls.root / "engine.log").open("w+")
        if cls.installed:
            wait(lambda: any(e.get_name() == "meltype-flash" for e in cls.bus.list_engines()), "Rust component discovery")
        else:
            cls.engine = subprocess.Popen([str(cls.executable)], stdout=cls.engine_log, stderr=subprocess.STDOUT, env={**os.environ, "G_DEBUG":"fatal-warnings"})
            cls.addClassCleanup(cls.stop, cls.engine)
            wait(lambda: any(e.get_name() == "meltype-flash" for e in cls.bus.list_active_engines()), "Rust factory registration")

    @staticmethod
    def stop(process):
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=5)

    def setUp(self):
        self.document = ""
        self.caret = None
        self.forwarded = []
        self.marked = ""
        self.candidates = []
        self.selected_candidate = 0
        self.deletions = []
        self.auxiliary = ""
        self.context_requests = 0
        self.selection = None
        client = "gtk3-im:code" if self._testMethodName == "test_native_application_code_mode" else "Meltype-FLASH integration"
        self.context = self.bus.create_input_context(client)
        self.addCleanup(self.context.destroy)
        def connect(name, callback):
            self.context.connect(name, signal_handler(callback))
        connect("commit-text", self.commit)
        connect("forward-key-event", self.forward)
        connect("update-preedit-text", self.preedit)
        connect("update-lookup-table", self.lookup)
        connect("hide-lookup-table", lambda *_: self.candidates.clear())
        connect("update-auxiliary-text", lambda _ic, text, visible: setattr(self, "auxiliary", text.get_text() if visible else ""))
        connect("delete-surrounding-text", self.delete)
        connect("require-surrounding-text", self.require_surrounding)
        self.context.set_capabilities(int(IBus.Capabilite.PREEDIT_TEXT | IBus.Capabilite.AUXILIARY_TEXT | IBus.Capabilite.LOOKUP_TABLE | IBus.Capabilite.FOCUS | IBus.Capabilite.PROPERTY | IBus.Capabilite.SURROUNDING_TEXT))
        self.context.focus_in()
        pump()
        # set_engine is asynchronous and redundantly selecting the same engine
        # can reset its composition after the first ProcessKeyEvent. Select once
        # through the bus and only observe readiness while polling.
        self.assertTrue(self.bus.set_global_engine("meltype-flash"))
        def selected():
            description = self.context.get_engine()
            return description is not None and description.get_name() == "meltype-flash"
        wait(selected, "selected test engine")
        self.surrounding()
        pump()

    def surrounding(self):
        at = len(self.document) if self.caret is None else self.caret
        cursor, anchor = self.selection or (at, at)
        self.context.set_surrounding_text(IBus.Text.new_from_string(self.document), cursor, anchor)

    def require_surrounding(self, *_):
        self.context_requests += 1
        self.surrounding()

    def commit(self, _ic, text):
        at = len(self.document) if self.caret is None else self.caret
        value = text.get_text()
        self.document = self.document[:at] + value + self.document[at:]
        if self.caret is not None:
            self.caret += len(value)
        self.surrounding()

    def forward(self, _ic, keyval, keycode, state):
        self.forwarded.append((keyval, state))
        if keyval == IBus.KEY_Left and not state & int(IBus.ModifierType.RELEASE_MASK):
            self.caret = max(0, (len(self.document) if self.caret is None else self.caret) - 1)
            self.surrounding()

    def preedit(self, _ic, text, cursor, visible):
        self.marked = text.get_text() if visible else ""

    def lookup(self, _ic, table, visible):
        self.candidates = [table.get_candidate(i).get_text() for i in range(table.get_number_of_candidates())] if visible else []
        self.selected_candidate = table.get_cursor_pos()

    def delete(self, _ic, offset, count):
        self.deletions.append((offset, count))
        at = len(self.document) + offset
        self.document = self.document[:at] + self.document[at + count:]
        self.surrounding()

    def key(self, keyval, scalar=None, state=0):
        self.surrounding()
        consumed = self.context.process_key_event(keyval, 0, state)
        pump(0.015)
        if not consumed and scalar is not None:
            self.commit(None, IBus.Text.new_from_string(scalar))
        return consumed

    def type(self, raw):
        return [self.key(IBus.unicode_to_keyval(c), c) for c in raw]

    def test_jis_layout_survives_japanese_direct_mode_toggle(self):
        # Compile the layout declared by the real engine, not synthetic keysyms.
        layout = self.context.get_engine().get_layout()
        self.assertEqual(layout, "jp")
        xkb = ctypes.CDLL("libxkbcommon.so.0")
        pointer, uint = ctypes.c_void_p, ctypes.c_uint32
        class Names(ctypes.Structure):
            _fields_ = [(name, ctypes.c_char_p) for name in ["rules", "model", "layout", "variant", "options"]]
        signatures = {
            "xkb_context_new": ([uint], pointer),
            "xkb_keymap_new_from_names": ([pointer, ctypes.POINTER(Names), uint], pointer),
            "xkb_state_new": ([pointer], pointer),
            "xkb_state_update_key": ([pointer, uint, uint], uint),
            "xkb_state_key_get_one_sym": ([pointer, uint], uint),
            "xkb_keymap_key_by_name": ([pointer, ctypes.c_char_p], uint),
        }
        for name, (arguments, result) in signatures.items():
            getattr(xkb, name).argtypes = arguments
            getattr(xkb, name).restype = result
        for name in ["context", "keymap", "state"]:
            getattr(xkb, "xkb_" + name + "_unref").argtypes = [pointer]
        context = xkb.xkb_context_new(0)
        self.assertTrue(context)
        self.addCleanup(xkb.xkb_context_unref, context)
        keymap = xkb.xkb_keymap_new_from_names(context, ctypes.byref(Names(b"evdev", b"jp106", layout.encode(), b"", b"")), 0)
        self.assertTrue(keymap)
        self.addCleanup(xkb.xkb_keymap_unref, keymap)
        state = xkb.xkb_state_new(keymap)
        self.assertTrue(state)
        self.addCleanup(xkb.xkb_state_unref, state)
        def symbol(name):
            return xkb.xkb_state_key_get_one_sym(state, xkb.xkb_keymap_key_by_name(keymap, name))
        toggle = symbol(b"TLDE")
        self.assertEqual(toggle, IBus.KEY_Zenkaku_Hankaku)
        for _ in range(3):
            self.document = ""
            self.assertTrue(self.key(toggle))
            self.assertEqual(symbol(b"AE02"), ord("2"))
            self.assertFalse(self.key(symbol(b"AE02"), "2"))
            shift = xkb.xkb_keymap_key_by_name(keymap, b"LFSH")
            xkb.xkb_state_update_key(state, shift, 1)
            self.assertEqual(symbol(b"AE02"), ord('"'))
            self.assertFalse(self.key(symbol(b"AE02"), '"', int(IBus.ModifierType.SHIFT_MASK)))
            xkb.xkb_state_update_key(state, shift, 0)
            self.assertEqual(self.document, '2"')
            # Test the mode in a fresh text field; an open ASCII quote affects
            # automatic language detection independently of keyboard layout.
            self.document = ""
            self.assertTrue(self.key(toggle))
            self.assertTrue(all(self.type("kana")))
            self.assertEqual(self.marked, "かな")
            self.key(IBus.KEY_Return)
            self.assertEqual(self.document, "かな")
            self.assertEqual(self.context.get_engine().get_layout(), "jp")

    def test_issue_487_registered_name_does_not_split_known_longer_word(self):
        self.type("chinamini")
        self.key(IBus.KEY_space," ")
        self.assertEqual(self.marked,"ちなみに")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document,"ちなみに")

    def test_japanese_conversion_candidates_and_commit(self):
        self.assertTrue(all(self.type("hashi")))
        self.assertEqual(self.marked, "はし")
        self.assertTrue(self.key(IBus.KEY_space, " "))
        self.assertIn("橋", self.candidates)
        self.assertIn("箸", self.candidates)
        self.assertTrue(self.key(IBus.KEY_Return))
        self.assertIn(self.document, ["橋", "箸", "端", "はし"])
        self.assertEqual(self.marked, "")

    def test_native_application_code_mode(self):
        self.assertFalse(any(self.type("const answer = 42;")))
        self.assertEqual(self.document, "const answer = 42;")
        self.assertEqual(self.marked, "")
        self.document = "// "
        self.surrounding()
        self.assertTrue(all(self.type("nihongo")))
        self.assertEqual(self.marked, "にほんご")
        self.assertTrue(self.key(IBus.KEY_Return))
        self.assertEqual(self.document, "// にほんご")

    def test_completion_keys_pass_and_space_returns_to_japanese(self):
        self.assertFalse(any(self.type("@src/app.ts")))
        self.assertEqual(self.document, "@src/app.ts")
        self.assertEqual(self.marked, "")
        # Space reaches the application without ProcessKeyEvent.
        self.document += " "
        self.surrounding()
        self.assertTrue(all(self.type("kyou")))
        self.assertEqual(self.marked, "きょう")

    def test_candidate_panel_click_selects_the_requested_candidate(self):
        self.type("hashi")
        self.key(IBus.KEY_space, " ")
        index = next(i for i, value in enumerate(self.candidates) if value.startswith("箸"))
        self.panel.candidate_clicked(index, 1, 0)
        wait(lambda: self.document == "箸", "panel candidate commit")
        self.assertEqual(self.document, "箸")
        self.assertEqual(self.marked, "")

    def test_typing_candidates_can_be_clicked_before_conversion(self):
        self.type("go")
        self.assertEqual(self.candidates[:2], ["go", "ご"])
        self.panel.candidate_clicked(1, 1, 0)
        wait(lambda: self.document == "ご", "typing candidate commit")
        self.assertEqual(self.marked, "")

    def test_candidate_panel_paging_click_uses_the_current_page(self):
        self.type("merutaipu")
        self.key(IBus.KEY_space, " ")
        self.assertGreaterEqual(len(self.candidates), 18)
        self.assertEqual(self.selected_candidate, 0)
        self.panel.page_down()
        wait(lambda: self.selected_candidate == 9, "panel page down")
        self.assertEqual(self.selected_candidate, 9)
        self.panel.page_up()
        wait(lambda: self.selected_candidate == 0, "panel page up")
        self.assertEqual(self.selected_candidate, 0)
        self.panel.cursor_down()
        wait(lambda: self.selected_candidate == 1, "panel cursor down")
        self.assertEqual(self.selected_candidate, 1)
        self.panel.cursor_up()
        wait(lambda: self.selected_candidate == 0, "panel cursor up")
        self.assertEqual(self.selected_candidate, 0)
        self.panel.page_down()
        wait(lambda: self.selected_candidate == 9, "panel second page down")
        expected = self.candidates[10]
        self.panel.candidate_clicked(1, 1, 0)
        wait(lambda: self.document == expected, "second-page candidate commit")
        self.assertEqual(self.document, expected)
        self.assertEqual(self.marked, "")

    def test_direct_mode_jis_keys_and_unicode(self):
        self.assertTrue(self.key(IBus.KEY_Muhenkan))
        self.assertFalse(any(self.type("hello")))
        self.assertTrue(self.key(IBus.KEY_Henkan))
        self.type("ka")
        self.assertEqual(self.marked, "か")
        self.key(IBus.KEY_Escape)
        self.type("İ😀")
        self.key(IBus.KEY_Return)
        self.assertTrue(self.document.endswith("İ😀"))

    def test_focus_out_commits_once_and_release_does_not_type(self):
        self.assertFalse(self.key(IBus.KEY_a, state=int(IBus.ModifierType.RELEASE_MASK)))
        self.assertEqual(self.marked, "")
        self.type("kyou")
        self.context.focus_out()
        pump(0.1)
        self.assertEqual(self.document, "きょう")
        self.assertEqual(self.marked, "")

    def test_focus_out_preserves_pending_preedit_and_enter_finalizes_it(self):
        self.type("nihon")
        self.assertEqual(self.marked, "にほn")
        self.context.focus_out()
        pump()
        # IBus commits its existing preedit before notifying the engine.
        self.assertEqual(self.document, "にほn")
        self.context.focus_in()
        pump()
        self.type("nihon")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "にほnにほん")

    def test_password_never_requests_context_or_creates_preedit(self):
        self.context.set_content_type(int(IBus.InputPurpose.PASSWORD), 0)
        pump()
        requests = self.context_requests
        self.assertFalse(any(self.type("secret")))
        self.assertEqual(self.marked, "")
        self.assertEqual(self.context_requests, requests)
        self.assertEqual(self.deletions, [])
        self.context.set_content_type(int(IBus.InputPurpose.FREE_FORM), 0)
        pump()
        self.type("ka")
        self.assertEqual(self.marked, "か")

    def test_private_hint_preserves_conversion_without_learning(self):
        self.context.set_content_type(int(IBus.InputPurpose.FREE_FORM), 1 << 11)
        pump()
        state = (self.root / "data/state.json").read_bytes()
        self.assertTrue(all(self.type("nihongo")))
        self.assertEqual(self.marked, "にほんご")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "にほんご")
        self.assertEqual((self.root / "data/state.json").read_bytes(), state)
        self.context.set_content_type(int(IBus.InputPurpose.FREE_FORM), 0)
        pump()
        self.type("ka")
        self.assertEqual(self.marked, "か")

    def test_terminal_purpose_is_restored_on_return_to_normal_field(self):
        self.context.set_content_type(int(IBus.InputPurpose.TERMINAL), 0)
        pump()
        self.assertFalse(any(self.type("const x=42;")))
        self.context.set_content_type(int(IBus.InputPurpose.FREE_FORM), 0)
        pump()
        self.type("ka")
        self.assertEqual(self.marked, "か")

    def test_bracket_commit_moves_cursor_between_pair(self):
        self.caret = 0
        self.type("()")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "()")
        self.assertEqual(self.caret, 1)
        # IBus は配送時に FORWARD_MASK を付ける。
        self.assertEqual([(key, state & ~(1 << 25)) for key, state in self.forwarded],
                         [(IBus.KEY_Left, 0), (IBus.KEY_Left, int(IBus.ModifierType.RELEASE_MASK))])
        self.type("ka")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "(か)")

    def test_sync_client_bracket_commit_keeps_end_context(self):
        self.context.set_capabilities(int(IBus.Capabilite.PREEDIT_TEXT | IBus.Capabilite.FOCUS | IBus.Capabilite.SURROUNDING_TEXT) | (1 << 7))
        pump()
        self.caret = 0
        self.type("()")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "()")
        self.assertEqual(self.caret, 2)
        self.assertEqual(self.forwarded, [])
        self.type("ka")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "()か")

    def test_direct_detection_replaces_only_verified_prefix(self):
        self.key(IBus.KEY_Muhenkan)
        self.assertFalse(any(self.type("kon")))
        self.assertEqual(self.document, "kon")
        self.assertTrue(self.key(IBus.KEY_n, "n"))
        self.assertEqual(self.document, "")
        self.assertEqual(self.marked, "こん")
        self.assertEqual(self.deletions, [(-3, 3)])
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "こん")

    def test_issues_397_516_paste_then_small_tsu_preserves_document(self):
        for pasted in ["貼り付けた文章😀", "貼り付けた文章😀\n"]:
            for raw, expected in [("tta","った"),("tti","っち"),("ttu","っつ"),("tte","って"),("tto","っと")]:
                with self.subTest(pasted=pasted,raw=raw):
                    self.context.reset();pump()
                    self.document="";self.deletions.clear();self.selection=None;self.caret=None
                    self.assertFalse(self.key(IBus.KEY_v,state=int(IBus.ModifierType.CONTROL_MASK)))
                    self.document=pasted;self.surrounding()
                    self.type(raw);self.key(IBus.KEY_Return)
                    self.assertEqual(self.document,pasted+expected)
                    self.assertEqual(self.deletions,[])

    def test_nonempty_selection_disables_backward_replacement(self):
        self.key(IBus.KEY_Muhenkan)
        self.assertFalse(any(self.type("kon")))
        self.selection = (3, 0)
        self.key(IBus.KEY_n)
        self.assertEqual(self.deletions, [])
        self.assertEqual(self.document, "kon")
        self.assertEqual(self.marked, "")

    def test_issue_1022_terminal_can_switch_to_japanese_then_return_to_code(self):
        self.context.set_content_type(IBus.InputPurpose.TERMINAL, 0)
        pump()
        self.assertFalse(any(self.type("a")))
        self.assertTrue(self.key(IBus.KEY_Zenkaku_Hankaku))
        self.assertTrue(all(self.type("nihongo")))
        self.assertEqual(self.marked, "にほんご")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "aにほんご")
        self.assertFalse(self.key(IBus.KEY_Left))
        self.assertFalse(any(self.type("a")))
        self.assertEqual(self.marked, "")

    def test_jis_mode_keys_toggle_both_ways_and_commit_preedit_once(self):
        # JIS keyboards can report Kanji instead of Zenkaku_Hankaku.
        locks = int(IBus.ModifierType.LOCK_MASK | IBus.ModifierType.MOD2_MASK)
        keys = [(IBus.KEY_grave, 0), (IBus.KEY_Kanji, 0), (IBus.KEY_Kanji, int(IBus.ModifierType.MOD1_MASK)),
                (IBus.KEY_Zenkaku_Hankaku, 0), (IBus.KEY_Hankaku, 0), (IBus.KEY_Zenkaku, 0)]
        for key, modifiers in keys:
            with self.subTest(key=key, modifiers=modifiers):
                self.context.reset(); pump()
                self.document = ""
                self.assertTrue(self.key(IBus.KEY_Henkan))
                self.assertTrue(all(self.type("kana")))
                self.assertEqual(self.marked, "かな")
                self.assertTrue(self.key(key, state=locks | modifiers))
                self.assertEqual(self.document, "かな")
                self.assertEqual(self.marked, "")
                self.assertFalse(self.key(key, state=modifiers | int(IBus.ModifierType.RELEASE_MASK)))
                self.assertFalse(self.key(IBus.KEY_x, "x"))
                self.assertEqual(self.document, "かなx")
                self.assertFalse(self.key(IBus.KEY_space, " "))
                self.assertTrue(self.key(key, state=locks | modifiers))
                self.assertTrue(all(self.type("nihongo")))
                self.assertEqual(self.marked, "にほんご")
                self.key(IBus.KEY_Return)
                self.assertEqual(self.document, "かなx にほんご")

    def test_issue_1033_shift_space_uses_configured_switch_without_inserting_space(self):
        path = self.root / "data/config.json"
        original = path.read_bytes()
        self.addCleanup(path.write_bytes, original)
        settings = json.loads(original)
        settings["ToggleKeyboardShortcuts"] = "Shift+Space"
        path.write_text(json.dumps(settings), encoding="utf-8")
        self.assertTrue(all(self.type("kyou")))
        self.assertEqual(self.marked, "きょう")
        shift = int(IBus.ModifierType.SHIFT_MASK)
        self.assertTrue(self.key(IBus.KEY_space, state=shift))
        self.assertEqual(self.document, "きょう")
        self.assertEqual(self.marked, "")
        self.assertFalse(any(self.type("x")))
        self.assertTrue(self.key(IBus.KEY_space, state=shift))
        self.assertTrue(all(self.type("nihongo")))
        self.assertEqual(self.marked, "にほんご")

    def test_issue_1113_muhenkan_cycles_katakana_without_mode_change(self):
        path = self.root / "data/config.json"
        settings = json.loads(path.read_text(encoding="utf-8"))
        updated = dict(settings, MuhenkanKatakanaCycle=True)
        path.write_text(json.dumps(updated), encoding="utf-8")
        self.addCleanup(path.write_text, json.dumps(settings), encoding="utf-8")
        self.type("katakana")
        for expected in ["カタカナ", "ｶﾀｶﾅ", "かたかな"]:
            self.assertTrue(self.key(IBus.KEY_Muhenkan))
            self.assertEqual(self.marked, expected)
            self.assertEqual(self.document, "")
        self.key(IBus.KEY_Return)
        self.assertEqual(self.document, "かたかな")

    def test_issue_1016_page_down_up_keep_candidates_unconfirmed(self):
        self.type("merutaipu")
        self.key(IBus.KEY_space)
        self.assertGreater(len(self.candidates), 9)
        selected = self.selected_candidate
        self.assertTrue(self.key(IBus.KEY_Page_Down))
        self.assertEqual(self.document, "")
        self.assertEqual(self.selected_candidate, min(selected + 9, len(self.candidates) - 1))
        self.assertTrue(self.key(IBus.KEY_Page_Up))
        self.assertEqual(self.selected_candidate, selected)
        self.assertEqual(self.document, "")

    def test_issue_1107_control_enter_confirms_and_is_consumed(self):
        self.type("kyou")
        self.assertTrue(self.key(IBus.KEY_Return, state=int(IBus.ModifierType.CONTROL_MASK)))
        self.assertEqual(self.document, "きょう")
        self.assertEqual(self.marked, "")

    @classmethod
    def tearDownClass(cls):
        for name, file in [("engine", cls.engine_log), ("daemon", cls.daemon_log)]:
            file.flush()
            file.seek(0)
            logs = file.read()
            if logs.strip():
                print(name + " log:\n" + logs, file=sys.stderr)
            file.close()

if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]], verbosity=2, failfast=True)
