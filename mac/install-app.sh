#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 hrmcngs

# Prepare and verify the new bundle before stopping or replacing the current IME.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
install_step_failed() {
    local status="$1"
    shift
    if [[ "$policy" == homebrew ]]; then
        after_install_failed=1
    else
        echo "$* 入力メニューで ABC または標準の日本語入力を選び、上のエラーを確認してください。" >&2
        exit "$status"
    fi
}
require_install_step() {
    local message="$1"
    shift
    local status=0
    "$@" || status=$?
    if [[ $status -ne 0 ]]; then
        install_step_failed "$status" "$message"
    fi
}
validate_target() {
    [[ ! -L "$target_app" ]] || { echo "Installation target must not be a symbolic link: $target_app" >&2; exit 1; }
    [[ ! -e "$target_app" || -d "$target_app" ]] || { echo "Installation target must be a directory: $target_app" >&2; exit 1; }
}
remove_stage() {
    for staged_app in "$stage/Meltype.app" "$stage/failed.app"; do
        /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -u "$staged_app" 2>/dev/null || true
    done
    rm -rf "$stage"
}
cleanup_preparation() {
    local result=$?
    trap - EXIT
    if [[ -f "$stage/preserve" ]]; then
        echo "Installer helper diagnostics retained at $stage" >&2
    else
        remove_stage
    fi
    exit "$result"
}

