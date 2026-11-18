#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""生成済みdeb/rpmの所属・依存・バイト列を元のIBus配布物と照合する。導入はしない。"""
import argparse
import hashlib
import json
import re
from pathlib import Path
import stat
import subprocess
import tempfile
import xml.etree.ElementTree as ET


TARGET = 'usr/libexec/meltype-flash/ibus'
BINARIES = ('meltype-ibus', 'meltype-flash', 'meltype-rs')
DESKTOPS = ('meltype-flash.desktop', 'ibus-setup-meltype-flash.desktop')


def command(*args):
    return subprocess.check_output([str(arg) for arg in args], text=True).strip()


def sha256(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def check_payload(root, bundle, version, kind, arch):
    original = {path.relative_to(bundle).as_posix(): sha256(path)
                for path in bundle.rglob('*') if path.is_file()}
    expected = {TARGET + '/' + name for name in original}
    expected.add('usr/share/ibus/component/meltype-flash.xml')
    expected.add('usr/share/metainfo/meltype-flash.metainfo.xml')
    expected.add('usr/share/swcatalog/xml/meltype-flash.xml')
    expected.add('usr/share/swcatalog/meltype-flash.xml')
    expected.add('usr/share/icons/hicolor/256x256/apps/meltype-flash.png')
    expected.update('usr/share/applications/' + name for name in DESKTOPS)
    if kind == 'deb' and arch == 'amd64':
        expected.update(('etc/apt/sources.list.d/meltype-flash.sources', 'usr/share/keyrings/meltype-flash-archive-key.asc'))
    actual = {path.relative_to(root).as_posix() for path in root.rglob('*') if path.is_file()}
    assert actual == expected, ('unexpected owned files', actual ^ expected)
    assert not any(path.is_symlink() for path in root.rglob('*')), 'symlink in package'
    for name, digest in original.items():
        assert sha256(root / TARGET / name) == digest, ('payload changed', name)
    for name in BINARIES:
        assert stat.S_IMODE((root / TARGET / name).stat().st_mode) == 0o755, name
        versions = re.findall(r'GLIBC_(\d+)\.(\d+)', command('readelf', '--version-info', '--wide', root / TARGET / name))
        assert all((int(major), int(minor)) <= (2, 39) for major, minor in versions), ('Ubuntu 24.04で使えないGLIBC依存', name, versions)
    xml = ET.parse(root / 'usr/share/ibus/component/meltype-flash.xml').getroot()
    assert xml.findtext('name') == 'org.freedesktop.IBus.MeltypeFlash'
    assert xml.findtext('version') == version
    assert xml.findtext('exec') == "'/" + TARGET + "/meltype-ibus' --ibus --keyboard-layout jp"
    assert xml.findtext('engines/engine/name') == 'meltype-flash'
    assert xml.findtext('engines/engine/layout') == 'jp'
    assert xml.findtext('engines/engine/icon') == '/' + TARGET + '/icon.png'
    for name in DESKTOPS:
        desktop = (root / 'usr/share/applications' / name).read_text(encoding='utf-8')
        assert '\nExec=/' + TARGET + '/meltype-flash\n' in desktop
    assert sha256(root / 'usr/share/icons/hicolor/256x256/apps/meltype-flash.png') == original['icon.png']
    setup = (root / 'usr/share/applications/ibus-setup-meltype-flash.desktop').read_text(encoding='utf-8')
    assert '\nNoDisplay=true\n' in setup and '\nX-Meltype-Flash-IBus=true\n' in setup
    metadata = ET.parse(root / 'usr/share/metainfo/meltype-flash.metainfo.xml').getroot()
    catalog = ET.parse(root / 'usr/share/swcatalog/xml/meltype-flash.xml').getroot()
    assert sha256(root/'usr/share/swcatalog/xml/meltype-flash.xml') == sha256(root/'usr/share/swcatalog/meltype-flash.xml')
    component = catalog.find('component')
    assert component is not None
    for tag in ('id', 'pkgname', 'launchable', 'icon', 'project_license'):
        assert component.findtext(tag) == metadata.findtext(tag), tag
    assert component.findtext('pkgname') == 'meltype-flash'
    assert component.findtext('launchable') == DESKTOPS[0]
    assert component.findtext('icon') == 'meltype-flash'
    return {'payload_files': len(original), 'binaries': {name: original[name] for name in BINARIES}}


def verify(bundle, deb, rpm, version, arch):
    assert bundle.is_dir() and all((bundle / name).is_file() for name in BINARIES), 'original bundle missing'
    normalized_version = version.replace('-pre.', '~pre.')
    assert command('dpkg-deb', '-f', deb, 'Package') == 'meltype-flash'
    assert command('dpkg-deb', '-f', deb, 'Version') == normalized_version
    assert command('dpkg-deb', '-f', deb, 'Architecture') == arch
    depends = command('dpkg-deb', '-f', deb, 'Depends')
    for required in ('ibus', 'zenity', 'libc6 (>= 2.39)', 'libstdc++6', 'libibus-1.0-5', 'libx11-xcb1', 'libegl1', 'libwayland-egl1'):
        assert required in depends, ('missing Debian runtime dependency', required)
    assert all(line.split()[1] == 'root/root' for line in command('dpkg-deb', '--contents', deb).splitlines())
    rpm_arch = {'amd64': 'x86_64', 'arm64': 'aarch64'}[arch]
    # RPM 6で作成しても、RPM 4系が読める配布形式を使う。
    if 'RPMFORMAT' in command('rpm', '--querytags').splitlines():
        assert command('rpm', '-qp', '--queryformat', '%{RPMFORMAT}', rpm) == '4', 'RPM v4 format required'
    assert command('rpm', '-qp', '--queryformat', '%{NAME}\n%{VERSION}\n%{RELEASE}\n%{ARCH}', rpm).splitlines() == [
        'meltype-flash', normalized_version, '1', rpm_arch]
    requires = command('rpm', '-qp', '--requires', rpm).splitlines()
    for required in ('ibus', 'zenity', 'glibc >= 2.39', 'libc.so.6(GLIBC_2.39)(64bit)',
                     'libibus-1.0.so.5()(64bit)', 'libX11-xcb.so.1()(64bit)', 'libEGL.so.1()(64bit)'):
        assert required in requires, ('missing RPM runtime dependency', required)
    assert command('rpm', '-qp', '--scripts', rpm) == '', 'unexpected privileged RPM script'
    assert set(command('rpm', '-qp', '--queryformat', '[%{FILEUSERNAME}\n%{FILEGROUPNAME}\n]', rpm).splitlines()) == {'root'}
    command('rpm', '--checksig', '--nosignature', rpm)
    with tempfile.TemporaryDirectory(prefix='meltype-flash-package-check-') as temporary:
        base = Path(temporary)
        deb_root, rpm_root = base / 'deb', base / 'rpm'
        rpm_root.mkdir()
        subprocess.run(['dpkg-deb', '--extract', str(deb), str(deb_root)], check=True)
        subprocess.run(['dpkg-deb', '--control', str(deb), str(base / 'control')], check=True)
        for name in ('postinst', 'postrm'):
            script = (base / 'control' / name).read_text(encoding='utf-8')
            assert 'ibus write-cache --system' in script
            assert 'appstreamcli refresh-cache --force' in script
            assert not any(text in script for text in ('pkill', 'killall', 'rm ', '$HOME', '.local/share'))
        archive = subprocess.Popen(['rpm2cpio', str(rpm)], stdout=subprocess.PIPE)
        try:
            subprocess.run(['cpio', '-idm', '--quiet', '--no-absolute-filenames'], cwd=rpm_root,
                           stdin=archive.stdout, check=True)
        finally:
            archive.stdout.close()
            archive_status = archive.wait()
        assert archive_status == 0
        result = {kind: check_payload(root, bundle, version, kind, arch) for kind, root in (('deb', deb_root), ('rpm', rpm_root))}
    result.update(version=version, arch=arch, assets={path.name: sha256(path) for path in (deb, rpm)})
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bundle', required=True, type=Path)
    parser.add_argument('--deb', required=True, type=Path)
    parser.add_argument('--rpm', required=True, type=Path)
    parser.add_argument('--version', required=True)
    parser.add_argument('--arch', choices=['amd64', 'arm64'], required=True)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    result = verify(*(path.resolve() for path in (args.bundle, args.deb, args.rpm)), args.version, args.arch)
    serialized = json.dumps(result, ensure_ascii=True, indent=2) + '\n'
    if args.output:
        args.output.write_text(serialized, encoding='utf-8')
    print(serialized, end='')
