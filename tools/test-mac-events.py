#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 0924haruto12
"""Exercise product InputController.handle with NSEvent and a synthetic IMKTextInput client.

The IME is not installed or registered, and no conversion helper is bundled (kana stays kana).
All data is temporary.
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
    parser.add_argument('library', type=Path)
    args = parser.parse_args()
    library = args.library.resolve(strict=True)
    repo = Path(__file__).resolve().parent.parent
    sources = repo / 'mac/Sources/MeltypeIME'
    with tempfile.TemporaryDirectory(prefix='meltype-events-') as temporary:
        root = Path(temporary)
        contents = root / 'EventTests.app/Contents'
        frameworks = contents / 'Frameworks'
        executable = contents / 'MacOS/EventTests'
        frameworks.mkdir(parents=True)
        executable.parent.mkdir()
        resources = contents / 'Resources'
        resources.mkdir()
        shutil.copy2(library, frameworks / 'libMeltypeNative.dylib')
        (contents / 'Info.plist').write_bytes(plistlib.dumps({
            'CFBundleExecutable': executable.name, 'CFBundleIdentifier': 'local.meltype.EventTests',
            'CFBundlePackageType': 'APPL'}))
        filenames = [
            'NativeCore.swift', 'InputController.swift', 'KeyMapping.swift', 'MacUserDictionary.swift',
            'ModeShortcuts.swift', 'ModeIndicator.swift', 'ModeServices.swift', 'InputSourceRegistration.swift',
            'ImportDialogPresentation.swift',
        ]
        subprocess.run(['xcrun', 'swiftc', '-swift-version', '5',
                        str(repo / 'tools/test-mac-events.swift'),
                        *[str(sources / name) for name in filenames],
                        '-framework', 'InputMethodKit', '-framework', 'Carbon', '-o', str(executable)], check=True)
        data = root / 'data'
        data.mkdir()
        (data / 'config.json').write_text(json.dumps({'SpaceAroundEnglish': True, 'LiveConversion': False, 'SigilWordsDirect': False}))
        env = os.environ.copy()
        env['MELTYPE_DATA_DIR'] = str(data)
        subprocess.run([str(executable)], env=env, check=True)


if __name__ == '__main__':
    main()
