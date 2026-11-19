#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 aster-void
#
# 端末版 (meltype-tty) を、本体・Mozc の変換ヘルパー・Zenzai を同梱した 1 つのフォルダーに組み立てる。できたものは tui/build/Meltype-tty/ に置く。
# IBus・fcitx5 の版 (linux/) とは本体を共有しない。フォルダーをどこに置いても動き、meltype-tty への symlink から起動してもよい。
#   tui/build.sh
# 先に Mozc の変換ヘルパーを native/mozc/build-mozc-helper.sh で (native/mozc/bin/linux/)、
# Zenzai の llama-server を native/zenzai/build-llama-server.sh で (native/zenzai/bin/linux-<版>/) ビルドしておく。
# 必要なもの: .NET 10 SDK、clang (NativeAOT のリンクに使う)、zlib (zlib1g-dev)、curl (Zenzai のモデルを取るのに使う)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$here/build/Meltype-tty"
mozc="$root/native/mozc/bin/linux"
# shellcheck source=../native/zenzai/zenzai.env
source "$root/native/zenzai/zenzai.env"
llama="$root/native/zenzai/bin/linux-$LLAMA_BUILD"

case "$(uname -s)-$(uname -m)" in
    Linux-x86_64) rid=linux-x64 ;;
    Linux-aarch64) rid=linux-arm64 ;;
    *) echo "対応していない OS・CPU です: $(uname -s) $(uname -m)" >&2; exit 1 ;;
esac
if [[ ! -x "$mozc/meltype_mozc_helper" ]]; then
    echo "Mozc の変換ヘルパーがありません。先に native/mozc/build-mozc-helper.sh を実行してください。" >&2
    exit 1
fi
if [[ ! -x "$llama/llama-server" ]]; then
    echo "Zenzai の llama-server ($LLAMA_BUILD) がありません。先に native/zenzai/build-llama-server.sh を実行してください。" >&2
    exit 1
fi

rm -rf "$here/build"
mkdir -p "$out/mozc"

echo "== 1/3 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish "$root/src/Meltype.Core.CApi/Meltype.Core.CApi.csproj" -c Release -r "$rid" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true -p:TreatWarningsAsErrors=true \
    --source https://api.nuget.org/v3/index.json -o "$here/build/native"
cp "$here/build/native/MeltypeNative.so" "$out/libMeltypeNative.so"

echo "== 2/3 組み立て"
cp "$here/meltype-tty" "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$out/"
cp "$mozc/meltype_mozc_helper" "$mozc/MOZC-LICENSE.txt" "$mozc/MOZC-CREDITS.html" "$out/mozc/"
chmod +x "$out/meltype-tty" "$out/mozc/meltype_mozc_helper"

echo "== 3/3 Zenzai"
# モデルを取れないとき: リリースのビルド (タグ v*) なら止める (Zenzai は既定 ON なので、無いまま配らない)。
# 自分で使うビルドなら、モデルを同梱せずに続ける (Zenzai なしで、Mozc だけで変換する)
cache="$root/native/zenzai/cache"
mkdir -p "$cache"
cp -R "$llama" "$out/zenzai"
# url・保存先・SHA-256。保存先のハッシュが合えば取り直さない。取れない・合わなければ消して 1 を返す
fetch_verified() {
    if [[ ! -f "$2" ]] || [[ "$(sha256sum "$2" | cut -d' ' -f1)" != "$3" ]]; then
        curl -fsSL -o "$2" "$1" || { rm -f "$2"; return 1; }
    fi
    [[ "$(sha256sum "$2" | cut -d' ' -f1)" == "$3" ]] || { rm -f "$2"; return 1; }
}
if fetch_verified "https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf/resolve/$ZENZ_COMMIT/ggml-model-Q5_K_M.gguf" "$cache/$ZENZ_NAME" "$ZENZ_SHA256" &&
    curl -fsSL -o "$out/zenzai/LICENSE-zenz.txt" https://www.apache.org/licenses/LICENSE-2.0.txt; then
    cp "$cache/$ZENZ_NAME" "$out/zenzai/"
    echo "Zenzai のモデルを同梱しました: $ZENZ_NAME"
elif [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then
    echo "Zenzai のモデルを取れませんでした。リリースのビルドなので止めます" >&2
    exit 1
else
    echo "警告: Zenzai のモデルを取れなかったので、同梱しません (Zenzai なしで変換します)" >&2
fi
echo "作成しました: $out"
