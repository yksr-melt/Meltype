#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltype Contributors
#
# Meltype の Linux 版 (IBus・fcitx5) を組み立てる。できたものは linux/build/Meltype-linux/ に置く。
#   linux/build.sh
# 本体は native/build-core-linux.sh が組み立てる (先にビルドしておくもの・必要なものはそのファイルの先頭)。
# fcitx5 のアドオンは、cmake と fcitx5 の開発用のパッケージ (fcitx5-modules-dev) があればビルドする (無ければ IBus だけ)。
# 設定アプリ (src/Meltype.Settings) は NativeAOT でビルドし、meltype-settings として置く (描画に使う libSkiaSharp.so・libHarfBuzzSharp.so を隣に置く)。
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

echo "== 設定アプリ (C#, NativeAOT)"
case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "設定アプリをビルドできない CPU です: $(uname -m)" >&2; exit 1 ;;
esac
dotnet publish "$root/src/Meltype.Settings/Meltype.Settings.csproj" -c Release -r "$rid" \
    -p:StripSymbols=true -o "$here/build/settings"
cp "$here/build/settings/MeltypeSettings" "$out/meltype-settings"
cp "$here/build/settings/libSkiaSharp.so" "$here/build/settings/libHarfBuzzSharp.so" "$out/"

echo "== IBus のエンジン"
sed "s|@VERSION@|$version|g" "$here/meltype.xml" > "$out/meltype.xml"
cp "$here/ibus-engine-meltype" "$here/ibus-setup-meltype.desktop" "$here/icon.png" "$out/"
chmod +x "$out/ibus-engine-meltype"
echo "作成しました: $out"
