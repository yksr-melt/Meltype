#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Linux 版 (IBus・fcitx5) を組み立てる。できたものは linux/build/Meltype-linux/ に置く。
#   linux/build.sh
# 本体は native/build-core-linux.sh が組み立てる (先にビルドしておくもの・必要なものはそのファイルの先頭)。
# fcitx5 のアドオンは、cmake と fcitx5 の開発用のパッケージ (fcitx5-modules-dev) があればビルドする (無ければ IBus だけ)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$here/build/Meltype-linux"
version="$(dotnet msbuild "$root/src/Meltype.Core/Meltype.Core.csproj" -nologo -getProperty:Version)"

rm -rf "$here/build"
"$root/native/build-core-linux.sh" "$out" "$here/build"

echo "== fcitx5 のアドオン"
if command -v cmake > /dev/null && pkg-config --exists Fcitx5Core 2> /dev/null; then
    cmake -S "$here/fcitx5" -B "$here/build/fcitx5" -DMELTYPE_DIR=/opt/meltype -DCMAKE_BUILD_TYPE=Release > /dev/null
    cmake --build "$here/build/fcitx5" > /dev/null
    mkdir -p "$out/fcitx5"
    cp "$here/build/fcitx5/meltype.so" "$here/fcitx5/meltype-addon.conf" "$here/fcitx5/meltype.conf" "$out/fcitx5/"
else
    echo "fcitx5 の開発用のパッケージ (fcitx5-modules-dev) か cmake が無いので、fcitx5 のアドオンは入れません (IBus だけ)。"
fi

echo "== IBus のエンジン・設定画面"
sed "s|@VERSION@|$version|g" "$here/meltype.xml" > "$out/meltype.xml"
cp "$here/ibus-engine-meltype" "$here/meltype-settings" "$here/ibus-setup-meltype.desktop" "$here/icon.png" "$out/"
chmod +x "$out/ibus-engine-meltype" "$out/meltype-settings"
echo "作成しました: $out"
