#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""xvfb-run -a dbus-run-session -- python3 rust/ibus/tests/gtk_fields.py <meltype-ibus>"""
import os
import subprocess
import sys
from unittest import TestCase, main

from integration import EngineTests as IsolatedBus, pump, wait

# Reuse the isolated daemon fixture, without running its direct D-Bus client tests.
class GtkFields(TestCase):
    def test_search_and_multiline_commit_and_toggle(self):
        import gi
        os.environ["GDK_BACKEND"] = "x11"
        os.environ["GTK_IM_MODULE"] = "ibus"
        self.addCleanup(IsolatedBus.doClassCleanups)
        IsolatedBus.setUpClass()
        self.addCleanup(IsolatedBus.tearDownClass)
        gi.require_version("Gtk", "3.0")
        gi.require_version("GdkX11", "3.0")
        from gi.repository import Gtk, GdkX11

        Gtk.init([])
        window = Gtk.Window()
        self.addCleanup(window.destroy)
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        search = Gtk.SearchEntry()
        chat = Gtk.TextView()
        box.add(search)
        box.add(chat)
        window.add(box)
        window.show_all()
        window.present()
        pump()
        xid = GdkX11.X11Window.get_xid(window.get_window())
        subprocess.run(["xdotool", "windowfocus", "--sync", str(xid)], check=True)
        self.assertTrue(IsolatedBus.bus.set_global_engine("meltype-flash"))

        def keys(*names):
            proc = subprocess.Popen(["xdotool", "key", "--delay", "100", *names])
            wait(lambda: proc.poll() is not None, "keyboard events")
            self.assertEqual(proc.wait(), 0)
            pump(0.2)

        buf = chat.get_buffer()
        for field, text in [(search, search.get_text), (chat, lambda: buf.get_text(*buf.get_bounds(), True))]:
            field.grab_focus()
            pump(0.2)
            self.assertTrue(field.has_focus(), type(field).__name__)
            keys(*"meetingwo")
            keys("Return")
            wait(lambda: bool(text()), "commit in "+type(field).__name__, timeout=15)
            self.assertEqual(text(), "meetingを")
            keys("Zenkaku_Hankaku", "2")
            wait(lambda: text().endswith("2"), "direct digit")
            self.assertEqual(text(), "meetingを2")
            keys("Zenkaku_Hankaku", "k", "a", "Return")
            wait(lambda: text().endswith("か"), "Japanese commit")
            self.assertEqual(text(), "meetingを2か")

if __name__ == "__main__":
    main(argv=[sys.argv[0]], defaultTest="GtkFields", verbosity=2)
