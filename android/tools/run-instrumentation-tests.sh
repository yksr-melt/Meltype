#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 KoheiTakada
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP="$ROOT/app/build/outputs/apk/debug/app-debug.apk"
TEST="$ROOT/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk"
REPORT="$ROOT/build/qa"
mkdir -p "$REPORT"
test -s "$APP" && test -s "$TEST" || {
  echo 'Missing APKs. Run ./gradlew :app:assembleDebug :app:assembleDebugAndroidTest' >&2; exit 2;
}
command -v adb >/dev/null
adb wait-for-device
{
  printf 'source_sha='
  git -C "$ROOT/.." rev-parse HEAD
  printf 'model='
  adb shell getprop ro.product.model
  printf 'android_release='
  adb shell getprop ro.build.version.release
  printf 'api='
  adb shell getprop ro.build.version.sdk
  printf 'abi='
  adb shell getprop ro.product.cpu.abi
  adb shell wm size
  adb shell wm density
} > "$REPORT/device-info.txt"
# Read both app IDs from the APK manifests. The Java/Kotlin namespace is
# independent from applicationId and must not be used as the installed package.
AAPT="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-}}/build-tools/36.0.0/aapt"
test -x "$AAPT" || { echo "aapt not found at $AAPT" >&2; exit 2; }
read_apk_package() {
  "$AAPT" dump badging "$1" | sed -n "s/^package: name='\\([^']*\\)'.*/\\1/p" | head -n 1
}
PACKAGE="$(read_apk_package "$APP")"
TEST_PACKAGE="$(read_apk_package "$TEST")"
test -n "$PACKAGE" && test -n "$TEST_PACKAGE" && test "$TEST_PACKAGE" = "$PACKAGE.test" || {
  echo "APK app / test package mismatch: app=$PACKAGE test=$TEST_PACKAGE" >&2
  exit 2
}
printf 'app_id=%s\ntest_id=%s\n' "$PACKAGE" "$TEST_PACKAGE" >> "$REPORT/device-info.txt"
adb install -r "$APP"
adb install -r "$TEST"
adb shell pm path "$PACKAGE" | grep -F 'package:'
adb shell am start -W -n "$PACKAGE/io.github.ninjin0802.keyboard.app.MainActivity"
status=0
: > "$REPORT/instrumentation.log"
run_group() {
  adb shell am instrument -w -r -e class "$1" \
    "$TEST_PACKAGE/androidx.test.runner.AndroidJUnitRunner" \
    | tee -a "$REPORT/instrumentation.log" || status=1
}
# Native Core fixes its data directory for the lifetime of a process. The
# bridge test uses a different directory, so run it in a fresh process.
run_group 'io.github.ninjin0802.keyboard.app.core.LanguageDetectionDeviceTest,io.github.ninjin0802.keyboard.app.SettingsScreenDeviceTest,io.github.ninjin0802.keyboard.app.DeleteLengthDeviceTest,io.github.ninjin0802.keyboard.app.KeyboardViewTouchTest'
run_group 'io.github.ninjin0802.keyboard.app.core.MeltypeCoreBridgeDeviceTest'
python3 "$ROOT/tools/instrumentation-report.py" \
  --log "$REPORT/instrumentation.log" \
  --device "$REPORT/device-info.txt" \
  --expected-runs 2 \
  --sources "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/LanguageDetectionDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/SettingsScreenDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/DeleteLengthDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/KeyboardViewTouchTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/MeltypeCoreBridgeDeviceTest.kt" \
  --output "$REPORT" || status=1
exit "$status"
