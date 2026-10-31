#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype の Mac 版をビルドして ~/Library/Input Methods にインストールする。
#   ./build.sh            ビルドしてインストール
#   ./build.sh --no-install  ビルドだけ (build/Meltype.app)
# 必要なもの: macOS 13 以降、Xcode (またはコマンドライン ツール: xcode-select --install)、.NET 10 SDK
set -euo pipefail
cd "$(dirname "$0")"

INSTALL=1
# 入力ソースの「+」の一覧に Meltype が出ない Mac がある (自分で署名した版、macOS 26、#21)。
# アプリ内の登録処理で、有効な入力ソースを追加し、Meltype の重複だけを整理する。
enable_input_source() {
    "$TARGET/Meltype.app/Contents/MacOS/Meltype" --register-input-source
    echo "Meltype の入力ソースの登録処理を実行しました"
}

# 入力ソースの登録をやり直す (#134)。Meltype.app を入れ替えるとき、バンドルが無い一瞬に入力ソースの
# 走査が走ると、macOS は登録を消す。消えたあとは、バンドルを戻して走査し直しても戻らないことがあり、
# これまではログアウトするしかなかった。TISRegisterInputSource なら実行中でも戻せる。
register_input_source() {
    local tool="$TARGET/Meltype.app/Contents/MacOS/MeltypeRegisterInputSource"
    "$tool" "$TARGET/Meltype.app" ||
        echo "入力ソースを登録できませんでした。システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype から追加してください。それでも出てこなければ、ログアウトしてログインし直してください。" >&2
}
[[ "${1:-}" == "--no-install" ]] && INSTALL=0

case "$(uname -m)" in
    arm64) RID=osx-arm64 ;;
    x86_64) RID=osx-x64 ;;
    *) echo "対応していない CPU です: $(uname -m)" >&2; exit 1 ;;
esac

BUILD=build
VERSION="${MELTYPE_VERSION:-$(dotnet msbuild ../src/Meltype.Core/Meltype.Core.csproj -nologo -getProperty:Version)}"
if [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then VERSION="${GITHUB_REF_NAME#v}"; fi
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$ ]] || { echo "版番号が不正です: $VERSION" >&2; exit 1; }
APP="$BUILD/Meltype.app"
rm -rf "$BUILD/native" "$APP"
mkdir -p "$BUILD"

echo "== 1/3 本体 (C#, NativeAOT) をビルド"
# リポジトリの nuget.config は NuGet を使わない設定 (Windows の開発環境用) なので、NativeAOT のコンパイラを取るために nuget.org を指定する。
dotnet publish ../src/Meltype.Mac.Native/Meltype.Mac.Native.csproj -c Release -r "$RID" \
    -p:PublishAot=true -p:NativeLib=Shared -p:StripSymbols=true -p:Version="$VERSION" \
    --source https://api.nuget.org/v3/index.json -o "$BUILD/native"

echo "== 2/3 IME (Swift) をビルド (初回は azooKey の変換エンジンと辞書のダウンロードに時間がかかります)"
swift build -c release

echo "== 3/3 Meltype.app を組み立て"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$APP/Contents/Frameworks"
BIN="$(swift build -c release --show-bin-path)"
cp "$BIN/MeltypeIME" "$APP/Contents/MacOS/Meltype"
# 入力ソースの登録をやり直す小さな道具 (#134)。install.sh が入れ替えたあとに呼ぶ。
cp "$BIN/RegisterInputSource" "$APP/Contents/MacOS/MeltypeRegisterInputSource"
# azooKey が使う llama.framework などの動的なフレームワークも同梱する。
# 入れていなかったため、1.0.0 は起動できなかった (dyld: Library not loaded: @rpath/llama.framework、#13)。
for framework in "$BIN"/*.framework; do
    [[ -e "$framework" ]] && cp -R "$framework" "$APP/Contents/Frameworks/"
done
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
# azooKey の辞書などのリソース (Swift Package のリソースバンドル)
for bundle in "$BIN"/*.bundle; do
    [[ -e "$bundle" ]] && cp -R "$bundle" "$APP/Contents/Resources/"
done
# 署名: 環境変数 MELTYPE_MAC_IDENTITY (Developer ID Application の証明書の名前) があれば配布用に署名する
# (Hardened Runtime・タイムスタンプ付き。公証 (notarization) は mac.yml で行う)。無ければ自分の Mac で使うための署名。
if [[ -n "${MELTYPE_MAC_IDENTITY:-}" ]]; then
    for item in "$APP/Contents/Frameworks/"*; do
        codesign --force --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$item"
    done
    codesign --force --deep --sign "$MELTYPE_MAC_IDENTITY" --options runtime --timestamp "$APP"
    echo "配布用に署名しました: $MELTYPE_MAC_IDENTITY"
else
    codesign --force --deep --sign - "$APP"
fi
echo "作成しました: $APP"

if [[ $INSTALL -eq 1 ]]; then
    TARGET="$HOME/Library/Input Methods"
    bash "$PWD/install-app.sh" "$APP" "$TARGET/Meltype.app"
    echo "インストールしました: $TARGET/Meltype.app"
    register_input_source
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$TARGET/Meltype.app"
    # Keep the build copy out of the input-source add dialog after installing.
    /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$PWD/$APP" 2>/dev/null || true
    if [[ "${MELTYPE_SKIP_START:-0}" != 1 ]]; then
        bash "$PWD/start-input-method.sh" "$TARGET/Meltype.app"
    fi
    echo "初めてのときは、いったんログアウトしてログインし直してから、"
    echo "システム設定 → キーボード → 入力ソース →「編集…」→「+」→ 日本語 → Meltype を追加してください。"
fi
