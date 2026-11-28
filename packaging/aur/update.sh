#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# AUR の meltype-bin の PKGBUILD を、リリースの版と zip の SHA-256 に書き換え、.SRCINFO を作り直す (#1097)。
#   packaging/aur/update.sh <版> <x86_64 の zip> <aarch64 の zip>
#   例: packaging/aur/update.sh 1.4.0 dist/Meltype-1.4.0-linux.zip dist/Meltype-1.4.0-linux-arm64.zip
# プレリリース (1.4.1-pre.1 など) は AUR に出さない。
# .SRCINFO は makepkg --printsrcinfo と同じ形をここで作る (CI の Ubuntu に makepkg が無いため)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
pkg="$here/meltype-bin"

version="${1:?版を指定してください}"
version="${version#v}"
if [[ ! "$version" =~ ^[0-9]+(\.[0-9]+)*$ ]]; then
    echo "AUR に出せるのは正式版だけです: $version" >&2
    exit 1
fi
sum_x64="$(sha256sum "${2:?x86_64 の zip を指定してください}" | cut -d' ' -f1)"
sum_arm64="$(sha256sum "${3:?aarch64 の zip を指定してください}" | cut -d' ' -f1)"

old="$(sed -n 's/^pkgver=//p' "$pkg/PKGBUILD")"
sed -i \
    -e "s/^pkgver=.*/pkgver=$version/" \
    -e "s/^sha256sums_x86_64=.*/sha256sums_x86_64=('$sum_x64')/" \
    -e "s/^sha256sums_aarch64=.*/sha256sums_aarch64=('$sum_arm64')/" \
    "$pkg/PKGBUILD"
# 版が変わったら pkgrel を 1 に戻す
if [[ "$old" != "$version" ]]; then sed -i "s/^pkgrel=.*/pkgrel=1/" "$pkg/PKGBUILD"; fi

# PKGBUILD を読んで .SRCINFO を書く (配列は 1 要素 1 行)
(
    cd "$pkg"
    # shellcheck disable=SC1091
    source ./PKGBUILD
    list() { local key="$1"; shift; local v; for v in "$@"; do printf '\t%s = %s\n' "$key" "$v"; done; }
    {
        echo "pkgbase = $pkgname"
        printf '\tpkgdesc = %s\n\tpkgver = %s\n\tpkgrel = %s\n\turl = %s\n\tinstall = %s\n' "$pkgdesc" "$pkgver" "$pkgrel" "$url" "$install"
        list arch "${arch[@]}"
        list license "${license[@]}"
        list depends "${depends[@]}"
        list optdepends "${optdepends[@]}"
        list provides "${provides[@]}"
        list conflicts "${conflicts[@]}"
        list options "${options[@]}"
        list source_x86_64 "${source_x86_64[@]}"
        list sha256sums_x86_64 "${sha256sums_x86_64[@]}"
        list source_aarch64 "${source_aarch64[@]}"
        list sha256sums_aarch64 "${sha256sums_aarch64[@]}"
        echo
        echo "pkgname = $pkgname"
    } > .SRCINFO
)
echo "更新しました: $pkg (版 $version)"
