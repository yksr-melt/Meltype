#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 0924haruto12
"""Exercise product InputController.handle with NSEvent and a synthetic IMKTextInput client.

The IME is not installed or registered. --real-converter additionally builds and
runs the pinned azooKey engine and its bundled dictionary. All data is temporary.
"""
import argparse
import json
import os
import plistlib
import re
import shutil
import subprocess
import tempfile
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('library', type=Path)
    parser.add_argument('--real-converter', action='store_true')
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
        if args.real_converter:
            package = root / 'package'
            target = package / 'Sources/EventTests'
            target.mkdir(parents=True)
            shutil.copy2(repo / 'tools/test-mac-events.swift', target / 'EventTests.swift')
            for name in filenames + ['Converter.swift']:
                shutil.copy2(sources / name, target / name)
            manifest = (repo / 'mac/Package.swift').read_text()
            dependency = re.search(r'\.package\(url: "([^"]+)", revision: "([^"]+)"\)', manifest)
            if not dependency:
                raise RuntimeError('Missing pinned converter dependency')
            (package / 'Package.swift').write_text('''// swift-tools-version:5.10
import PackageDescription
let package = Package(name: "EventTests", platforms: [.macOS(.v13)], dependencies: [
.package(url: %s, revision: %s)], targets: [.executableTarget(name: "EventTests", dependencies: [
.product(name: "KanaKanjiConverterModuleWithDefaultDictionary", package: "AzooKeyKanaKanjiConverter")],
swiftSettings: [.define("REAL_CONVERTER")], linkerSettings: [.linkedFramework("InputMethodKit"), .linkedFramework("Carbon")])], swiftLanguageVersions: [.v5])
''' % (json.dumps(dependency[1]), json.dumps(dependency[2])))
            subprocess.run(['swift', 'build', '--package-path', str(package), '-c', 'release'], check=True)
            binary_directory = Path(subprocess.check_output([
                'swift', 'build', '--package-path', str(package), '-c', 'release', '--show-bin-path'], text=True).strip())
            shutil.copy2(binary_directory / 'EventTests', executable)
            for bundle in binary_directory.glob('*.bundle'):
                shutil.copytree(bundle, resources / bundle.name)
            for framework in binary_directory.glob('*.framework'):
                shutil.copytree(framework, frameworks / framework.name, symlinks=True)
            # The engine's framework and older-macOS Swift compatibility dylibs use @rpath.
            dependencies = subprocess.check_output(['otool', '-L', str(executable)], text=True)
            toolchain = Path(subprocess.check_output(['xcrun', '--find', 'swift'], text=True).strip()).parent.parent / 'lib'
            for line in dependencies.splitlines()[1:]:
                path = line.strip().split(' ')[0]
                if not path.startswith('@rpath/libswift'):
                    continue
                name = path.removeprefix('@rpath/')
                matches = list(toolchain.glob('swift-*/macosx/' + name)) + list(toolchain.glob('swift/macosx/' + name))
                if matches:
                    shutil.copy2(matches[0], frameworks / name)
            subprocess.run(['install_name_tool', '-add_rpath', '@executable_path/../Frameworks', str(executable)], check=True)
            subprocess.run(['codesign', '--force', '--deep', '--sign', '-', str(contents.parent)], check=True)
        else:
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