if [[ "${1:-}" != --prepared ]]; then
    [[ $# -ge 2 && $# -le 4 ]] || { echo "Usage: install-app.sh source.app target.app [manual|homebrew] [receipt]" >&2; exit 1; }
    source_app="$1"
    target_app="$2"
    policy="${3:-manual}"
    receipt="${4:-}"
    [[ "$policy" == manual || "$policy" == homebrew ]] || { echo "Unknown installation policy: $policy" >&2; exit 1; }
    [[ -d "$source_app" ]] || { echo "Missing app: $source_app" >&2; exit 1; }
    validate_target
    [[ -f "$here/start-input-method.sh" ]] || { echo "Missing start-input-method.sh" >&2; exit 1; }
    mkdir -p "$(dirname "$target_app")"
    target_app="$(cd "$(dirname "$target_app")" && pwd -P)/$(basename "$target_app")"
    # A hidden sibling of Input Methods avoids discovery and stays on its volume.
    stage="$(mktemp -d "$(dirname "$(dirname "$target_app")")/.meltype-install.XXXXXX")"
    trap cleanup_preparation EXIT
    stage_device="$(stat -f %d "$stage")"
    target_device="$(stat -f %d "$(dirname "$target_app")")"
    [[ "$stage_device" == "$target_device" ]] || {
        echo "Staging and installation must be on the same volume." >&2
        exit 1
    }
    ditto "$source_app" "$stage/Meltype.app"
    codesign --verify --deep --strict "$stage/Meltype.app"
    # Read-only packaged resources must allow quarantine removal in this working copy.
    chmod -R u+w "$stage/Meltype.app"
    # Bootstrap with a system tool: an unverified helper cannot prepare itself.
    # Limit this to the signature-verified working copy and the one copied CLI.
    /usr/bin/xattr -drs com.apple.quarantine "$stage/Meltype.app"
    # The new helper remains available even when the old version lacks it.
    cp -X "$stage/Meltype.app/Contents/MacOS/MeltypeInstallSupport" "$stage/install-support"
    /usr/bin/xattr -drs com.apple.quarantine "$stage/install-support"
    if "$stage/install-support" --validate-bundle "$stage/Meltype.app"; then
        :
    else
        result=$?
        # Preserve signature and quarantine details for diagnosis if the helper cannot run or is killed.
        if [[ $result -eq 126 || $result -eq 137 ]]; then
            touch "$stage/preserve"
            {
                /usr/bin/xattr -l "$stage/install-support"
                /usr/bin/codesign -dv --verbose=3 "$stage/install-support"
            } > "$stage/helper-diagnostics.txt" 2>&1 || true
            echo "Installer helper was refused or killed before replacement (exit $result)." >&2
        fi
        exit "$result"
    fi
    # Serialize ZIP and source installs so replacement or rollback cannot overwrite another install's backup.
    lock="$(dirname "$target_app")/.$(basename "$target_app").install.lock"
    # The prepared child owns rollback and cleanup. Do not delete its files if
    # this outer process is interrupted while the locked child is still running.
    trap - EXIT
    result=0
    "$stage/install-support" --lock "$lock" /bin/bash "$here/install-app.sh" --prepared "$stage" "$target_app" "$policy" "$receipt" || result=$?
    if [[ -d "$stage" ]]; then
        if [[ -f "$stage/preserve" ]]; then
            echo "Installation recovery files retained at $stage" >&2
        else
            remove_stage
        fi
    fi
    exit "$result"
fi

[[ $# -eq 5 ]] || { echo "Invalid prepared installation arguments" >&2; exit 1; }
stage="$2"
target_app="$3"
policy="$4"
receipt="$5"
support_tool="$stage/install-support"
state_file="$stage/input-state.json"
phase=prepared
had_previous=0
after_install_failed=0
cleanup() {
    local result=$?
    trap - EXIT
    if [[ $result -ne 0 && $phase == swapped ]]; then
        # macOS may have started the replacement before validation failed.
        if ! "$support_tool" --stop "$target_app" "$stage/Meltype.app" -; then
            touch "$stage/preserve"
            echo "Cannot stop the failed input server; previous app preserved at $stage/Meltype.app" >&2
            exit "$result"
        fi
        if ! "$support_tool" "$stage/Meltype.app/Contents" "$target_app/Contents"; then
            touch "$stage/preserve"
            echo "Restore the previous app from $stage/Meltype.app; failed installation remains at $target_app" >&2
            exit "$result"
        fi
    elif [[ $result -ne 0 && $phase == initial && -e "$target_app" ]]; then
        # Registration may already have launched the new server, even on first install.
        if ! "$support_tool" --stop "$target_app" "$target_app" -; then
            echo "Cannot stop the failed input server; recovery files retained at $stage" >&2
            exit "$result"
        fi
        if ! mv "$target_app" "$stage/failed.app"; then
            touch "$stage/preserve"
            echo "Partial installation preserved at $target_app; recovery files at $stage" >&2
            exit "$result"
        fi
    fi
    if [[ $result -ne 0 && $had_previous -eq 1 && -f "$state_file" ]]; then
        if ! "$support_tool" --restore "$target_app" "$state_file"; then
            echo "The previous app is restored, but its input session could not resume: $target_app" >&2
        fi
    fi
    remove_stage
    exit "$result"
}
trap cleanup EXIT
# Retain recovery files if this process dies before cleanup completes.
# Only this child removes them after successful completion or rollback.
touch "$stage/preserve"
# Recheck under the lock: another installation may have completed after install.sh checked.
validate_target
if [[ -e "$target_app" ]]; then
    if [[ "$policy" == homebrew ]]; then
        echo "手動で入れた Meltype があります: $target_app" >&2
        echo "Homebrew で入れるときは、先にこれを消してから、もう一度実行してください。" >&2
        exit 1
    fi
    had_previous=1
    "$support_tool" --validate-bundle "$target_app"
fi
stop_args=(--stop "$target_app" "$stage/Meltype.app" "$state_file")
manual_job=io.github.yksr-melt.Meltype.manual
if launchctl list "$manual_job" >/dev/null 2>&1; then
    stop_args+=("$manual_job")
fi
"$support_tool" "${stop_args[@]}"
if [[ $had_previous -eq 1 ]]; then
    root_attributes="$(/usr/bin/xattr "$target_app")"
    if /usr/bin/grep -qx com.apple.quarantine <<< "$root_attributes"; then
        /usr/bin/xattr -ds com.apple.quarantine "$target_app"
    fi
fi
if [[ $had_previous -eq 1 ]]; then
    # Keep the outer app identity used by existing Input Method Kit clients.
    "$support_tool" "$stage/Meltype.app/Contents" "$target_app/Contents"
    phase=swapped
else
    phase=initial
    mv "$stage/Meltype.app" "$target_app"
fi
codesign --verify --deep --strict "$target_app"
if [[ "$policy" == homebrew ]]; then
    echo "インストールしました: $target_app"
fi
# Keep the old Contents and the lock until registration and startup both succeed,
# including updates that began with Meltype stopped and another input source selected.
# Restarting the input menu during an update can detach the frontmost client's IME session.
app_registration=(--register-input-source)
helper_registration=("$target_app")
if [[ $had_previous -eq 1 ]]; then
    app_registration+=(--keep-input-menu)
    helper_registration+=(--keep-input-menu)
fi
require_install_step "入力ソースの登録に失敗しました。" "$target_app/Contents/MacOS/Meltype" "${app_registration[@]}"
registration_status=0
"$target_app/Contents/MacOS/MeltypeRegisterInputSource" "${helper_registration[@]}" || registration_status=$?
# Exit 2 means registration succeeded but macOS has not enabled the source yet.
# Keep this distinct from a registration failure so manual activation remains possible.
case "$registration_status" in
    0|2) ;;
    *) install_step_failed "$registration_status" "入力ソースの登録に失敗しました。" ;;
esac
require_install_step "アプリの登録に失敗しました。" /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$target_app"
require_install_step "Meltype の起動に失敗しました。" bash "$here/start-input-method.sh" "$target_app" "$support_tool"
restore_status=0
if [[ $after_install_failed -eq 0 ]]; then
    "$support_tool" --restore "$target_app" "$state_file" || restore_status=$?
fi
if [[ $restore_status -eq 2 ]]; then
    echo "Meltype は更新・起動しましたが、入力ソースを戻せませんでした。入力メニューから選び直してください。" >&2
elif [[ $restore_status -ne 0 ]]; then
    install_step_failed "$restore_status" "入力状態の復元に失敗しました。"
fi
if [[ $registration_status -eq 2 && $after_install_failed -eq 0 ]]; then
    echo "Meltype は配置・起動しましたが、入力ソースの有効化は保留中です。アプリはそのまま残します。システム設定 → キーボード → テキスト入力「編集…」→「+」→ 日本語 → Meltype から追加してください。表示されなければログアウトしてログインし直してください。" >&2
fi
if [[ "$policy" == homebrew ]]; then
    if [[ $after_install_failed -eq 1 ]]; then
        echo "Meltype を入れましたが、登録か起動が終わりませんでした。ログアウトしてログインし直してから、入力メニューで Meltype を選んでください。" >&2
    else
        echo "Meltype を起動しました。入力メニューから Meltype を選んでください。"
    fi
elif [[ -n "$receipt" ]]; then
    if [[ $had_previous -eq 1 ]]; then echo update > "$receipt"
    else echo initial > "$receipt"; fi
fi
