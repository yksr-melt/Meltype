#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Mac 版をビルドして ~/Library/Input Methods にインストールする。
#   ./build.sh            ビルドしてインストール
#   ./build.sh --no-install  ビルドだけ (build/Meltype.app)
# 必要なもの: macOS 13.3 以降、Xcode (またはコマンドライン ツール: xcode-select --install)、.NET 10 SDK、
# Mozc の変換ヘルパー (先に native/mozc/build-mozc-helper.sh でビルドして native/mozc/bin/mac/ に置く)
set -euo pipefail
cd "$(dirname "$0")"

INSTALL=1
[[ "${1:-}" == "--no-install" ]] && INSTALL=0

case "$(uname -m)" in
    arm64) RID=osx-arm64; LLAMA_ARCH=arm64; LLAMA_SHA=LLAMA_MAC_ARM64_SHA256 ;;
    x86_64) RID=osx-x64; LLAMA_ARCH=x64; LLAMA_SHA=LLAMA_MAC_X64_SHA256 ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
esac

BUILD=build
VERSION="${MELTYPE_VERSION:-$(dotnet msbuild ../src/Meltype.Core/Meltype.Core.csproj -nologo -getProperty:Version)}"
if [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then VERSION="${GITHUB_REF_NAME#v}"; fi
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$ ]] || { echo "版番号が不正です: $VERSION" >&2; exit 1; }
APP="$BUILD/Meltype.app"
MOZC=../native/mozc/bin/mac
if [[ ! -x "$MOZC/meltype_mozc_helper" ]]; then
    echo "Mozc の変換ヘルパーがありません。先に native/mozc/build-mozc-helper.sh を実行してください。" >&2
    exit 1
fi
rm -rf "$BUILD/native" "$APP"
mkdir -p "$BUILD"

echo "== 1/3 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish ../src/Meltype.Mac.Native/Meltype.Mac.Native.csproj -c Release -r "$RID" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true -p:Version="$VERSION" -p:TreatWarningsAsErrors=true \
    --source https://api.nuget.org/v3/index.json -o "$BUILD/native"

echo "== 2/3 IME (Swift) をビルド"
swift build -c release

echo "== 3/3 Meltype.app を組み立て"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"
BIN="$(swift build -c release --show-bin-path)"
cp "$BIN/MeltypeIME" "$APP/Contents/MacOS/Meltype"
# 入力ソースの登録をやり直す小さな道具 (#134)。install.sh が入れ替えたあとに呼ぶ。
cp "$BIN/RegisterInputSource" "$APP/Contents/MacOS/MeltypeRegisterInputSource"
cp "$BIN/InstallSupport" "$APP/Contents/MacOS/MeltypeInstallSupport"
# Swift 6.2 以降は、古い macOS 向けの互換ライブラリ (libswiftCompatibilitySpan.dylib など) を @rpath で読む。
# macOS 26 は OS に入っているが、13〜15 では無いので、ツールチェーンから同梱する (#20)。
TOOLCHAIN_SWIFT_LIBS="$(dirname "$(xcrun --find swift)")/../lib"
while read -r lib; do
    name="${lib#@rpath/}"
    [[ "$name" == libswift*.dylib && ! -e "$APP/Contents/Frameworks/$name" ]] || continue
    for dylib in "$TOOLCHAIN_SWIFT_LIBS"/swift-*/macosx/"$name" "$TOOLCHAIN_SWIFT_LIBS"/swift/macosx/"$name"; do
        [[ -e "$dylib" ]] && { cp "$dylib" "$APP/Contents/Frameworks/"; break; }
    done
