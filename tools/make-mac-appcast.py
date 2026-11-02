#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 joyamazaki
"""Make a single-release Sparkle feed using the official signing tool. Never publish or install."""
import argparse
import base64
from email.utils import formatdate
from pathlib import Path
import plistlib
import re
import subprocess
import xml.etree.ElementTree as ET
from urllib.parse import urlparse
import zipfile

SPARKLE = 'http://www.andymatuschak.org/xml-namespaces/sparkle'
ET.register_namespace('sparkle', SPARKLE)


def generate(archive, key_file, sign_tool, download_url, output):
    parsed = urlparse(download_url)
    if (parsed.scheme != 'https' or parsed.hostname != 'github.com' or parsed.username or parsed.password
            or not re.fullmatch(r'/yksr-melt/Meltype/releases/download/v\d+\.\d+\.\d+/Meltype-\d+\.\d+\.\d+-mac-update\.zip', parsed.path)
            or parsed.query or parsed.fragment):
        raise ValueError('Expected a stable official GitHub mac-update.zip release URL')
    with zipfile.ZipFile(archive) as z:
        info = plistlib.loads(z.read('Meltype.app/Contents/Info.plist'))
    if info.get('CFBundleIdentifier') != 'io.github.yksr-melt.inputmethod.Meltype':
        raise ValueError('Unexpected application bundle ID')
    version = info['CFBundleVersion']
    display_version = info['CFBundleShortVersionString']
    if not re.fullmatch(r'\d+\.\d+\.\d+', version) or display_version != version:
        raise ValueError('Publish only stable builds with a monotonic numeric bundle version')
    expected_name = f'Meltype-{version}-mac-update.zip'
    if archive.name != expected_name or parsed.path != f'/yksr-melt/Meltype/releases/download/v{version}/{expected_name}':
        raise ValueError('Archive, URL and application versions do not match')
    public_key = subprocess.check_output(['xcrun', 'swift', str(Path(__file__).with_name('sparkle-public-key.swift')),
                                          str(key_file)], text=True).strip()
    if info.get('SUPublicEDKey') != public_key:
        raise ValueError('Signing key does not match the public key embedded in the app')
    signature = subprocess.check_output([str(sign_tool), '--ed-key-file', str(key_file), '-p', str(archive)], text=True).strip()
    if len(base64.b64decode(signature, validate=True)) != 64:
        raise ValueError('Invalid EdDSA signature from signing tool')
    subprocess.run([str(sign_tool), '--verify', '--ed-key-file', str(key_file), str(archive), signature],
                   check=True, stdout=subprocess.DEVNULL)
    rss = ET.Element('rss', version='2.0')
    channel = ET.SubElement(rss, 'channel')
    ET.SubElement(channel, 'title').text = 'Meltype for macOS'
    item = ET.SubElement(channel, 'item')
    ET.SubElement(item, 'title').text = f'Meltype {display_version}'
    ET.SubElement(item, 'pubDate').text = formatdate(usegmt=True)
    ET.SubElement(item, f'{{{SPARKLE}}}minimumSystemVersion').text = info.get('LSMinimumSystemVersion', '13.0')
    ET.SubElement(item, f'{{{SPARKLE}}}version').text = version
    ET.SubElement(item, f'{{{SPARKLE}}}shortVersionString').text = display_version
    ET.SubElement(item, 'enclosure', {'url': download_url, 'length': str(archive.stat().st_size),
                                    'type': 'application/octet-stream', f'{{{SPARKLE}}}edSignature': signature})
    output.parent.mkdir(parents=True, exist_ok=True)
    ET.indent(rss)
    ET.ElementTree(rss).write(output, encoding='utf-8', xml_declaration=True)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('archive', type=Path)
    p.add_argument('--key-file', required=True, type=Path)
    p.add_argument('--sign-tool', required=True, type=Path)
    p.add_argument('--download-url', required=True)
    p.add_argument('--output', required=True, type=Path)
    args = p.parse_args()
    generate(args.archive.resolve(), args.key_file.resolve(), args.sign_tool.resolve(), args.download_url, args.output)


if __name__ == '__main__':
    main()
