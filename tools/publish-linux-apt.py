#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
"""公開済みの検証済みdebから、署名付きAPT更新リポジトリーを作る。"""
import gzip
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys

def tag_version(tag):
    """ゼロ埋めのタグ (flash-v0001.0001.0020) を deb の版 (1.1.20) に直す。"""
    return '.'.join(str(int(part)) for part in tag.removeprefix('flash-v').split('.'))

def build(tag, output):
    if not re.fullmatch(r'flash-v\d+\.\d+\.\d+', tag):
        raise ValueError('正式版タグが必要です')
    release = json.loads(subprocess.check_output(['gh', 'api', f'repos/appipinopi/Meltype-Flash/releases/tags/{tag}']))
    if release['draft'] or release['prerelease']:
        raise ValueError('公開済み正式版だけを登録します')
    asset = next(a for a in release['assets'] if a['name'] == 'Meltype-flash-Linux-x64.deb')
    output = Path(output).resolve()
    pool = output/'apt/pool'
    pool.mkdir(parents=True, exist_ok=True)
    package = pool/'meltype-flash.deb'
    with package.open('wb') as stream:
        subprocess.run(['gh', 'api', f"repos/appipinopi/Meltype-Flash/releases/assets/{asset['id']}", '-H', 'Accept: application/octet-stream'], stdout=stream, check=True)
    with package.open('rb') as stream:
        actual = 'sha256:' + hashlib.file_digest(stream, 'sha256').hexdigest()
    if actual != asset.get('digest'):
        raise ValueError('debのSHA-256が一致しません')
    version = subprocess.check_output(['dpkg-deb', '-f', package, 'Version'], text=True).strip()
    if version != tag_version(tag):
        raise ValueError('debと公開タグの版が一致しません')
    apt = output/'apt'
    binary = apt/'dists/stable/main/binary-amd64'
    binary.mkdir(parents=True, exist_ok=True)
    packages = subprocess.check_output(['apt-ftparchive', 'packages', 'pool'], cwd=apt)
    (binary/'Packages').write_bytes(packages)
    (binary/'Packages.gz').write_bytes(gzip.compress(packages, mtime=0))
    release_file = apt/'dists/stable/Release'
    release_file.write_bytes(subprocess.check_output(['apt-ftparchive', '-o', 'APT::FTPArchive::Release::Origin=Meltype-FLASH', '-o', 'APT::FTPArchive::Release::Label=Meltype-FLASH', '-o', 'APT::FTPArchive::Release::Suite=stable', '-o', 'APT::FTPArchive::Release::Codename=stable', '-o', 'APT::FTPArchive::Release::Architectures=amd64', '-o', 'APT::FTPArchive::Release::Components=main', 'release', 'dists/stable'], cwd=apt))
    for filename, mode in [('InRelease', '--clearsign'), ('Release.gpg', '--detach-sign')]:
        subprocess.run(['gpg', '--batch', '--yes', '--armor', mode, '--output', str(release_file.with_name(filename)), str(release_file)], check=True)
    shutil.copyfile(Path(__file__).resolve().parents[1]/'rust/meltype-flash-archive-key.asc', apt/'meltype-flash-archive-key.asc')
    (output/'.nojekyll').write_text('')
    (output/'index.html').write_text('<!doctype html><meta charset="utf-8"><title>Meltype-FLASH APT</title><p>Meltype-FLASH Ubuntu向け署名付き更新リポジトリーです。導入方法はGitHubのLinux案内を参照してください。</p>', encoding='utf-8')

if __name__ == '__main__':
    build(sys.argv[1], sys.argv[2])
