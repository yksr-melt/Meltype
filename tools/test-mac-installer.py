#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 joyamazaki
"""Run the real installer in a fixture; registration, app startup and process control are stubs."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parent.parent
LSREGISTER = '/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister'


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='meltype-installer-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.package = self.root / 'package'
        self.package.mkdir()
        self.target = self.root / 'installed'
        self.target.mkdir()
        self.log = self.root / 'calls'
        self.bin = self.root / 'bin'
        self.bin.mkdir()
        self.environment = os.environ.copy()
        self.environment['PATH'] = str(self.bin) + ':' + self.environment['PATH']
        self.environment['MELTYPE_TEST_LOG'] = str(self.log)
        self.environment['MELTYPE_TEST_FAIL_REGISTER'] = '0'
        for name in ['pkill', 'codesign', 'xattr']:
            self.executable(self.bin / name, 'exit 0')
        self.executable(self.bin / 'lsregister', 'echo "ls:$*" >> "$MELTYPE_TEST_LOG"')
        self.executable(self.bin / 'swift', 'echo selected >> "$MELTYPE_TEST_LOG"')
        for name in ['install.sh', 'install-app.sh']:
            text = (REPO / 'mac' / name).read_text().replace(LSREGISTER, 'lsregister')
            text = text.replace('$HOME/Library/Input Methods', str(self.target))
            text = text.replace('${TMPDIR:-/tmp}', str(self.root))
            (self.package / name).write_text(text)
        self.executable(self.package / 'start-input-method.sh', 'echo started >> "$MELTYPE_TEST_LOG"')
        (self.package / 'select-input-source.swift').write_text('// fixture')
        self.app = self.package / 'Meltype.app'
        self.executable(self.app / 'Contents/MacOS/Meltype', 'echo "app:$*" >> "$MELTYPE_TEST_LOG"')
        self.executable(self.app / 'Contents/MacOS/MeltypeRegisterInputSource',
                        'echo register >> "$MELTYPE_TEST_LOG"; exit "$MELTYPE_TEST_FAIL_REGISTER"')
        (self.app / 'version').write_text('new')
        previous = self.target / 'Meltype.app'
        previous.mkdir()
        (previous / 'version').write_text('previous')

    @staticmethod
    def executable(path, body):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('#!/bin/bash\nset -e\n' + body + '\n')
        path.chmod(0o755)

    def compress(self):
        subprocess.run(['ditto', '-c', '-k', '--keepParent', str(self.app),
                        str(self.package / 'Meltype.app.zip')], check=True)
        shutil.rmtree(self.app)

    def run_install(self):
        result = subprocess.run(['bash', str(self.package / 'install.sh')], env=self.environment,
                                capture_output=True, text=True)
        self.assertFalse(list(self.root.glob('.meltype-payload.*')), 'temporary payload leaked')
        return result

    def calls(self):
        return self.log.read_text().splitlines() if self.log.exists() else []

    def test_compressed_payload_repeated_updates_register_once_each(self):
        self.compress()
        for _ in range(2):
            result = self.run_install()
            self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.calls().count('register'), 2)
        self.assertEqual(self.calls().count('started'), 2)
        self.assertEqual(self.calls().count('selected'), 2)
        self.assertFalse(any(x.startswith('app:') for x in self.calls()), 'second registration path ran')
        self.assertFalse(self.app.exists(), 'download folder must keep payload compressed')
        self.assertEqual((self.target / 'Meltype.app/version').read_text(), 'new')

    def test_legacy_package_uses_helper_once(self):
        result = self.run_install()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.calls().count('register'), 1)
        self.assertFalse(any(x.startswith('app:') for x in self.calls()))

    def test_old_bundle_without_helper_falls_back_once(self):
        (self.app / 'Contents/MacOS/MeltypeRegisterInputSource').unlink()
        result = self.run_install()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.calls().count('app:--register-input-source'), 1)

    def test_preflight_failure_keeps_previous_app(self):
        self.compress()
        (self.package / 'start-input-method.sh').unlink()
        self.assertNotEqual(self.run_install().returncode, 0)
        self.assertEqual((self.target / 'Meltype.app/version').read_text(), 'previous')
        self.assertEqual(self.calls(), [])

    def test_corrupt_archive_keeps_previous_app(self):
        shutil.rmtree(self.app)
        (self.package / 'Meltype.app.zip').write_text('invalid zip')
        self.assertNotEqual(self.run_install().returncode, 0)
        self.assertEqual((self.target / 'Meltype.app/version').read_text(), 'previous')
        self.assertFalse('register' in self.calls())

    def test_distribution_contains_only_archived_app(self):
        repo = self.root / 'repository'
        mac = repo / 'mac'
        (mac / 'build').mkdir(parents=True)
        shutil.copytree(self.app, mac / 'build/Meltype.app')
        for name in ['install.sh', 'Install Meltype.command', 'install-app.sh',
                     'start-input-method.sh', 'select-input-source.swift', 'TESTER-README.txt']:
            shutil.copy2(REPO / 'mac' / name, mac / name)
        for name in ['LICENSE', 'THIRD-PARTY-NOTICES.md']:
            (repo / name).write_text('fixture')
        (mac / 'package.sh').write_text((REPO / 'mac/package.sh').read_text().replace(LSREGISTER, 'lsregister'))
        result = subprocess.run(['bash', str(mac / 'package.sh'), 'test.zip'], env=self.environment,
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        import zipfile
        with zipfile.ZipFile(repo / 'dist/test.zip') as archive:
            names = archive.namelist()
        self.assertIn('Meltype-mac/Meltype.app.zip', names)
        self.assertFalse(any('/Meltype.app/' in name for name in names))

    def test_registration_failure_warns_and_continues(self):
        self.compress()
        self.environment['MELTYPE_TEST_FAIL_REGISTER'] = '23'
        result = self.run_install()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('システム設定', result.stderr)
        self.assertEqual(self.calls().count('register'), 1)
        self.assertIn('started', self.calls())
        self.assertIn('selected', self.calls())


if __name__ == '__main__':
    unittest.main(verbosity=2)
