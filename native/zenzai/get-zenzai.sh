#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Linux 版に Zenzai を同梱するために、llama.cpp の llama-server (CPU 版) と zenz のモデルを取ってきて native/zenzai/bin/linux に置く。
#   native/zenzai/get-zenzai.sh [置く場所]
# 版とハッシュは zenzai.env に書く (Windows の Get-Zenzai.ps1・mac/build.sh と共通)。ハッシュが合わなければ止める。
# linux/build.sh は native/zenzai/bin/linux があれば zenzai/ に同梱する。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="${1:-$here/bin/linux}"

# shellcheck source=zenzai.env
source "$here/zenzai.env"
case "$(uname -m)" in
    x86_64) arch=x64; llama_sha256="$LLAMA_SHA256_LINUX_X64" ;;
    aarch64) arch=arm64; llama_sha256="$LLAMA_SHA256_LINUX_ARM64" ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
esac

cache="$here/cache"
mkdir -p "$cache"

# 取ってきて、ハッシュを確かめる (合わなければ消して止める)
fetch() {
    local url="$1" path="$2" sha256="$3"
    if [[ -f "$path" ]] && [[ "$(sha256sum "$path" | cut -d' ' -f1)" == "$sha256" ]]; then return; fi
    curl -fsSL --retry 3 --connect-timeout 15 -o "$path" "$url"
    local actual
    actual="$(sha256sum "$path" | cut -d' ' -f1)"
    if [[ "$actual" != "$sha256" ]]; then
        rm -f "$path"
        echo "ハッシュが合いません: $url (期待 $sha256、実際 $actual)" >&2
        exit 1
    fi
}

# llama.cpp (MIT License)
tarball="$cache/llama-$LLAMA_BUILD-linux-$arch.tar.gz"
fetch "https://github.com/ggml-org/llama.cpp/releases/download/$LLAMA_BUILD/llama-$LLAMA_BUILD-bin-ubuntu-$arch.tar.gz" "$tarball" "$llama_sha256"
extract="$cache/llama-$LLAMA_BUILD-linux-$arch"
rm -rf "$extract" "$out"
mkdir -p "$extract" "$out"
tar -xzf "$tarball" -C "$extract" --strip-components=1

# llama-server と、それが使うライブラリだけを配る (ほかの実行ファイルと、それ専用の *-impl.so は配らない)。
# CPU ごとの計算の部品 (libggml-cpu-*.so) は llama-server が起動したときに選んで読むので、すべて入れる。
# 実行ファイルは RUNPATH が $ORIGIN なので、同じフォルダーに置けばよい。版付きの名前へのシンボリックリンクはそのまま写す
cp -P "$extract/llama-server" "$out/"
for lib in "$extract"/lib*.so*; do
    name="$(basename "$lib")"
    if [[ "$name" == *-impl.so && "$name" != libllama-server-impl.so ]]; then continue; fi
    cp -P "$lib" "$out/"
done
chmod 755 "$out/llama-server"
cp "$extract/LICENSE" "$out/LICENSE-llama.cpp.txt"
curl -fsSL --retry 3 -o "$out/LICENSE-zenz.txt" https://www.apache.org/licenses/LICENSE-2.0.txt

# zenz-v3.2-small (Apache License 2.0)。版はコミットで固定する
fetch "https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf/resolve/$ZENZ_COMMIT/ggml-model-Q5_K_M.gguf" "$cache/$ZENZ_NAME" "$ZENZ_SHA256"
cp "$cache/$ZENZ_NAME" "$out/$ZENZ_NAME"

cat > "$out/ZENZAI-NOTICE.txt" << EOF
llama.cpp $LLAMA_BUILD (MIT License, Copyright (c) 2023-2026 The ggml authors): https://github.com/ggml-org/llama.cpp
zenz-v3.2-small $ZENZ_COMMIT (Apache License 2.0, Miwa-Keita): https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf
EOF
echo "Zenzai (llama-server $LLAMA_BUILD と $ZENZ_NAME) を $out に置きました。"
