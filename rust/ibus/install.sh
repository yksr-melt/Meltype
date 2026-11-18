#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 appipinopi
# Explicit installation. Never restart IBus or select an input source.
set -euo pipefail
source_dir="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
case "$data" in /*) ;; *) echo 'XDG_DATA_HOME must be absolute.' >&2; exit 1 ;; esac
system=false
prefix=/usr
uninstall=false
keyboard_layout=jp
while [[ $# -gt 0 ]]; do
    case "$1" in
        --system) system=true; shift ;;
        --prefix) [[ $# -ge 2 ]] || exit 1; prefix="$2"; shift 2 ;;
        --uninstall) uninstall=true; shift ;;
        --keyboard-layout) [[ $# -ge 2 ]] || exit 1; keyboard_layout="$2"; shift 2 ;;
        *) echo 'Usage: bash install.sh [--system [--prefix /absolute/path]] [--keyboard-layout jp|us|default] [--uninstall]' >&2; exit 1 ;;
    esac
done
case "$keyboard_layout" in jp|us|default) ;; *) echo 'Keyboard layout must be jp, us or default.' >&2; exit 1 ;; esac
if $system; then
    case "$prefix" in /*) ;; *) echo 'Prefix must be absolute.' >&2; exit 1 ;; esac
    target="$prefix/libexec/meltype-flash/ibus"
    components="$prefix/share/ibus/component"
    applications="$prefix/share/applications"
else
    target="$data/Meltype-flash/ibus"
    components="$data/ibus/component"
    applications="$data/applications"
fi
component="$components/meltype-flash.xml"
desktop="$applications/ibus-setup-meltype-flash.desktop"
fail() { echo "$*" >&2; exit 1; }
check_directory() {
    local path="$1"
    while [[ "$path" != / ]]; do
        [[ ! -L "$path" ]] || fail "Refusing symlink in installation path: $path"
        [[ ! -e "$path" || -d "$path" ]] || fail "Not a directory: $path"
        path="$(dirname "$path")"
    done
}
check_file() {
    check_directory "$(dirname "$1")"
    [[ ! -L "$1" && ( ! -e "$1" || -f "$1" ) ]] || fail "Not a regular installation file: $1"
}
check_directory "$target"
check_directory "$target/mozc"
check_directory "$components"
files=(meltype-ibus meltype-flash meltype-rs LICENSE THIRD-PARTY-NOTICES.md README.md MIGRATION.md install.sh icon.png .meltype-flash-ibus
       mozc/meltype_mozc_helper mozc/MOZC-LICENSE.txt mozc/MOZC-CREDITS.html)
for file in "${files[@]}"; do check_file "$target/$file"; done
check_file "$component"
check_file "$desktop"
if [[ -e "$desktop" ]] && ! grep -q '^X-Meltype-Flash-IBus=true$' "$desktop"; then
    fail 'Setup desktop file belongs to another application; keeping it.'
fi
if [[ -e "$component" ]] && ! grep -q '<name>org.freedesktop.IBus.MeltypeFlash</name>' "$component"; then
    fail 'Component file belongs to another application; keeping it.'
fi
if $uninstall; then
    if [[ ! -f "$target/.meltype-flash-ibus" ]]; then
        echo 'No owned Rust IBus installation found.' >&2
        exit 1
    fi
    if [[ -e "$component" ]] && ! grep -q '<name>org.freedesktop.IBus.MeltypeFlash</name>' "$component"; then
        echo 'Component file belongs to another application; keeping it.' >&2
        exit 1
    fi
    rm -f -- "$component" "$desktop"
    for file in meltype-ibus meltype-flash meltype-rs LICENSE THIRD-PARTY-NOTICES.md README.md MIGRATION.md install.sh icon.png .meltype-flash-ibus; do
        rm -f -- "$target/$file"
    done
    for file in meltype_mozc_helper MOZC-LICENSE.txt MOZC-CREDITS.html; do
        rm -f -- "$target/mozc/$file"
    done
    rmdir -- "$target/mozc" 2>/dev/null || true
    rmdir -- "$target" 2>/dev/null || true
    echo 'Removed the Rust IBus program and component. Settings and learning data remain.'
    exit 0
fi
for file in meltype-ibus meltype-flash meltype-rs icon.png; do
    [[ -f "$source_dir/$file" ]] || { echo "Missing $file" >&2; exit 1; }
done
mkdir -p "$target" "$components" "$applications"
stage="$(mktemp -d "$target/.meltype-flash-stage.XXXXXX")"
temporary=""
desktop_temporary=""
desktop_backup=""
desktop_changed=false
changed=()
complete=false
cleanup() {
    local status=$? file i restore_failed=false
    trap - EXIT
    if ! $complete; then
        for ((i=${#changed[@]}-1; i>=0; i--)); do
            file="${changed[i]}"
            if [[ -f "$stage/old/$file" ]]; then
                if ! mv -fT -- "$stage/old/$file" "$target/$file"; then
                    restore_failed=true
                    echo "Restore failed: $target/$file" >&2
                fi
            else
                rm -f -- "$target/$file"
            fi
        done
    fi
    if ! $complete && $desktop_changed; then
        if [[ -n "$desktop_backup" ]]; then
            if ! mv -fT -- "$desktop_backup" "$desktop"; then
                echo "Setup backup retained for recovery: $desktop_backup" >&2
                desktop_backup=""
                status=1
            fi
        else
            rm -f -- "$desktop"
        fi
    fi
    [[ -z "$desktop_temporary" ]] || rm -f -- "$desktop_temporary"
    [[ -z "$desktop_backup" ]] || rm -f -- "$desktop_backup"
    [[ -z "$temporary" ]] || rm -f -- "$temporary"
    for file in "${files[@]}"; do
        rm -f -- "$stage/new/$file"
        if ! $restore_failed; then rm -f -- "$stage/old/$file"; fi
    done
    if $restore_failed; then
        echo "Previous files retained for recovery: $stage/old" >&2
        status=1
    fi
    rmdir -- "$stage/new/mozc" "$stage/old/mozc" "$stage/new" "$stage/old" "$stage" 2>/dev/null || true
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -p "$stage/new/mozc" "$stage/old/mozc"
install_files=()
for file in "${files[@]}"; do
    if [[ "$file" == .meltype-flash-ibus ]]; then
        printf 'Meltype-FLASH IBus\n' > "$stage/new/$file"
    elif [[ -f "$source_dir/$file" ]]; then
        cp -- "$source_dir/$file" "$stage/new/$file"
    else
        continue
    fi
    if [[ -f "$target/$file" ]]; then cp -p -- "$target/$file" "$stage/old/$file"; fi
    install_files+=("$file")
done
chmod 755 "$stage/new/meltype-ibus" "$stage/new/meltype-flash" "$stage/new/meltype-rs"
if [[ -f "$stage/new/mozc/meltype_mozc_helper" ]]; then chmod 755 "$stage/new/mozc/meltype_mozc_helper"; fi
# Validate the new executable before replacing any old file. The descriptor must
# contain final paths even though validation runs from the staging directory.
temporary="$(mktemp "$components/.meltype-flash.XXXXXX")"
"$stage/new/meltype-ibus" --print-component --component-executable "$target/meltype-ibus" --keyboard-layout "$keyboard_layout" > "$temporary"
chmod 644 "$temporary"
grep -q '<component>' "$temporary" && grep -q '</component>' "$temporary" || fail 'Invalid IBus component output.'
desktop_temporary="$(mktemp "$applications/.meltype-setup.XXXXXX")"
"$stage/new/meltype-ibus" --print-setup-desktop --component-executable "$target/meltype-ibus" > "$desktop_temporary"
grep -q '^X-Meltype-Flash-IBus=true$' "$desktop_temporary" || fail 'Invalid setup desktop output.'
chmod 644 "$desktop_temporary"
if [[ -f "$desktop" ]]; then
    desktop_backup="$(mktemp "$applications/.meltype-setup-old.XXXXXX")"
    cp -p -- "$desktop" "$desktop_backup"
fi
# Recheck after staging and use rename rather than truncating running ELF files.
for file in "${install_files[@]}"; do check_file "$target/$file"; done
check_file "$component"
check_file "$desktop"
for file in "${install_files[@]}"; do
    mkdir -p "$(dirname "$target/$file")"
    mv -fT -- "$stage/new/$file" "$target/$file"
    changed+=("$file")
done
mv -fT -- "$desktop_temporary" "$desktop"
desktop_temporary=""
desktop_changed=true
mv -fT -- "$temporary" "$component"
temporary=""
complete=true
if $system; then
    echo 'Installed. Sign out/in, then select Meltype-FLASH in IBus input sources.'
    printf 'Remove with: sudo bash %q --system --prefix %q --uninstall\n' "$target/install.sh" "$prefix"
else
    echo 'User files installed. IBus does not discover user components by default.'
    echo 'Configure IBUS_COMPONENT_PATH to include the following folder and the system component folders before starting IBus:'
    echo "$components"
    printf 'Remove with: bash %q --uninstall\n' "$target/install.sh"
fi