done < <(otool -L "$BIN/MeltypeIME" | awk '/@rpath\//{print $1}')
# 実行ファイルの隣 (@loader_path) だけでなく、Contents/Frameworks も探すようにする
install_name_tool -add_rpath "@executable_path/../Frameworks" "$APP/Contents/MacOS/Meltype" 2>/dev/null || true
# Xcode のツールチェーン内の Swift 互換ライブラリを先に見に行くと、配布・テスト環境で XProtect に止められることがある。
# 必要な互換ライブラリは Contents/Frameworks に同梱しているので、絶対パスの rpath は消しておく。
while read -r rpath; do
    [[ "$rpath" == /Applications/Xcode.app/*/usr/lib/swift* ]] || continue
    install_name_tool -delete_rpath "$rpath" "$APP/Contents/MacOS/Meltype" 2>/dev/null || true
done < <(otool -l "$APP/Contents/MacOS/Meltype" | awk '/LC_RPATH/{in_rpath=1; next} in_rpath && /path /{print $2; in_rpath=0}')
# @rpath で読み込むライブラリが全部 Contents/Frameworks にあるか確かめる (無ければ配布しない)
missing=0
while read -r lib; do
    name="${lib#@rpath/}"
    if [[ ! -e "$APP/Contents/Frameworks/$name" ]]; then echo "同梱されていないライブラリ: $lib" >&2; missing=1; fi
done < <(otool -L "$APP/Contents/MacOS/Meltype" | awk '/@rpath\//{print $1}')
[[ $missing -eq 0 ]] || { echo "Meltype.app に必要なライブラリが足りません" >&2; exit 1; }
cp "$BUILD/native/MeltypeNative.dylib" "$APP/Contents/Frameworks/libMeltypeNative.dylib"
cp Resources/Info.plist "$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
cp Resources/icon.tiff "$APP/Contents/Resources/icon.tiff"
# システム設定の入力ソースの一覧に出す名前
cp -R Resources/ja.lproj Resources/en.lproj "$APP/Contents/Resources/"
# Mozc の変換ヘルパー (本体が Contents/Resources/mozc/meltype_mozc_helper を起動する)
mkdir -p "$APP/Contents/Resources/mozc"
cp "$MOZC/meltype_mozc_helper" "$MOZC/MOZC-LICENSE.txt" "$MOZC/MOZC-CREDITS.html" "$APP/Contents/Resources/mozc/"
# Zenzai: llama.cpp の llama-server (MIT) と zenz-v3.2-small (Apache-2.0) を Contents/Resources/zenzai/ に同梱する (Windows の zenzai\ と同じ)。
# 版とハッシュは native/zenzai/zenzai.env (Windows と共通)。
# 取れないとき: リリースのビルド (タグ v*) なら止める (Zenzai は既定 ON なので、無いまま配らない)。
# 自分の Mac で使うビルドなら、同梱せずに続ける (Zenzai なしで、Mozc だけで変換する)
# shellcheck source=../native/zenzai/zenzai.env
source ../native/zenzai/zenzai.env
mkdir -p "$BUILD/cache"
# url・保存先・SHA-256。保存先のハッシュが合えば取り直さない。取れない・合わなければ消して 1 を返す
fetch_verified() {
    if [[ ! -f "$2" ]] || [[ "$(shasum -a 256 "$2" | cut -d' ' -f1)" != "$3" ]]; then
        curl -fsSL -o "$2" "$1" || { rm -f "$2"; return 1; }
    fi
    [[ "$(shasum -a 256 "$2" | cut -d' ' -f1)" == "$3" ]] || { rm -f "$2"; return 1; }
}
zenzai_missing() {
    if [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then
        echo "Zenzai の$1を取れませんでした。リリースのビルドなので止めます" >&2
        exit 1
    fi
    echo "警告: Zenzai の$1を取れなかったので、同梱しません (Zenzai なしで変換します)" >&2
}
ZENZAI="$APP/Contents/Resources/zenzai"
mkdir -p "$ZENZAI"
ZENZ_CACHE="$BUILD/cache/$ZENZ_NAME"
if fetch_verified "https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf/resolve/$ZENZ_COMMIT/ggml-model-Q5_K_M.gguf" "$ZENZ_CACHE" "$ZENZ_SHA256"; then
    cp "$ZENZ_CACHE" "$ZENZAI/$ZENZ_NAME"
    curl -fsSL -o "$ZENZAI/LICENSE-zenz.txt" https://www.apache.org/licenses/LICENSE-2.0.txt
    echo "Zenzai のモデルを同梱しました: $ZENZ_NAME"
else
    zenzai_missing "モデル"
fi
LLAMA_CACHE="$BUILD/cache/llama-$LLAMA_BUILD-macos-$LLAMA_ARCH.tar.gz"
if fetch_verified "https://github.com/ggml-org/llama.cpp/releases/download/$LLAMA_BUILD/llama-$LLAMA_BUILD-bin-macos-$LLAMA_ARCH.tar.gz" "$LLAMA_CACHE" "${!LLAMA_SHA}"; then
    rm -rf "$BUILD/llama"
    mkdir -p "$BUILD/llama"
    tar -xzf "$LLAMA_CACHE" -C "$BUILD/llama" --strip-components 1
    cp "$BUILD/llama/llama-server" "$ZENZAI/"
    cp "$BUILD/llama/LICENSE" "$ZENZAI/LICENSE-llama.cpp.txt"
    # llama-server が @rpath (@loader_path) で読むライブラリを、読む側から順にたどって隣に置く (版の付いた実体を、読む名前で)
    pending=(llama-server)
    while [[ ${#pending[@]} -gt 0 ]]; do
        file="${pending[0]}"
        pending=("${pending[@]:1}")
        while read -r lib; do
            name="${lib#@rpath/}"
            if [[ ! -e "$ZENZAI/$name" ]]; then
                cp -L "$BUILD/llama/$name" "$ZENZAI/$name"
                pending+=("$name")
            fi
        done < <(otool -L "$ZENZAI/$file" | awk '/@rpath\//{print $1}')
    done
    echo "Zenzai の llama-server を同梱しました: $LLAMA_BUILD"
else
    zenzai_missing "llama-server"
fi
# 同梱する実行ファイル・ライブラリの最低の macOS が、Meltype.app の LSMinimumSystemVersion より新しければ止める
# (Bazel でビルドした Mozc のヘルパーは、指定しないとビルドした Mac の SDK の版になり、古い macOS で起動できなかった)
MIN_OS="$(/usr/libexec/PlistBuddy -c "Print :LSMinimumSystemVersion" "$APP/Contents/Info.plist")"
version_number() { echo "$1" | awk -F. '{ printf "%d%03d%03d\n", $1, $2, $3 }'; }
too_new=0
while IFS= read -r -d '' item; do
    if ! file -b "$item" | grep -q Mach-O; then continue; fi
    minos="$(otool -l "$item" | awk '/LC_BUILD_VERSION|LC_VERSION_MIN_MACOSX/ { found = 1 } found && ($1 == "minos" || $1 == "version") { print $2; exit }')"
    if [[ -n "$minos" ]] && [[ "$(version_number "$minos")" -gt "$(version_number "$MIN_OS")" ]]; then
        echo "最低の macOS ($minos) が Meltype.app の $MIN_OS より新しい: ${item#"$APP"/}" >&2
        too_new=1
    fi
done < <(find "$APP/Contents" -type f -print0)
if [[ $too_new -ne 0 ]]; then exit 1; fi
# 署名: 環境変数 MELTYPE_MAC_IDENTITY (Developer ID Application の証明書の名前) があれば配布用に署名する
# (Hardened Runtime・タイムスタンプ付き。公証 (notarization) は mac.yml で行う)。無ければ自分の Mac で使うための署名。
# Contents/Resources の実行ファイル・ライブラリ (Mozc のヘルパー・llama-server) は --deep では署名されないので、1 つずつ署名する
nested=("$APP/Contents/Frameworks/"* "$APP/Contents/Resources/mozc/meltype_mozc_helper")
for item in "$ZENZAI"/llama-server "$ZENZAI"/*.dylib; do
    if [[ -e "$item" ]]; then nested+=("$item"); fi
done
if [[ -n "${MELTYPE_MAC_IDENTITY:-}" ]]; then
    for item in "${nested[@]}"; do
        codesign --force --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$item"
    done
    codesign --force --deep --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$APP"
    echo "配布用に署名しました: $MELTYPE_MAC_IDENTITY"
else
    for item in "${nested[@]}"; do
        codesign --force --sign - "$item"
    done
    codesign --force --deep --sign - "$APP"
fi
echo "作成しました: $APP"

if [[ $INSTALL -eq 1 ]]; then
    TARGET="$HOME/Library/Input Methods"
    bash "$PWD/install-app.sh" "$APP" "$TARGET/Meltype.app"
    echo "インストールしました: $TARGET/Meltype.app"
    # Keep the build copy out of the input-source add dialog after installing.
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$PWD/$APP" 2>/dev/null || true
    echo "初めてのときは、いったんログアウトしてログインし直してから、"
    echo "システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加してください。"
fi
