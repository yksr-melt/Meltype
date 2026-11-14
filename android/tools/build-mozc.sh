#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 ninjin0802
#
# Mozc（かな漢字変換）の JNI ライブラリと辞書をビルドして :mozc モジュールに置く。
#
#   使い方: bash tools/build-mozc.sh
#
# 必要なもの: Linux x86_64、git、curl、python3、C++ コンパイラ。
# 使われる NDK の版: r29（29.0.14206865）。Mozc の update_deps.py が自分で取得するもので、
# Android SDK に入っている NDK や ANDROID_NDK_HOME は使われない。
#
# ネットワークへは、Bazelisk、Mozc、Mozc が宣言している依存物の取得のためだけに出る。
set -euo pipefail

BAZELISK_VERSION="v1.27.0"
BAZELISK_URL="https://github.com/bazelbuild/bazelisk/releases/download/${BAZELISK_VERSION}/bazelisk-linux-amd64"
# GitHub のリリース（bazelbuild/bazelisk v1.27.0）が資産 bazelisk-linux-amd64 に付けている SHA-256。
BAZELISK_SHA256="e1508323f347ad1465a887bc5d2bfb91cffc232d11e8e997b623227c6b32fb76"
MOZC_URL="https://github.com/google/mozc.git"
# アプリの minSdk（Android 8.0）と同じにする。
ANDROID_API_LEVEL=26

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$ROOT/.mozc-build"
BAZELISK="$WORK/bazelisk"
MOZC_DIR="$WORK/mozc"
MOZC_COMMIT="$(tr -d '[:space:]' < "$ROOT/mozc/MOZC_COMMIT")"

JNI_DIR="$ROOT/mozc/src/main/jniLibs"
ASSETS_DIR="$ROOT/mozc/src/main/assets"
PROTO_DIR="$ROOT/mozc/src/main/proto/protocol"
# できたものはまずここに集め、全部そろってから上の場所に移す。
STAGE="$WORK/stage"

# $1 のファイルが固定した Bazelisk と同じ中身かを確かめる。違えば消して止める。
verify_bazelisk() {
  local actual
  actual="$(sha256sum "$1" | cut -d ' ' -f 1)"
  if [ "$actual" != "$BAZELISK_SHA256" ]; then
    rm -f "$1"
    echo "Bazelisk の SHA-256 が合いません（期待 $BAZELISK_SHA256、実際 $actual）。$1 を消しました。" >&2
    return 1
  fi
}

# $1（Mozc の MODULE.bazel）の NDK の設定に api_level を足す。足せたことを確かめ、だめなら止める。
set_api_level() {
  local ndk_line='^( *)path = "\$WORKSPACE_ROOT/third_party/ndk/android-ndk-r29",$'
  if [ "$(grep -c -E "$ndk_line" "$1")" != 1 ]; then
    echo "Mozc の MODULE.bazel に NDK の設定の行がちょうど1つは見つかりません。Mozc の版が変わったなら、この手順を見直してください。" >&2
    return 1
  fi
  sed -i -E "s|$ndk_line|&\\n\\1api_level = $ANDROID_API_LEVEL,|" "$1"
  if [ "$(grep -c -E "^ *api_level = $ANDROID_API_LEVEL,\$" "$1")" != 1 ]; then
    echo "Mozc の MODULE.bazel に api_level = $ANDROID_API_LEVEL を足せませんでした。API 31 向けのライブラリができてしまうので止めます。" >&2
    return 1
  fi
}

