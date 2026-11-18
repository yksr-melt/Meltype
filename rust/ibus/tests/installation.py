#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""Install/remove only in temporary XDG roots; never change desktop IBus."""
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

class Installation(unittest.TestCase):
    binary = Path(sys.argv[1]).resolve()

    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="meltype-ibus-install-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.bundle = self.root / "bundle"
        self.bundle.mkdir()
        shutil.copy2(self.binary, self.bundle / "meltype-ibus")
        shutil.copy2(Path(__file__).resolve().parents[3] / "linux/icon.png", self.bundle / "icon.png")
        self.script = self.bundle / "install.sh"
        shutil.copy2(Path(__file__).resolve().parents[1] / "install.sh", self.script)
        for name in ["meltype-flash", "meltype-rs"]:
            (self.bundle / name).write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        self.data = self.root / "日本語 & quoted' path"
        self.target = self.data / "Meltype-flash/ibus"
        self.component = self.data / "ibus/component/meltype-flash.xml"
        self.desktop = self.data / "applications/ibus-setup-meltype-flash.desktop"
        self.env = {**os.environ, "XDG_DATA_HOME":str(self.data)}

    def run_script(self, *args, check=True):
        return subprocess.run(["bash", str(self.script), *args], env=self.env, text=True, capture_output=True, check=check)

    def test_keyboard_layout_selection_and_invalid_layout_preserve_installation(self):
        for layout in ["jp", "us", "default"]:
            self.run_script("--keyboard-layout", layout)
            component = ET.parse(self.component)
            self.assertEqual(component.findtext("engines/engine/layout"), layout)
            self.assertEqual(shlex.split(component.findtext("exec"))[-2:], ["--keyboard-layout", layout])
        original = self.component.read_bytes()
        self.assertNotEqual(self.run_script("--keyboard-layout", "bad", check=False).returncode, 0)
        self.assertEqual(self.component.read_bytes(), original)
        for args in [["--keyboard-layout"], ["--keyboard-layout", "bad"]]:
            result = subprocess.run([str(self.binary), "--print-component", *args], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertEqual(result.stdout, b"")

    def test_install_component_command_round_trips_paths_and_uninstall_preserves_user_data(self):
        self.run_script()
        component = ET.fromstring(self.component.read_text(encoding="utf-8"))
        self.assertEqual(component.findtext("name"), "org.freedesktop.IBus.MeltypeFlash")
        self.assertEqual(component.findtext("engines/engine/name"), "meltype-flash")
        self.assertEqual(component.findtext("engines/engine/icon"), str(self.target / "icon.png"))
        self.assertEqual((self.target / "icon.png").read_bytes(), (self.bundle / "icon.png").read_bytes())
        command = shlex.split(component.findtext("exec"))
        self.assertEqual(command, [str(self.target / "meltype-ibus"), "--ibus", "--keyboard-layout", "jp"])
        self.assertEqual(component.findtext("engines/engine/layout"), "jp")
        self.assertEqual(shlex.split(component.findtext("engines/engine/setup")),[str(self.target / "meltype-flash")])
        self.assertIn("X-Meltype-Flash-IBus=true",self.desktop.read_text(encoding="utf-8"))
        self.assertEqual(self.desktop.stat().st_mode & 0o777,0o644)
        version = subprocess.check_output([command[0], "--version"], text=True)
        self.assertIn("Meltype-FLASH IBus", version)
        self.run_script()  # Reinstall from the bundle is idempotent.
        settings = self.data / "Meltype-flash/config.json"
        settings.write_text('{"Enabled":true}', encoding="utf-8")
        other_component = self.component.with_name("meltype.xml")
        other_component.write_text("upstream component", encoding="utf-8")
        unrelated = self.target / "keep.txt"
        unrelated.write_text("keep", encoding="utf-8")
        self.run_script("--uninstall")
        self.assertFalse(self.component.exists())
        self.assertFalse(self.desktop.exists())
        self.assertFalse((self.target / "meltype-ibus").exists())
        self.assertFalse((self.target / "icon.png").exists())
        self.assertEqual(settings.read_text(), '{"Enabled":true}')
        self.assertEqual(other_component.read_text(), "upstream component")
        self.assertEqual(unrelated.read_text(), "keep")

    def test_install_refuses_symlinks_and_remove_refuses_foreign_component(self):
        outside = self.root / "outside"
        outside.mkdir()
        self.target.parent.mkdir(parents=True)
        self.target.symlink_to(outside, target_is_directory=True)
        self.assertNotEqual(self.run_script(check=False).returncode, 0)
        self.assertEqual(list(outside.iterdir()), [])
        self.target.unlink()
        self.run_script()
        self.component.write_text("<component><name>another.application</name></component>", encoding="utf-8")
        self.assertNotEqual(self.run_script("--uninstall", check=False).returncode, 0)
        self.assertTrue(self.component.exists())
        self.assertTrue((self.target / "meltype-ibus").exists())

    def test_every_owned_leaf_and_ancestor_symlink_is_rejected(self):
        self.target.mkdir(parents=True)
        outside = self.root / "outside.txt"
        outside.write_text("untouched", encoding="utf-8")
        for name in ["meltype-ibus", "meltype-flash", "meltype-rs", "LICENSE", "THIRD-PARTY-NOTICES.md",
                     "README.md", "MIGRATION.md", "install.sh", "icon.png", ".meltype-flash-ibus",
                     "mozc/meltype_mozc_helper", "mozc/MOZC-LICENSE.txt", "mozc/MOZC-CREDITS.html"]:
            with self.subTest(name=name):
                leaf = self.target / name
                leaf.parent.mkdir(parents=True, exist_ok=True)
                leaf.symlink_to(outside)
                self.assertNotEqual(self.run_script(check=False).returncode, 0)
                self.assertEqual(outside.read_text(), "untouched")
                leaf.unlink()
        self.component.parent.mkdir(parents=True)
        self.component.symlink_to(outside)
        self.assertNotEqual(self.run_script(check=False).returncode, 0)
        self.component.unlink()
        # target 自体ではなく、その親がリンクでも拒否する。
        other = self.root / "other"
        other.mkdir()
        linked = self.root / "linked"
        linked.symlink_to(other, target_is_directory=True)
        self.env["XDG_DATA_HOME"] = str(linked / "data")
        self.assertNotEqual(self.run_script(check=False).returncode, 0)
        self.assertEqual(list(other.iterdir()), [])

    def test_running_elf_is_replaced_without_truncating_it(self):
        self.run_script()
        executable = self.target / "meltype-flash"
        shutil.copy2("/bin/dash", executable)
        process = subprocess.Popen([str(executable), "-c", "sleep 30; :"])
        self.addCleanup(lambda: (process.terminate(), process.wait()) if process.poll() is None else None)
        self.run_script()
        self.assertIsNone(process.poll())
        self.assertEqual(executable.read_bytes(), (self.bundle / "meltype-flash").read_bytes())
        self.assertEqual(self.component.stat().st_mode & 0o777, 0o644)

    def test_invalid_new_binary_and_failed_publication_preserve_old_installation(self):
        self.run_script()
        originals = {p:p.read_bytes() for p in self.target.rglob("*") if p.is_file()}
        xml = self.component.read_bytes()
        engine = self.bundle / "meltype-ibus"
        valid = engine.read_bytes()
        engine.write_text("#!/bin/sh\nexit 1\n", encoding="utf-8")
        self.assertNotEqual(self.run_script(check=False).returncode, 0)
        engine.write_bytes(valid)
        # 2 個目の公開時に失敗させ、既に置き換えたファイルも旧版へ戻ること。
        (self.bundle / "meltype-flash").write_text("new version", encoding="utf-8")
        wrappers = self.root / "wrappers"
        wrappers.mkdir()
        mv = wrappers / "mv"
        mv.write_text('#!/bin/bash\ncase "$*" in *stage.*/new/meltype-rs*) exit 1 ;; esac\nexec /bin/mv "$@"\n', encoding="utf-8")
        mv.chmod(0o755)
        self.env["PATH"] = str(wrappers) + os.pathsep + os.environ["PATH"]
        self.assertNotEqual(self.run_script(check=False).returncode, 0)
        for path, contents in originals.items():
            self.assertEqual(path.read_bytes(), contents, str(path))
        self.assertEqual(self.component.read_bytes(), xml)
        self.assertEqual(list(self.target.glob(".meltype-flash-stage.*")), [])

    def test_failed_restore_keeps_backup_for_recovery(self):
        self.run_script()
        old = (self.target / "meltype-flash").read_bytes()
        (self.bundle / "meltype-flash").write_text("new version", encoding="utf-8")
        wrappers = self.root / "wrappers"
        wrappers.mkdir()
        mv = wrappers / "mv"
        mv.write_text('#!/bin/bash\ncase "$*" in *stage.*/new/meltype-rs*|*stage.*/old/meltype-flash*) exit 1 ;; esac\nexec /bin/mv "$@"\n', encoding="utf-8")
        mv.chmod(0o755)
        self.env["PATH"] = str(wrappers) + os.pathsep + os.environ["PATH"]
        result = self.run_script(check=False)
        self.assertNotEqual(result.returncode, 0)
        backups = list(self.target.glob(".meltype-flash-stage.*/old/meltype-flash"))
        self.assertEqual(len(backups), 1)
        self.assertEqual(backups[0].read_bytes(), old)
        self.assertIn("retained for recovery", result.stderr)

    def test_setup_launcher_refuses_foreign_files_and_rolls_back_with_component(self):
        self.run_script()
        previous=self.desktop.read_bytes()
        self.desktop.write_text("foreign desktop",encoding="utf-8")
        self.assertNotEqual(self.run_script(check=False).returncode,0)
        self.assertEqual(self.desktop.read_text(),"foreign desktop")
        self.desktop.write_bytes(previous)
        (self.bundle / "meltype-flash").write_text("new version",encoding="utf-8")
        wrappers=self.root / "wrappers";wrappers.mkdir()
        mv=wrappers / "mv"
        mv.write_text('#!/bin/bash\ncase "$*" in *.meltype-flash.*meltype-flash.xml*) exit 1 ;; esac\nexec /bin/mv "$@"\n',encoding="utf-8");mv.chmod(0o755)
        self.env["PATH"]=str(wrappers)+os.pathsep+os.environ["PATH"]
        self.assertNotEqual(self.run_script(check=False).returncode,0)
        self.assertEqual(self.desktop.read_bytes(),previous)
        self.assertEqual((self.target / "meltype-flash").read_text(),"#!/bin/sh\nexit 0\n")

    def test_gnome_setup_desktop_launches_final_path_with_reserved_characters(self):
        # GNOMEと同じDesktopAppInfoのID検索で起動し、シェル展開されないことも確かめる。
        self.env["XDG_DATA_HOME"]=str(self.root / '日本語 % \" $ ` \\ path')
        data=Path(self.env["XDG_DATA_HOME"])
        target=data / "Meltype-flash/ibus"
        marker=self.root / "settings-launched"
        (self.bundle / "meltype-flash").write_text('#!/bin/sh\nprintf opened > '+shlex.quote(str(marker))+'\n',encoding="utf-8")
        self.run_script()
        desktop=data / "applications/ibus-setup-meltype-flash.desktop"
        self.assertTrue(desktop.exists())
        subprocess.run([sys.executable,"-c",
            "from gi.repository import Gio; app=Gio.DesktopAppInfo.new('ibus-setup-meltype-flash.desktop'); assert app is not None; assert app.launch([],None)"],
            env=self.env,check=True,capture_output=True)
        import time
        deadline=time.monotonic()+3
        while not marker.exists() and time.monotonic()<deadline:time.sleep(0.01)
        self.assertEqual(marker.read_text(),"opened")
        self.assertTrue((target / "meltype-flash").exists())

    def test_system_prefix_registers_and_removes_only_owned_files(self):
        prefix = self.root / "system prefix"
        mozc = self.bundle / "mozc"
        mozc.mkdir()
        (mozc / "meltype_mozc_helper").write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        (mozc / "MOZC-LICENSE.txt").write_text("test license", encoding="utf-8")
        # Artifact downloads do not preserve executable permissions.
        (mozc / "meltype_mozc_helper").chmod(0o644)
        self.run_script("--system", "--prefix", str(prefix))
        component = prefix / "share/ibus/component/meltype-flash.xml"
        binary = prefix / "libexec/meltype-flash/ibus/meltype-ibus"
        command = shlex.split(ET.parse(component).findtext("exec"))
        self.assertEqual(command, [str(binary), "--ibus", "--keyboard-layout", "jp"])
        self.assertIn("Meltype-FLASH IBus", subprocess.check_output([str(binary), "--version"], text=True))
        helper = binary.parent / "mozc/meltype_mozc_helper"
        self.assertTrue(os.access(helper, os.X_OK))
        self.assertEqual((helper.parent / "MOZC-LICENSE.txt").read_text(), "test license")
        other = component.with_name("meltype.xml")
        other.write_text("upstream", encoding="utf-8")
        subprocess.run(["bash", str(binary.with_name("install.sh")), "--system", "--prefix", str(prefix), "--uninstall"], env=self.env, check=True, capture_output=True)
        self.assertFalse(component.exists())
        self.assertFalse(binary.exists())
        self.assertFalse(helper.exists())
        self.assertEqual(other.read_text(), "upstream")

if __name__ == "__main__":
    unittest.main(argv=[sys.argv[0]], verbosity=2)
