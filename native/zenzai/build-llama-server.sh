#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltype Contributors
#
# Linux 版に同梱する llama.cpp の llama-server (CPU 版) を、native/zenzai/zenzai.env の版のソースからビルドする。
#   native/zenzai/build-llama-server.sh
# できたもの (llama-server・共有ライブラリ・LICENSE-llama.cpp.txt) は native/zenzai/bin/linux-<版>/ に置き、native/build-core-linux.sh が同梱する。
# フォルダーの名前に版を入れ、zenzai.env の版を上げたら、前の版のビルドを native/build-core-linux.sh が使わないようにする。
# 必要なもの: curl、cmake、C / C++ のコンパイラ。
# 配る物は、サポートする中で一番古い glibc (2.34、RHEL 9 系) の manylinux のコンテナでビルドする (Mozc のヘルパーと同じ、#1484)。
# llama.cpp の Ubuntu 向けのビルドは Ubuntu 24.04 の glibc・libstdc++ を要求し、RHEL 9 系・Debian 12 で動かない。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=zenzai.env
source "$here/zenzai.env"
cache="$here/cache"
out="$here/bin/linux-$LLAMA_BUILD"
src="$cache/llama.cpp-$LLAMA_BUILD"
build="$cache/llama.cpp-$LLAMA_BUILD-build"

mkdir -p "$cache"
archive="$cache/llama.cpp-$LLAMA_BUILD-src.tar.gz"
if [[ ! -f "$archive" ]] || [[ "$(sha256sum "$archive" | cut -d' ' -f1)" != "$LLAMA_SOURCE_SHA256" ]]; then
    curl -fsSL --retry 3 -o "$archive" "https://github.com/ggml-org/llama.cpp/archive/refs/tags/$LLAMA_BUILD.tar.gz"
fi
echo "$LLAMA_SOURCE_SHA256  $archive" | sha256sum -c -
rm -rf "$src" "$build"
mkdir -p "$src"
tar -xzf "$archive" -C "$src" --strip-components 1

# CPU ごとの計算部分 (libggml-cpu-*.so) をすべて作り、llama-server が起動するときに CPU に合うものを選ばせる (GGML_BACKEND_DL)。
# OpenMP・OpenSSL は使わない (ローカルの llama-server に要らず、配布物の依存が増える)。Web UI はビルド中にネットから取るので作らない
cmake -S "$src" -B "$build" -DCMAKE_BUILD_TYPE=Release \
    -DBUILD_SHARED_LIBS=ON -DGGML_NATIVE=OFF -DGGML_BACKEND_DL=ON -DGGML_CPU_ALL_VARIANTS=ON -DGGML_OPENMP=OFF \
    -DLLAMA_OPENSSL=OFF -DLLAMA_BUILD_UI=OFF -DLLAMA_USE_PREBUILT_UI=OFF \
    -DLLAMA_BUILD_TESTS=OFF -DLLAMA_BUILD_EXAMPLES=OFF \
    -DCMAKE_BUILD_WITH_INSTALL_RPATH=ON -DCMAKE_INSTALL_RPATH='$ORIGIN'
cmake --build "$build" --target llama-server --parallel "$(nproc)"

rm -rf "$out"
mkdir -p "$out"
cp "$build/bin/llama-server" "$out/"
# zip・CI の成果物ではシンボリックリンクが実体になるので、版の付いた実体を読む名前 (SONAME) で置く
for lib in "$build"/bin/*.so; do
    soname="$(readelf -d "$lib" | sed -n 's/.*(SONAME).*\[\(.*\)\]/\1/p')"
    cp -L "$lib" "$out/${soname:-$(basename "$lib")}"
done
cp "$src/LICENSE" "$out/LICENSE-llama.cpp.txt"
echo "作成しました: $out"
ls -la "$out"
