#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 KoheiTakada
set -euo pipefail

# Builds the SAME C# shared Core used by the Mac/Linux front ends for Android.
# Usage from anywhere: ANDROID_NDK_HOME=/path/to/ndk bash android/tools/build-meltype-native.sh
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NDK="${ANDROID_NDK_HOME:-${ANDROID_NDK_ROOT:-}}"
if [[ ! -d "$NDK/toolchains/llvm/prebuilt" ]]; then
  echo "Set ANDROID_NDK_HOME to an installed Android NDK directory" >&2; exit 1
fi
command -v dotnet >/dev/null || { echo "Install .NET 10 SDK" >&2; exit 1; }
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) HOST=linux-x86_64 ;;
  Darwin-arm64) HOST=darwin-x86_64 ;;
  Darwin-x86_64) HOST=darwin-x86_64 ;;
  *) echo "Unsupported build host; extend toolchain host mapping" >&2; exit 1 ;;
esac
TOOLCHAIN="$NDK/toolchains/llvm/prebuilt/$HOST/bin"
[[ -d "$TOOLCHAIN" ]] || { echo "NDK toolchain directory missing: $TOOLCHAIN" >&2; exit 1; }

for spec in "arm64-v8a:linux-bionic-arm64:aarch64-linux-android" "x86_64:linux-bionic-x64:x86_64-linux-android"; do
  IFS=: read -r ABI RID TRIPLE <<< "$spec"
  echo "Building $ABI ($RID) ..."
  # NativeAOT uses the NDK clang/LLVM linker. Match the app's minSdk 26.
  # Root nuget.config clears package sources for offline desktop builds; the
  # NativeAOT compiler/runtime packs must be restored from official NuGet.
  CC="$TOOLCHAIN/${TRIPLE}26-clang" \
  CXX="$TOOLCHAIN/${TRIPLE}26-clang++" \
  PATH="$TOOLCHAIN:$PATH" \
  dotnet publish "$ROOT/src/Meltype.Mac.Native/Meltype.Mac.Native.csproj" \
    -c Release -r "$RID" \
    -p:PublishAot=true -p:NativeLib=Shared \
    -p:DisableUnsupportedError=true -p:PublishAotUsingRuntimePack=true \
    -p:AndroidApiLevelMin=26 -p:MeltypeAndroidNative=true \
    -p:RestoreSources=https://api.nuget.org/v3/index.json
  OUTPUT_DIR="$ROOT/src/Meltype.Mac.Native/bin/Release/net10.0/$RID/publish"
ls -lah "$OUTPUT_DIR"
SOURCE="$(find "$OUTPUT_DIR" -maxdepth 1 -type f -name "*.so" -print -quit)"
  [[ -f "$SOURCE" ]] || { echo "NativeAOT output not found: $SOURCE" >&2; exit 1; }
  DEST="$ROOT/android/app/prebuilt/$ABI"
  mkdir -p "$DEST"
  cp "$SOURCE" "$DEST/libMeltypeNative.so"
  # Android dynamic linker resolves DT_NEEDED by soname, not CI filesystem path.
  # Without an explicit SONAME, CMake records the absolute imported path
  # in libmeltype_jni.so and the IME fails to load on real Android devices.
  command -v patchelf >/dev/null || { echo "Install patchelf for Android SONAME" >&2; exit 1; }
  # SONAME is set by the native linker. patchelf corrupts Android ELF PT_LOAD alignment.  readelf -d "$DEST/libMeltypeNative.so" | grep -q 'Library soname: \[libMeltypeNative.so\]'
  echo "Wrote $DEST/libMeltypeNative.so"
done
