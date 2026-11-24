#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# ブラウザー版を作る。出力先のフォルダーを静的なサイトに置き、meltype-web.js を読み込む。
#   web-ime/build.sh [出力先 (既定: web-ime/dist)]
# 必要なもの: .NET 10 SDK と wasm-tools のワークロード (dotnet workload install wasm-tools)。
# Mozc の WebAssembly (mozc/build-mozc-wasm.sh で作る) が web-ime/mozc/out/ にあれば同梱する。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$(realpath -m "${1:-$here/dist}")"
rm -rf "$out" "$here/obj/publish"
dotnet publish "$here/../src/Meltype.Web" -c Release -o "$here/obj/publish"
mkdir -p "$out"
cp -r "$here/obj/publish/wwwroot/_framework" "$out/"
cp "$here/meltype-web.js" "$here/meltype-web.css" "$out/"
if [[ -d "$here/mozc/out" ]]; then
    mkdir -p "$out/mozc"
    cp "$here/mozc/out/"* "$out/mozc/"
fi
echo "できました: $out"
