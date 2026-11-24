#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hisiragi
#
# 紹介ページ (web-engine/site/) を、デモのエンジンと一緒に web-engine/site-dist/ に作る。site-dist/ をそのまま静的なサイトに置く。
#   web-engine/build-site.sh
# エンジン (web-engine/dist/) が無ければ、先に build.sh で作る。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/site-dist"
[[ -f "$here/dist/meltype.js" ]] || "$here/build.sh"
rm -rf "$out"
cp -r "$here/site" "$out"
cp -r "$here/dist" "$out/engine"
echo "できました: $out"
