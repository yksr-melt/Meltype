# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro, appipinopi
"""Compare native dispatch with former frontend order, using independent cores."""
import ctypes as c
import json
from pathlib import Path
import sys
import tempfile

api = c.CDLL(str(Path(sys.argv[1]).resolve()))
P = c.c_void_p
U = c.c_uint32
I = c.c_int
class Context(c.Structure):
    _fields_ = [("before", P), ("before_length", U), ("after", P), ("after_length", U)]
Query = c.CFUNCTYPE(I, P, U, c.POINTER(Context))
Event = c.CFUNCTYPE(I, P, U, I, P, U)
def bind(name, result, arguments):
    fn = getattr(api, "meltype_" + name)
    fn.restype, fn.argtypes = result, arguments
    return fn
create = bind("fcitx_create_at", P, [c.c_char_p])
destroy = bind("fcitx_destroy", None, [P])
state = bind("fcitx_state", I, [P])
direct = bind("fcitx_set_direct", None, [P, I])
visible = bind("fcitx_set_preedit", None, [P, I])
capability = bind("set_can_delete", None, [P, I])
key = bind("handle_key", P, [P, I, I, I, c.c_char_p, c.c_char_p])
role = bind("role_key", P, [P, I])
commit = bind("commit", P, [P])
select = bind("select_candidate", P, [P, I])
free = bind("free", None, [P])
map_key = bind("fcitx_key", c.c_uint64, [U, U])
mode = bind("fcitx_mode", U, [U, I])
modifiers = bind("fcitx_modifiers", I, [U])
delete = bind("fcitx_delete_count", I, [c.c_char_p, U, U, c.c_char_p, U, I])
apply = bind("fcitx_apply_utf8", I, [P, c.c_char_p, U, P, Event])
dispatch = bind("fcitx_key_event", I, [P, U, U, U, I, P, Query, Event])
action = bind("fcitx_action", I, [P, U, I, P, Query, Event])
def take(pointer):
    if not pointer:
        return None
    try:
        return c.string_at(pointer)
    finally:
        free(pointer)
class Host:
    def __init__(self, handle):
        self.handle = handle
        self.text, self.cursor, self.capable = "前😀後", 2, True
        self.trace, self.errors, self.buffers = [], [], []
        self.destroy_query = None
        self.fail_query = None
        self.query_cb, self.event_cb = Query(self.query), Event(self.event)
    def query(self, _, kind, output):
        try:
            self.trace.append(("query", kind))
            if self.fail_query == kind:
                return -1
            if self.destroy_query == kind:
                destroy(self.handle)
                return 1
            if kind == 0:
                return int(self.capable)
            if not self.capable or not self.text:
                return 0
            before = self.text[max(0, self.cursor - 20):self.cursor].encode()
            after = self.text[self.cursor:self.cursor + 20].encode()
            self.buffers = [c.create_string_buffer(before), c.create_string_buffer(after)]
            output[0] = Context(c.cast(self.buffers[0], P), len(before), c.cast(self.buffers[1], P), len(after))
            return 1
        except BaseException as error:
            self.errors.append(error)
            return -1
    def event(self, _, op, argument, pointer, length):
        try:
            text = c.string_at(pointer, length).decode()
            self.trace.append((op, argument, text))
            if op == 9:
                surrounding, expected = self.text.encode(), text.encode()
                return delete(surrounding, len(surrounding), self.cursor, expected, len(expected), argument) if self.capable else 0
            if op == 10:
                self.text = self.text[:self.cursor - argument] + self.text[self.cursor:]
                self.cursor -= argument
            if op in (11, 15):
                self.text = self.text[:self.cursor] + text + self.text[self.cursor:]
                self.cursor += len(text) - (argument if op == 15 else 0)
            if op in (2, 8, 12):
                visible(self.handle, argument if op == 2 else 0)
            return 1
        except BaseException as error:
            self.errors.append(error)
            return -1
    def apply(self, reply):
        if reply:
            assert apply(self.handle, reply, len(reply), None, self.event_cb)

