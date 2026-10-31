#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 joyamazaki
"""Exercise real Sparkle signatures without publishing or touching the Keychain."""
import base64
import importlib.util
import os
from pathlib import Path
import plistlib
import subprocess
import tempfile
import unittest
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('appcast', ROOT / 'tools/make-mac-appcast.py')
appcast = importlib.util.module_from_spec(spec)
spec.loader.exec_module(appcast)
SIGN = ROOT / 'mac/.build/artifacts/sparkle/Sparkle/bin/sign_update'

class Tests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='meltype-feed-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.key = self.root / 'seed'
        self.key.write_bytes(base64.b64encode(os.urandom(32)))
        self.key.chmod(0o600)
        self.public = subprocess.check_output(['xcrun', 'swift', str(ROOT / 'tools/sparkle-public-key.swift'), str(self.key)], text=True).strip()
        self.archive = self.root / 'Meltype-1.4.0-mac-update.zip'
        self.url = 'https://github.com/yksr-melt/Meltype/releases/download/v1.4.0/Meltype-1.4.0-mac-update.zip'
        self.output = self.root / 'appcast.xml'
        self.write_app()

    def write_app(self, **overrides):
        info = dict(CFBundleIdentifier='io.github.yksr-melt.inputmethod.Meltype', CFBundleVersion='1.4.0',
                    CFBundleShortVersionString='1.4.0', SUPublicEDKey=self.public, LSMinimumSystemVersion='13.0')
        info.update(overrides)
        with zipfile.ZipFile(self.archive, 'w') as z:
            z.writestr('Meltype.app/Contents/Info.plist', plistlib.dumps(info))

    def generate(self, url=None):
        appcast.generate(self.archive, self.key, SIGN, url or self.url, self.output)

    def test_signature_and_tampering(self):
        self.generate()
        feed = ET.parse(self.output)
        enclosure = feed.find('./channel/item/enclosure')
        signature = enclosure.attrib['{' + appcast.SPARKLE + '}edSignature']
        self.assertEqual(enclosure.attrib['length'], str(self.archive.stat().st_size))
        self.assertEqual(feed.find('./channel/item/{' + appcast.SPARKLE + '}version').text, '1.4.0')
        with self.archive.open('ab') as f:
            f.write(b'tampered')
        result = subprocess.run([str(SIGN), '--verify', '--ed-key-file', str(self.key), str(self.archive), signature], capture_output=True)
        self.assertNotEqual(result.returncode, 0)

    def test_key_mismatch(self):
        self.write_app(SUPublicEDKey=base64.b64encode(bytes(32)).decode())
        with self.assertRaises(ValueError): self.generate()
        self.assertFalse(self.output.exists())

    def test_version_mismatch(self):
        self.write_app(CFBundleVersion='1.5.0', CFBundleShortVersionString='1.5.0')
        with self.assertRaises(ValueError): self.generate()

    def test_prerelease_rejected(self):
        self.write_app(CFBundleShortVersionString='1.4.0-beta')
        with self.assertRaises(ValueError): self.generate()

    def test_unexpected_bundle(self):
        self.write_app(CFBundleIdentifier='example.other')
        with self.assertRaises(ValueError): self.generate()

    def test_untrusted_urls(self):
        for url in [self.url.replace('https:', 'http:'), self.url.replace('github.com', 'example.org'), self.url + '?x=1', self.url.replace('github.com', 'user@github.com')]:
            with self.subTest(url=url), self.assertRaises(ValueError): self.generate(url)

if __name__ == '__main__': unittest.main(verbosity=2)
