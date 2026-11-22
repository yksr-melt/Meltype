#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hisiragi
#
# ブラウザー版を web-ime/dist/ に作る。dist/ をそのまま静的なサイトに置けば動く。
#   web-ime/build.sh
# 必要なもの: .NET 10 SDK と wasm-tools のワークロード (dotnet workload install wasm-tools)。
# Mozc の WebAssembly (mozc/build-mozc-wasm.sh で作る) が web-ime/mozc/out/ にあれば同梱する。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/dist"
rm -rf "$out"
dotnet publish "$here/../src/Meltype.Web" -c Release -o "$here/obj/publish"
mkdir -p "$out"
cp -r "$here/obj/publish/wwwroot/_framework" "$out/"
cp "$here/index.html" "$here/meltype-web.js" "$here/meltype-web.css" "$out/"
if [[ -d "$here/mozc/out" ]]; then
    mkdir -p "$out/mozc"
    cp "$here/mozc/out/"* "$out/mozc/"
fi
echo "できました: $out"