def reference(host, symbol, scalar, flags, release):
    # Former keyEvent() sequence, using the independently validated key/mode ABI.
    if release or state(host.handle) < 0:
        return 0
    capability(host.handle, host.query(None, 0, c.pointer(Context())))
    plan = mode(symbol, state(host.handle) & 1)
    if plan >> 8:
        reply = take(role(host.handle, (plan >> 8) & 255))
        if reply:
            host.apply(reply)
            return 1
    if plan & 1:
        host.apply(take(commit(host.handle)))
        direct(host.handle, (plan >> 1) & 1)
        label = b"A" if plan & 2 else "あ".encode()
        host.event(None, 14, 0, c.cast(c.c_char_p(label), P), len(label))
        return 1
    packed = map_key(symbol, scalar)
    vk = packed & 0xffffffff
    if not vk:
        return 0
    surrounding = Context()
    available = not state(host.handle) & 2 and host.query(None, 1, c.pointer(surrounding)) > 0
    before = c.string_at(surrounding.before) if available else None
    after = c.string_at(surrounding.after) if available else None
    reply = take(key(host.handle, vk, packed >> 32, modifiers(flags), before, after))
    if not reply:
        return 0
    host.apply(reply)
    return int(json.loads(reply).get("consumed", False))

with tempfile.TemporaryDirectory(prefix="meltype-fcitx-event-") as temporary:
    hosts = []
    for name in ("native", "reference"):
        root = Path(temporary) / name
        root.mkdir()
        (root / "config.json").write_text('{"DetectionLevel":"Manual"}', encoding="utf-8")
        hosts.append(Host(create(str(root).encode())))
    actual, expected = hosts
    count = 0
    try:
        events = [(ord(letter), ord(letter), 0, 0) for letter in "nihongo"]
        events += [(0xff23, 0, 0, 0), (0xff0d, 0, 0, 0), (0xff2a, 0, 0, 0), (0xff21, 0, 0, 0), (0xff21, 0, 0, 0)]
        events += [(ord("a"), ord("a"), 0, 0), (0xff27, 0, 0, 0)]
        events += [(symbol, scalar, flags, release) for symbol, scalar in [(ord("k"), ord("k")), (0xff08, 0), (0xff09, 0), (0xff22, 0), (0xff21, 0), (0xff25, 0), (0x0101f600, 0x1f600), (0, 0)] for flags in [0, 1, 4, 8, 64, 128] for release in [0, 1]]
        for number, event in enumerate(events):
            for host in hosts:
                host.trace.clear()
                host.capable = number % 3 != 0
            want = reference(expected, *event)
            got = dispatch(actual.handle, *event, None, actual.query_cb, actual.event_cb)
            assert got == want, (number, event, got, want)
            assert actual.trace == expected.trace, (number, event, actual.trace, expected.trace)
            assert (actual.text, actual.cursor, state(actual.handle)) == (expected.text, expected.cursor, state(expected.handle))
            assert not actual.errors and not expected.errors, (actual.errors, expected.errors)
            count += 1
        # Escの下がUS配列のgraveとして届いても、記号を挿入せず往復できる。
        regression_root = Path(temporary) / "grave"
        regression_root.mkdir()
        (regression_root / "config.json").write_text('{"DetectionLevel":"Manual"}', encoding="utf-8")
        regression = Host(create(str(regression_root).encode()))
        hosts.append(regression)
        direct(regression.handle, 0)
        action(regression.handle, 3, 0, None, regression.query_cb, regression.event_cb)
        regression.text, regression.cursor = "", 0
        for letter in "kana":
            assert dispatch(regression.handle, ord(letter), ord(letter), 0, 0, None, regression.query_cb, regression.event_cb)
        assert dispatch(regression.handle, 0x60, 0x60, 2 | 16, 0, None, regression.query_cb, regression.event_cb)
        assert regression.text == "かな", regression.text
        assert state(regression.handle) & 1
        assert not dispatch(regression.handle, 0x60, 0x60, 0, 1, None, regression.query_cb, regression.event_cb)
        assert dispatch(regression.handle, 0x60, 0x60, 0, 0, None, regression.query_cb, regression.event_cb)
        assert not state(regression.handle) & 1
        assert regression.text == "かな"
        for kind, index in [(1, 0), (1, -1), (0, 0), (1, 1), (0, 0)]:
            for host in hosts:
                host.trace.clear()
            capability(expected.handle, expected.query(None, 0, c.pointer(Context())))
            expected.apply(take(commit(expected.handle) if kind == 0 else select(expected.handle, index)))
            assert action(actual.handle, kind, index, None, actual.query_cb, actual.event_cb)
            assert actual.trace == expected.trace
            count += 1
        actual.trace.clear()
        assert not action(actual.handle, 99, 0, None, actual.query_cb, actual.event_cb)
        assert not actual.trace
        for failure in (0, 1):
            actual.trace.clear()
            actual.fail_query = failure
            visible(actual.handle, 0)
            assert not dispatch(actual.handle, ord("a"), ord("a"), 0, 0, None, actual.query_cb, actual.event_cb)
            assert actual.trace == [("query", n) for n in range(failure + 1)], "failed query continued to edit"
        actual.fail_query = None
        actual.trace.clear()
        actual.destroy_query = 0
        assert not dispatch(actual.handle, ord("a"), ord("a"), 0, 0, None, actual.query_cb, actual.event_cb)
        assert actual.trace == [("query", 0)], "destroyed context continued to edit"
        actual.trace.clear()
        assert not dispatch(actual.handle, ord("a"), ord("a"), 0, 0, None, actual.query_cb, actual.event_cb)
        assert not actual.trace, "stale handle queried host"
        replacement = Host(create(str(Path(temporary) / "native").encode()))
        hosts.append(replacement)
        replacement.destroy_query = 1
        assert not dispatch(replacement.handle, ord("a"), ord("a"), 0, 0, None, replacement.query_cb, replacement.event_cb)
        assert replacement.trace == [("query", 0), ("query", 1)], "getter destroyed context continued to edit"
    finally:
        for host in hosts:
            destroy(host.handle)
