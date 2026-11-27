#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltype Contributors
#
# 端末版 (meltype-tty) を、本体・Mozc の変換ヘルパー・Zenzai を同梱した 1 つのフォルダーに組み立てる。できたものは tui/build/Meltype-tty/ に置く。
# IBus・fcitx5 の版 (linux/) とは本体を共有しない。フォルダーをどこに置いても動き、meltype-tty への symlink から起動してもよい。
#   tui/build.sh
# 本体は native/build-core-linux.sh が組み立てる (先にビルドしておくもの・必要なものはそのファイルの先頭)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$here/build/Meltype-tty"

rm -rf "$here/build"
"$root/native/build-core-linux.sh" "$out" "$here/build"
install -m 755 "$here/meltype-tty" "$out/"
echo "作成しました: $out"
