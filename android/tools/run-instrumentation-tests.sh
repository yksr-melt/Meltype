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
adb install -r "$APP"
adb install -r "$TEST"
PACKAGE=io.github.ninjin0802.keyboard.bilingualtest
adb shell pm path "$PACKAGE" | grep -F 'package:'
adb shell am start -W -n "$PACKAGE/io.github.ninjin0802.keyboard.app.MainActivity"
status=0
adb shell am instrument -w -r -e class \
  io.github.ninjin0802.keyboard.app.core.LanguageDetectionDeviceTest,io.github.ninjin0802.keyboard.app.SettingsScreenDeviceTest \
  "$PACKAGE.test/androidx.test.runner.AndroidJUnitRunner" \
  | tee "$REPORT/instrumentation.log" || status=1
python3 "$ROOT/tools/instrumentation-report.py" \
  --log "$REPORT/instrumentation.log" \
  --device "$REPORT/device-info.txt" \
  --sources "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/LanguageDetectionDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/SettingsScreenDeviceTest.kt" \
  --output "$REPORT" || status=1
exit "$status"
