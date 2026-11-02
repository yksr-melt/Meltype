#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Linux 版 (IBus・fcitx5) を組み立てる。できたものは linux/build/Meltype-linux/ に置く。
#   linux/build.sh
# 先に Mozc の変換ヘルパーを native/mozc/build-mozc-helper.sh でビルドしておく (native/mozc/bin/linux/)。
# 必要なもの: .NET 10 SDK、clang (NativeAOT のリンクに使う)、zlib (zlib1g-dev)
# fcitx5 のアドオンは、cmake と fcitx5 の開発用のパッケージ (fcitx5-modules-dev) があればビルドする (無ければ IBus だけ)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$here/build/Meltype-linux"
mozc="$root/native/mozc/bin/linux"
version="$(dotnet msbuild "$root/src/Meltype.Core/Meltype.Core.csproj" -nologo -getProperty:Version)"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ ]]; then
    echo "版番号が不正です: $version" >&2
    exit 1
fi
if [[ "${GITHUB_REF:-}" == refs/tags/v* && "${GITHUB_REF#refs/tags/v}" != "$version" ]]; then
    echo "タグの版と本体の版が一致しません: $GITHUB_REF / $version" >&2
    exit 1
fi

case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
esac
if [[ ! -x "$mozc/meltype_mozc_helper" ]]; then
    echo "Mozc の変換ヘルパーがありません。先に native/mozc/build-mozc-helper.sh を実行してください。" >&2
    exit 1
fi

rm -rf "$here/build"
mkdir -p "$out/mozc"

echo "== 1/3 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish "$root/src/Meltype.Mac.Native/Meltype.Mac.Native.csproj" -c Release -r "$rid" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true \
    --source https://api.nuget.org/v3/index.json -o "$here/build/native"
cp "$here/build/native/MeltypeNative.so" "$out/libMeltypeNative.so"

echo "== 2/3 fcitx5 のアドオン"
if command -v cmake > /dev/null && pkg-config --exists Fcitx5Core 2> /dev/null; then
    cmake -S "$here/fcitx5" -B "$here/build/fcitx5" -DMELTYPE_DIR=/opt/meltype -DCMAKE_BUILD_TYPE=Release > /dev/null
    cmake --build "$here/build/fcitx5" > /dev/null
    mkdir -p "$out/fcitx5"
    cp "$here/build/fcitx5/meltype.so" "$here/fcitx5/meltype-addon.conf" "$here/fcitx5/meltype.conf" "$out/fcitx5/"
else
    echo "fcitx5 の開発用のパッケージ (fcitx5-modules-dev) か cmake が無いので、fcitx5 のアドオンは入れません (IBus だけ)。"
fi

echo "== 3/3 組み立て"
sed "s|@VERSION@|$version|g" "$here/meltype.xml" > "$out/meltype.xml"
cp "$here/ibus-engine-meltype" "$here/icon.png" "$here/install.sh" "$here/uninstall.sh" "$out/"
cp "$here/TESTER-README.txt" "$out/はじめにお読みください.txt"
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$out/"
cp "$mozc/meltype_mozc_helper" "$mozc/MOZC-LICENSE.txt" "$mozc/MOZC-CREDITS.html" "$out/mozc/"
chmod +x "$out/ibus-engine-meltype" "$out/install.sh" "$out/uninstall.sh" "$out/mozc/meltype_mozc_helper"
echo "作成しました: $out"