print(f"fcitx Rust events: {count} legacy/core key/action sequences, query ordering, modes, releases, modifiers, Unicode, visibility and destruction passed")

# 修正した動作は旧版比較とは独立して検証する。
control = bind("control", P, [P, c.c_char_p])
caret_capability = bind("set_can_move_caret", None, [P, I])
with tempfile.TemporaryDirectory(prefix="meltype-fcitx-fixes-") as temporary:
    profile = Path(temporary)
    (profile / "config.json").write_text(json.dumps({"LiveConversion":False,"PredictiveCandidates":True,"DetectionLevel":"Manual",
        "CursorIntoBrackets":True,"FullWidthParentheses":False}),encoding="utf-8")
    host = Host(create(str(profile).encode()))
    host.text, host.cursor = "", 0
    def press(symbol):
        return dispatch(host.handle, symbol, symbol if symbol < 128 else 0, 0, 0, None, host.query_cb, host.event_cb)
    def act(kind, index=0):
        return action(host.handle, kind, index, None, host.query_cb, host.event_cb)
    try:
        take(control(host.handle, json.dumps({"op":"dictionary_set","reading":"めるたいぷ","word":"Meltype"}).encode()))
        for ch in "meru": press(ord(ch))
        assert any(len(event) == 3 and event[0] == 5 and event[2] == "Meltype" for event in host.trace), host.trace
        assert act(1)
        assert host.text == "Meltype", host.text
        # フォーカス移動: 表示は既にホストが確定済みとして通知する。
        for ch in "nihon": press(ord(ch))
        before = host.text
        assert act(2)
        assert host.text == before and not state(host.handle) & 2
        assert act(2) and host.text == before
        # reset は挿入しない。server preedit は表示どおりに一度だけ確定する。
        for ch in "ka": press(ord(ch))
        assert act(3) and host.text == before
        for ch in "nihon": press(ord(ch))
        assert act(4) and host.text == before + "にほn"
        # Shift 状態のない ISO_Left_Tab でも逆順に候補を移動する。
        settings = json.loads((profile / "config.json").read_text())
        settings["PredictiveCandidates"] = False
        (profile / "config.json").write_text(json.dumps(settings), encoding="utf-8")
        host.text, host.cursor = "。", 1
        for ch in "hashi": press(ord(ch))
        # Core は Space で始めた変換中の Tab を確定・通過として扱う。
        # 逆順選択は Tab で始めた変換を使って検査する。
        for _ in range(3):
            host.trace.clear()
            press(0xff09)
            if any(event[0] == 4 for event in host.trace):
                break
        host.trace.clear()
        assert press(0xfe20), host.trace
        reverse = [event[1] for event in host.trace if event[0] == 6]
        items = [event[2] for event in host.trace if event[0] == 5]
        assert items and reverse == [len(items)-1], (reverse, items)
        assert act(3)
        # 対応クライアントでは括弧の間へカーソルを移す。
        caret_capability(host.handle, 1)
        for ch in "()": press(ord(ch))
        assert act(0)
        assert host.text.endswith("()") and host.cursor == len(host.text)-1, (host.text, host.cursor)
        assert not host.errors, host.errors
    finally:
        destroy(host.handle)
print("fcitx fixes: prediction click, focus acknowledgement, cancel, server preedit, ISO_Left_Tab and bracket caret passed")
