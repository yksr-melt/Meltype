#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs

# Launch through macOS application services, never as a manual launchd daemon.
set -euo pipefail
app="${1:?Usage: start-input-method.sh /path/to/Meltype.app [install-support]}"
helper="${2:-$app/Contents/MacOS/MeltypeInstallSupport}"
[[ -x "$helper" ]] || { echo "Missing installer helper: $helper" >&2; exit 1; }
stage="$(mktemp -d "${TMPDIR:-/tmp}/meltype-start.XXXXXX")"
cleanup() {
    local result=$?
    trap - EXIT
    if [[ $result -ne 0 && -f "$stage/input-state.json" ]]; then
        "$stage/install-support" --restore "$app" "$stage/input-state.json" ||
            echo "Could not restore the previous input session." >&2
    fi
    rm -rf "$stage"
    exit "$result"
}
trap cleanup EXIT
# Keep the CLI helper outside the input-method bundle while using AppKit.
cp -X "$helper" "$stage/install-support"
/usr/bin/xattr -drs com.apple.quarantine "$stage/install-support"
manual_job=io.github.yksr-melt.Meltype.manual
if launchctl list "$manual_job" >/dev/null 2>&1; then
    "$stage/install-support" --stop "$app" "$app" "$stage/input-state.json" "$manual_job"
    restore_status=0
    "$stage/install-support" --restore "$app" "$stage/input-state.json" || restore_status=$?
    if [[ $restore_status -eq 2 ]]; then
        echo "Meltype は起動しました。入力メニューから選び直してください。" >&2
    elif [[ $restore_status -ne 0 ]]; then
        exit "$restore_status"
    fi
fi
"$stage/install-support" --start "$app"
