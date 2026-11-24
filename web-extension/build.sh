#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hisiragi
#
# ブラウザーの拡張機能 (Chrome・Edge・Firefox) を web-extension/dist/ に作り、web-extension/meltype-extension.zip にまとめる。
#   web-extension/build.sh
# dist/ は Chrome の「パッケージ化されていない拡張機能を読み込む」・Firefox の about:debugging でそのまま読める。zip はストアに出すもの。
# 中身の Web エンジンは web-engine/build.sh で作る (無ければ先に作る)。アイコンは web/scripts/make-icons.mjs が作る。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
engine="$here/../web-engine/dist"
out="$here/dist"
[[ -f "$engine/meltype.js" ]] || "$here/../web-engine/build.sh"
rm -rf "$out" "$here/meltype-extension.zip"
mkdir -p "$out/engine"
cp "$here/src/"* "$out/"
cp -r "$here/icons" "$out/"
cp "$here/../LICENSE" "$out/"
# 拡張機能はファイルをそのまま読むので、圧縮した写し (.gz・.br) と npm のためのファイルは入れない
(cd "$engine" && find . -type f ! -name '*.gz' ! -name '*.br' ! -name 'package.json' ! -name 'index.d.ts' ! -name 'index.mjs' ! -name 'README.md' -print0 | xargs -0 -I{} cp --parents {} "$out/engine/")
(cd "$out" && zip -qr -X "$here/meltype-extension.zip" .)
echo "できました: $out ($(du -sh "$out" | cut -f1))、$here/meltype-extension.zip ($(du -sh "$here/meltype-extension.zip" | cut -f1))"