# $1（集めた場所）の中身がそろっていることを確かめてから、古い成果物を消して新しいものを移す。
install_outputs() {
  local stage="$1" f
  for f in jniLibs/arm64-v8a/libmozc.so jniLibs/x86_64/libmozc.so assets/mozc.data assets/MOZC_NOTICES.txt; do
    if [ ! -s "$stage/$f" ]; then
      echo "成果物 $f ができていません。置いてある成果物は変えていません。" >&2
      return 1
    fi
  done
  if ! compgen -G "$stage/proto/*.proto" > /dev/null; then
    echo "命令定義（.proto）がありません。置いてある成果物は変えていません。" >&2
    return 1
  fi
  # ここから先で止まっても、古いものと新しいものが混ざったまま「そろっている」ようには見えない
  # （先に全部消すので、足りないものは Gradle の checkMozcArtifacts が見つける）。
  rm -f "$JNI_DIR/arm64-v8a/libmozc.so" "$JNI_DIR/x86_64/libmozc.so" \
    "$ASSETS_DIR/mozc.data" "$ASSETS_DIR/MOZC_NOTICES.txt" "$PROTO_DIR"/*.proto
  mkdir -p "$JNI_DIR/arm64-v8a" "$JNI_DIR/x86_64" "$ASSETS_DIR" "$PROTO_DIR"
  mv "$stage/jniLibs/arm64-v8a/libmozc.so" "$JNI_DIR/arm64-v8a/libmozc.so"
  mv "$stage/jniLibs/x86_64/libmozc.so" "$JNI_DIR/x86_64/libmozc.so"
  mv "$stage/assets/mozc.data" "$ASSETS_DIR/mozc.data"
  mv "$stage/assets/MOZC_NOTICES.txt" "$ASSETS_DIR/MOZC_NOTICES.txt"
  mv "$stage"/proto/*.proto "$PROTO_DIR/"
}

mkdir -p "$WORK"
rm -rf "$STAGE"
mkdir -p "$STAGE/jniLibs/arm64-v8a" "$STAGE/jniLibs/x86_64" "$STAGE/assets" "$STAGE/proto"

# 1. Bazelisk（取得したときも、前のものを使うときも、中身を確かめてから実行する）
if [ ! -f "$BAZELISK" ]; then
  echo "== Bazelisk ${BAZELISK_VERSION} を取得"
  curl --fail --location --silent --show-error "$BAZELISK_URL" -o "$BAZELISK.tmp"
  verify_bazelisk "$BAZELISK.tmp"
  mv "$BAZELISK.tmp" "$BAZELISK"
fi
verify_bazelisk "$BAZELISK"
chmod +x "$BAZELISK"

# 2. Mozc の取得と版の固定
if [ ! -d "$MOZC_DIR/.git" ]; then
  echo "== Mozc を取得"
  git clone --filter=blob:none "$MOZC_URL" "$MOZC_DIR"
fi
if ! git -C "$MOZC_DIR" cat-file -e "${MOZC_COMMIT}^{commit}" 2>/dev/null; then
  git -C "$MOZC_DIR" fetch origin
fi
echo "== Mozc ${MOZC_COMMIT} を checkout"
# 前回このスクリプトが書き換えた分を戻してから版を合わせる。
git -C "$MOZC_DIR" checkout --quiet -- src/MODULE.bazel 2>/dev/null || true
git -C "$MOZC_DIR" checkout --quiet "$MOZC_COMMIT"

# 3. NDK と、対象にする Android の版
# Mozc は自分の NDK（r29）を update_deps.py で third_party/ndk/ に取得して使い、
# ANDROID_NDK_HOME は見ない。そのままだと API 31 向けにビルドされ、Android 11 以前では
# 読み込めないライブラリになるので、このアプリの minSdk に合わせる。
set_api_level "$MOZC_DIR/src/MODULE.bazel"

cd "$MOZC_DIR/src"

# 4. Mozc の依存物
echo "== Mozc の依存物を取得"
python3 build_tools/update_deps.py

# 5. 辞書
echo "== 辞書をビルド"
"$BAZELISK" build --config oss_linux --config release_build //data_manager/oss:mozc.data
# Android 用のビルドで bazel-bin の指す先が変わるので、ここで場所を確定して写す。
data_file="$("$BAZELISK" cquery --config oss_linux --config release_build \
  //data_manager/oss:mozc.data --output=files | tail -n 1)"
cp -f "$data_file" "$STAGE/assets/mozc.data"

# 6. JNI ライブラリ
echo "== JNI ライブラリをビルド"
"$BAZELISK" build --config oss_android --config release_build \
  //android/jni:mozc.arm64 //android/jni:mozc.x86_64

# 7. 成果物を集める
arm64_file="$("$BAZELISK" cquery --config oss_android --config release_build \
  //android/jni:mozc.arm64 --output=files | tail -n 1)"
x86_64_file="$("$BAZELISK" cquery --config oss_android --config release_build \
  //android/jni:mozc.x86_64 --output=files | tail -n 1)"
cp -f "$arm64_file" "$STAGE/jniLibs/arm64-v8a/libmozc.so"
cp -f "$x86_64_file" "$STAGE/jniLibs/x86_64/libmozc.so"
# Mozc のビルドはデバッグ情報を残すので、ここで落とす（Gradle 側では落とさない）。
strip="$MOZC_DIR/src/third_party/ndk/android-ndk-r29/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-strip"
"$strip" --strip-unneeded "$STAGE/jniLibs/arm64-v8a/libmozc.so" "$STAGE/jniLibs/x86_64/libmozc.so"

# 8. 命令定義（.proto）
cp -f protocol/*.proto "$STAGE/proto/"

# 9. ライセンスの表示
# credits_en.html には、Mozc に組み込まれているソフトウェア（Protocol Buffers など）の
# ライセンスの全文が入っている。THIRD-PARTY-NOTICES.md がそれを当てにしているので確かめる。
credits="data/installer/credits_en.html"
if [ ! -f "$credits" ]; then
  echo "Mozc のクレジット（src/$credits）がありません。ライセンスの表示を作れないので止めます。" >&2
  exit 1
fi
if ! grep -q -F 'Protocol Buffers is licensed as follows:' "$credits"; then
  echo "Mozc のクレジットに Protocol Buffers のライセンスが見つかりません。THIRD-PARTY-NOTICES.md と合わなくなるので止めます。" >&2
  exit 1
fi
{
  cat "$MOZC_DIR/LICENSE"
  printf '\n\n'
  cat "$credits"
} > "$STAGE/assets/MOZC_NOTICES.txt"

# 10. そろったものを置く
chmod 644 "$STAGE"/jniLibs/*/libmozc.so "$STAGE"/assets/* "$STAGE"/proto/*.proto
install_outputs "$STAGE"
rm -rf "$STAGE"

echo "== できたもの"
for f in "$JNI_DIR/arm64-v8a/libmozc.so" "$JNI_DIR/x86_64/libmozc.so" \
         "$ASSETS_DIR/mozc.data" "$ASSETS_DIR/MOZC_NOTICES.txt"; do
  printf '%12d バイト  %s\n' "$(stat -c %s "$f")" "${f#"$ROOT"/}"
done
