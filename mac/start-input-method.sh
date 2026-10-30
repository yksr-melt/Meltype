#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs

# Start in the GUI launchd session so terminal teardown cannot stop the IME.
set -euo pipefail
app="${1:?Usage: start-input-method.sh /path/to/Meltype.app}"
label=io.github.yksr-melt.Meltype.manual
log_dir="${MELTYPE_START_LOG_DIR:-$HOME/Library/Logs}"
mkdir -p "$log_dir"
launchctl remove "$label" 2>/dev/null || true
# 入力ソースが Meltype のままだと、入れ替えた直後に macOS (TextInputMenuAgent など) が先に起動することがある。
# そのプロセスが接続名を持っていれば、こちらから起動したものは接続を登録できない。すでに動いていれば準備完了とみなす。
already_running() {
    pgrep -f "^$app/Contents/MacOS/Meltype\$" >/dev/null 2>&1
}
for attempt in {1..3}; do
    if already_running; then
        echo "Meltype input server is running (started by macOS)."
        exit 0
    fi
    if launchctl submit -l "$label" -o "$log_dir/Meltype.stdout.log" -e "$log_dir/Meltype.stderr.log" -- "$app/Contents/MacOS/Meltype"; then
        for _ in {1..30}; do
            state="$(launchctl list "$label" 2>/dev/null || true)"
            if [[ "$state" == *'"PID" ='* && "$state" == *'Meltype_Connection'* ]]; then
                echo "Meltype input server is running."
                exit 0
            fi
            sleep 0.2
        done
    fi
    echo "Meltype startup attempt $attempt did not become ready." >&2
    launchctl remove "$label" 2>/dev/null || true
    sleep 0.2
done
echo "Meltype input server did not become ready. Keep the standard input source selected." >&2
exit 1
