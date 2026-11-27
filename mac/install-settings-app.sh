#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltype Contributors
#
# 設定アプリ (Meltype Settings.app) を ~/Applications に入れる (Launchpad・Spotlight に出し、入力メニューの「Meltype の設定…」が bundle ID で見つける)。
# 管理者権限を使わないので /Applications ではなく ~/Applications に入れる。Homebrew の cask は、cask の app で /Applications に入れる。
#   install-settings-app.sh "Meltype Settings.app"
set -euo pipefail
source_app="$1"
[[ -d "$source_app" ]] || { echo "Missing app: $source_app" >&2; exit 1; }
target="$HOME/Applications/Meltype Settings.app"
codesign --verify --deep --strict "$source_app"
mkdir -p "$HOME/Applications"
# 開いている設定アプリは、入れ替える前に閉じる
osascript -e 'tell application id "io.github.yksr-melt.Meltype.Settings" to quit' > /dev/null 2>&1 || true
rm -rf "$target"
ditto "$source_app" "$target"
/usr/bin/xattr -drs com.apple.quarantine "$target"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$target"
echo "インストールしました: $target"
