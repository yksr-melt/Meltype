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

# ヘルパーは、ほかの日本語入力の品詞名を Mozc の品詞に読み替えるのに、Mozc の辞書の取り込み (user_dictionary_importer) を使う。
# 取り込みは Mozc の設定画面だけに公開されているので、visibility に converter パッケージを足す (native/mozc/build-mozc-helper.sh と同じ書き換え)。
dictionary_rules="$src/dictionary/BUILD.bazel"
perl -0pi -e 's/(textual_hdrs = \[":pos_map"\],\n( *)visibility = \[\n)(?!\2    "\/\/converter:__pkg__",)/$1$2    "\/\/converter:__pkg__",\n/' "$dictionary_rules"
if ! perl -0ne 'exit(/textual_hdrs = \[":pos_map"\],\n *visibility = \[\n *"\/\/converter:__pkg__",/ ? 0 : 1)' "$dictionary_rules"; then
    echo "エラー: $dictionary_rules の取り込みの visibility に converter を足せませんでした (Mozc の BUILD.bazel の形が変わった可能性があります)。" >&2
    exit 1
fi

# ヘルパーのソースと BUILD の定義を Mozc の converter パッケージに入れる。
# 前に足した定義は消してから足す (BUILD.fragment の変更を反映する。native/mozc/build-mozc-helper.sh と同じ)。
cp -f "$native/meltype_mozc_helper.cc" "$here/meltype_mozc_wasm.cc" "$src/converter/"
git -C "$mozc" checkout -q HEAD -- src/converter/BUILD.bazel
printf '\n%s\n' "$(cat "$here/BUILD.fragment")" >> "$src/converter/BUILD.bazel"
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
