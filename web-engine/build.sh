#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hisiragi
#
# Meltype Web Engine を web-engine/dist/ に作る。dist/ がそのまま npm のパッケージで、<script> で読むときも dist/ を置けばよい。
#   web-engine/build.sh
# 必要なもの: .NET 10 SDK と wasm-tools のワークロード (dotnet workload install wasm-tools)。
# 漢字変換の Mozc の WebAssembly は、web-ime/mozc/out/ にあれば同梱する (無ければ、かなのまま確定する版になる)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/dist"
publish="$here/obj/publish"
rm -rf "$out" "$publish"
dotnet publish "$here/../src/Meltype.Web" -c Release -o "$publish"
mkdir -p "$out"
cp -r "$publish/wwwroot/_framework" "$out/"
cp "$here/src/meltype.js" "$here/src/index.mjs" "$here/src/meltype.css" "$here/index.d.ts" "$here/package.json" "$here/README.md" "$out/"
cp "$here/../LICENSE" "$here/../THIRD-PARTY-NOTICES.md" "$out/"
mozc="$here/../web-ime/mozc/out"
if [[ -f "$mozc/mozc.wasm" ]]; then
    mkdir -p "$out/mozc"
    cp "$mozc/mozc.js" "$mozc/mozc.wasm" "$out/mozc/"
else
    echo "注意: $mozc に Mozc がないので、漢字変換をしない版を作りました" >&2
fi
echo "できました: $out"
