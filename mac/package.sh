#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs

# ビルド済みのアプリから ZIP または DMG を作る。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(dirname "$here")"
[[ -d "$here/build/Meltype.app" && -d "$here/build/Meltype Settings.app" ]] || { echo "Run mac/build-cli.sh --build first." >&2; exit 1; }
name="${1:-Meltype-mac-test-$(date +%Y%m%d-%H%M%S).zip}"
[[ "$name" != */* && ( "$name" == *.zip || "$name" == *.dmg ) ]] || {
    echo "ZIP または DMG のファイル名を指定してください。" >&2
    exit 1
}
mkdir -p "$root/dist"
stage="$(mktemp -d "$root/dist/.meltype-package.XXXXXX")"
package="$stage/Meltype-mac"
apps="$package"
installer=
volume="$stage/volume"
if [[ "$name" == *.dmg ]]; then
    installer="$volume/Meltype をインストール.app"
    package="$installer/Contents/Resources/Meltype-mac"
    apps="$installer/Contents/Helpers"
fi
cleanup() {
    for app in "$apps/Meltype.app" "$apps/Meltype Settings.app" "$installer"; do
        if [[ -d "$app" ]]; then
            /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$app" 2>/dev/null || true
        fi
    done
    rm -rf "$stage"
}
trap cleanup EXIT
mkdir -p "$package" "$apps"
for app in Meltype.app "Meltype Settings.app"; do
    ditto "$here/build/$app" "$apps/$app"
done
cp "$here/install.sh" "$here/install-app.sh" "$here/install-settings-app.sh" "$here/start-input-method.sh" "$package/"
cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$package/"
chmod +x "$package/install.sh" "$package/start-input-method.sh" "$package/install-settings-app.sh"

if [[ "$name" == *.zip ]]; then
    cp "$here/Install Meltype.command" "$package/"
    cp "$here/TESTER-README.txt" "$package/はじめにお読みください.txt"
    chmod +x "$package/Install Meltype.command"
    ditto -c -k --keepParent "$package" "$root/dist/$name"
else
    # アプリを署名対象の Helpers に置き、install.sh が使う配置はバンドル内のリンクで作る。
    for app in Meltype.app "Meltype Settings.app"; do
        ln -s "../../Helpers/$app" "$package/$app"
    done
    mkdir -p "$installer/Contents/MacOS"
    plist=/usr/libexec/PlistBuddy
    installed_info="$apps/Meltype.app/Contents/Info.plist"
    installer_info="$installer/Contents/Info.plist"
    cp "$here/Installer/Info.plist" "$installer_info"
    for key in CFBundleShortVersionString CFBundleVersion LSMinimumSystemVersion; do
        value="$("$plist" -c "Print :$key" "$installed_info")"
        "$plist" -c "Set :$key $value" "$installer_info"
    done
    min_os="$("$plist" -c 'Print :LSMinimumSystemVersion' "$installer_info")"
    arch="$(lipo -archs "$apps/Meltype.app/Contents/MacOS/Meltype")"
    sips -s format icns "$here/Resources/Settings/icon.png" --out "$installer/Contents/Resources/Installer.icns" >/dev/null
    xcrun swiftc -parse-as-library -swift-version 5 -target "$arch-apple-macos$min_os" \
        "$here/Installer/main.swift" -o "$installer/Contents/MacOS/MeltypeInstaller"

    # Developer ID の署名・公証はしないので、インストーラーはアドホック署名にし、DMG は署名しない。
    codesign --force --sign - "$installer"
    codesign --verify --deep --strict "$installer"
    image="$stage/$name"
    hdiutil create -srcfolder "$volume" -volname "Meltype インストーラー" -format UDZO "$image"
    hdiutil verify "$image"
    mv "$image" "$root/dist/$name"
fi
echo "Created: $root/dist/$name"
