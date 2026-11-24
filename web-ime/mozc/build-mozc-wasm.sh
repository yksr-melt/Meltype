#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# ブラウザー版 (web-ime/) の Mozc を WebAssembly にして、web-ime/mozc/out/ (mozc.js・mozc.wasm) に置く。
#   web-ime/mozc/build-mozc-wasm.sh [Mozc を置く場所 (既定: ~/mozc-wasm)]
# 必要なもの: git、Bazelisk (bazel)、clang (辞書を作るツールをこの PC 向けにビルドする)。初回は 30 分以上かかる。
# Mozc の版は native/mozc/MOZC_COMMIT。デスクトップ版のヘルパー (native/mozc/build-mozc-helper.sh) とは別の場所に取る
# (MODULE.bazel に emsdk を足すので、同じ場所だとデスクトップ版のビルドまで emsdk を読むことになる)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
native="$here/../../native/mozc"
mozc="${1:-$HOME/mozc-wasm}"
commit="$(tr -d '[:space:]' < "$native/MOZC_COMMIT")"
# Mozc が使う Bazel 9 で読める emsdk の Bazel のモジュール
emsdk_version=6.0.13

if [[ ! -d "$mozc/.git" ]]; then
    mkdir -p "$mozc"
    git -C "$mozc" init -q
    git -C "$mozc" remote add origin https://github.com/google/mozc.git
    git -C "$mozc" fetch -q --depth 1 origin "$commit"
    git -C "$mozc" checkout -q FETCH_HEAD
    git -C "$mozc" submodule update -q --init --recursive --depth 1
fi
# emsdk は git で取る (Bazel に GitHub のアーカイブを取らせると、取れない環境がある)
if [[ ! -d "$mozc/emsdk" ]]; then
    git clone -q --depth 1 --branch "$emsdk_version" https://github.com/emscripten-core/emsdk.git "$mozc/emsdk"
fi
src="$mozc/src"

cp -f "$native/meltype_mozc_helper.cc" "$here/meltype_mozc_wasm.cc" "$src/converter/"
if ! grep -q 'name = "meltype_mozc_wasm"' "$src/converter/BUILD.bazel"; then
    printf '\n%s\n' "$(cat "$here/BUILD.fragment")" >> "$src/converter/BUILD.bazel"
fi
if ! grep -q 'name = "emsdk"' "$src/MODULE.bazel"; then
    printf '\nbazel_dep(name = "emsdk", version = "%s")\nlocal_path_override(module_name = "emsdk", path = "%s")\n' \
        "$emsdk_version" "$mozc/emsdk/bazel" >> "$src/MODULE.bazel"
fi

# スレッドを使わずにビルドする (thread-inline.patch の先頭)
if ! grep -q MOZC_THREAD_RUNS_INLINE "$src/base/thread.h"; then
    patch -d "$src" -p1 < "$here/thread-inline.patch"
fi

cd "$src"
# Emscripten は POSIX として動くので、Mozc の Linux 向けの分岐 (oss_linux) でビルドする
"${BAZEL:-bazel}" build //converter:meltype_mozc_wasm --config oss_linux --config release_build \
    "--downloader_config=$native/bazel-downloader.cfg"
mkdir -p "$here/out"
cp -f bazel-bin/converter/meltype_mozc_wasm/meltype_mozc_wasm_bin.js "$here/out/mozc.js"
cp -f bazel-bin/converter/meltype_mozc_wasm/meltype_mozc_wasm_bin.wasm "$here/out/mozc.wasm"
chmod u+w "$here/out/"*
echo "できました: $here/out"
