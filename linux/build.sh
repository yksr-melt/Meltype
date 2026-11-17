#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Linux 版 (IBus・fcitx5) を組み立てる。できたものは linux/build/Meltype-linux/ に置く。
#   linux/build.sh
# 先に Mozc の変換ヘルパーを native/mozc/build-mozc-helper.sh で (native/mozc/bin/linux/)、
# Zenzai の llama-server を native/zenzai/build-llama-server.sh で (native/zenzai/bin/linux-<版>/) ビルドしておく。
# 必要なもの: .NET 10 SDK、clang (NativeAOT のリンクに使う)、zlib (zlib1g-dev)、curl (Zenzai のモデルを取るのに使う)
# fcitx5 のアドオンは、cmake と fcitx5 の開発用のパッケージ (fcitx5-modules-dev) があればビルドする (無ければ IBus だけ)。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
out="$here/build/Meltype-linux"
mozc="$root/native/mozc/bin/linux"
# shellcheck source=../native/zenzai/zenzai.env
source "$root/native/zenzai/zenzai.env"
llama="$root/native/zenzai/bin/linux-$LLAMA_BUILD"
version="$(dotnet msbuild "$root/src/Meltype.Core/Meltype.Core.csproj" -nologo -getProperty:Version)"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ ]]; then
    echo "版番号が不正です: $version" >&2
    exit 1
fi
if [[ "${GITHUB_REF:-}" == refs/tags/v* && "${GITHUB_REF#refs/tags/v}" != "$version" ]]; then
    echo "タグの版と本体の版が一致しません: $GITHUB_REF / $version" >&2
    exit 1
fi

case "$(uname -m)" in
    x86_64) rid=linux-x64 ;;
    aarch64) rid=linux-arm64 ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
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

echo "== 1/4 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish "$root/src/Meltype.Core.CApi/Meltype.Core.CApi.csproj" -c Release -r "$rid" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true -p:TreatWarningsAsErrors=true \
    --source https://api.nuget.org/v3/index.json -o "$here/build/native"
cp "$here/build/native/MeltypeNative.so" "$out/libMeltypeNative.so"

echo "== 2/4 fcitx5 のアドオン"
if command -v cmake > /dev/null && pkg-config --exists Fcitx5Core 2> /dev/null; then
    cmake -S "$here/fcitx5" -B "$here/build/fcitx5" -DMELTYPE_DIR=/opt/meltype -DCMAKE_BUILD_TYPE=Release > /dev/null
    cmake --build "$here/build/fcitx5" > /dev/null
    mkdir -p "$out/fcitx5"
    cp "$here/build/fcitx5/meltype.so" "$here/fcitx5/meltype-addon.conf" "$here/fcitx5/meltype.conf" "$out/fcitx5/"
else
    echo "fcitx5 の開発用のパッケージ (fcitx5-modules-dev) か cmake が無いので、fcitx5 のアドオンは入れません (IBus だけ)。"
fi

echo "== 3/4 組み立て"
sed "s|@VERSION@|$version|g" "$here/meltype.xml" > "$out/meltype.xml"
cp "$here/ibus-engine-meltype" "$here/meltype-settings" "$here/ibus-setup-meltype.desktop" "$here/icon.png" "$out/"
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$out/"
cp "$mozc/meltype_mozc_helper" "$mozc/MOZC-LICENSE.txt" "$mozc/MOZC-CREDITS.html" "$out/mozc/"
chmod +x "$out/ibus-engine-meltype" "$out/meltype-settings" "$out/mozc/meltype_mozc_helper"

echo "== 4/4 Zenzai"
# llama.cpp の llama-server (MIT) と zenz-v3.2-small (Apache-2.0) を zenzai/ に同梱する (Windows の zenzai\・Mac と同じ)。
# モデルの版とハッシュは native/zenzai/zenzai.env (Windows・Mac と共通)。取ってきたモデルは native/zenzai/cache に置き、ハッシュが合えば取り直さない。
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
