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

# Validate the installed InputMethodService, not just view construction.
# "ime list -s" shows only enabled IMEs on some Android releases, so
# include disabled IMEs with -a before first enabling Meltype.
IME="$PACKAGE/io.github.ninjin0802.keyboard.app.KeyboardService"
adb shell ime list -a -s | tr -d '\r' | tee "$REPORT/installed-ime-list.txt"
if ! grep -Fx "$IME" "$REPORT/installed-ime-list.txt"; then
  echo "APK did not register its InputMethodService: $IME" >&2
  adb shell dumpsys input_method > "$REPORT/ime-registration-diagnostic.txt" || true
  status=1
else
  previous_ime="$(adb shell settings get secure default_input_method | tr -d '\r')"
  other_ime="$(adb shell ime list -a -s | tr -d '\r' | grep -Fxv "$IME" | head -1 || true)"
  if test -z "$other_ime"; then
    echo "No alternative keyboard exists for safe test recovery" >&2
    status=1
  else
    recover_ime() {
      adb shell ime enable "$other_ime" >/dev/null 2>&1 || true
      if test -n "$previous_ime" && test "$previous_ime" != "null"; then
        adb shell ime set "$previous_ime" >/dev/null 2>&1 ||
          adb shell ime set "$other_ime" >/dev/null 2>&1 || true
      else
        adb shell ime set "$other_ime" >/dev/null 2>&1 || true
      fi
    }
    trap recover_ime EXIT
    if adb shell ime enable "$IME" && adb shell ime set "$IME"; then
      selected_ime="$(adb shell settings get secure default_input_method | tr -d '\r')"
      if test "$selected_ime" = "$IME"; then
        adb logcat -c || true
        run_group 'io.github.ninjin0802.keyboard.app.ImeLaunchDeviceTest'
      else
        echo "Android did not select requested IME: $selected_ime" >&2
        status=1
      fi
    else
      echo "Android refused to enable/select the installed IME" >&2
      status=1
    fi
    adb shell dumpsys input_method > "$REPORT/ime-window-state.txt" || true
    adb logcat -d -v threadtime -s AndroidRuntime:E > "$REPORT/ime-fatal-logcat.txt" || true
    # Print the IME state into the job log too, so a failure can be read
    # without downloading the artifact.
    echo '--- IME state after the on-screen typing test'
    grep -E 'mCurMethodId|mCurId=|mInputShown|mShowRequested|mCurFocusedWindow|mBoundToMethod|mHaveConnection' \
      "$REPORT/ime-window-state.txt" | head -n 20 || true
    adb logcat -d -v time -s KeyboardService:V InputMethodManagerService:V AndroidRuntime:E \
      > "$REPORT/ime-logcat.txt" || true
    tail -n 40 "$REPORT/ime-logcat.txt" || true
    if grep -Fq "Process: $PACKAGE," "$REPORT/ime-fatal-logcat.txt"; then
      echo "Fatal keyboard exception detected; see ime-fatal-logcat.txt" >&2
      status=1
    fi
    recover_ime
    trap - EXIT
  fi
fi
python3 "$ROOT/tools/instrumentation-report.py" \
  --log "$REPORT/instrumentation.log" \
  --device "$REPORT/device-info.txt" \
  --expected-runs 3 \
  --sources "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/LanguageDetectionDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/SettingsScreenDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/DeleteLengthDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/KeyboardViewTouchTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/core/MeltypeCoreBridgeDeviceTest.kt" \
            "$ROOT/app/src/androidTest/kotlin/io/github/ninjin0802/keyboard/app/ImeLaunchDeviceTest.kt" \
  --output "$REPORT" || status=1
exit "$status"
