#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 0924haruto12
"""Swift の実際のラッパーを NativeAOT と接続して検証する。IME はインストールしない。

python3 tools/test-mac-boundaries.py path/to/MeltypeNative.dylib
"""

import argparse
import json
import os
import plistlib
import shutil
import subprocess
import tempfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("library", type=Path)
    args = parser.parse_args()
    library = args.library.resolve(strict=True)
    repo = Path(__file__).resolve().parent.parent
    with tempfile.TemporaryDirectory(prefix="meltype-swift-boundaries-") as temporary:
        root = Path(temporary)
        contents = root / "BoundaryTests.app" / "Contents"
        frameworks = contents / "Frameworks"
        executable = contents / "MacOS" / "BoundaryTests"
        frameworks.mkdir(parents=True)
        executable.parent.mkdir()
        shutil.copy2(library, frameworks / "libMeltypeNative.dylib")
        (contents / "Info.plist").write_bytes(plistlib.dumps({"CFBundleExecutable": executable.name,
                                                            "CFBundlePackageType": "APPL"}))
        subprocess.run(["xcrun", "swiftc", "-swift-version", "5",
                        str(repo / "tools/test-mac-boundaries.swift"),
                        *[str(repo / "mac/Sources/MeltypeIME" / name)
                          for name in ("NativeCore.swift", "InputController.swift", "KeyMapping.swift",
                                       "MacUserDictionary.swift", "ModeShortcuts.swift", "ModeIndicator.swift",
                                       "ModeServices.swift", "InputSourceRegistration.swift",
                                       "ImportDialogPresentation.swift")],
                        "-framework", "InputMethodKit", "-framework", "Carbon", "-o", str(executable)], check=True)
        data = root / "data"
        data.mkdir()
        (data / "config.json").write_text(json.dumps({"SpaceAroundEnglish": True, "SigilWordsDirect": False}), encoding="utf-8")
        env = os.environ.copy()
        env["MELTYPE_DATA_DIR"] = str(data)
        subprocess.run([str(executable)], env=env, check=True)


if __name__ == "__main__":
    main()
