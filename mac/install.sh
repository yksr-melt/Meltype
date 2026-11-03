#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# テスト版の Meltype.app を ~/Library/Input Methods に入れる。zip を展開したフォルダーで実行する:
#   bash install.sh
# Homebrew の cask からは --homebrew を付けて動かす。付けたときは:
#   - 最後に入力ソースを Meltype に切り替えない (brew install の途中で入力ソースを変えない)
#   - 手動で入れた Meltype.app があれば、何も置かずに止める (Homebrew で入れると brew uninstall で消えるため)
#   - 配置したあとの登録・起動に失敗しても、警告して続ける。Homebrew は失敗した installer script が
#     置いたものを戻さないので、ここで止めると Homebrew の管理外の Meltype.app が残る
set -euo pipefail
cd "$(dirname "$0")"

from_homebrew=0
for arg in "$@"; do
    case "$arg" in
        --homebrew) from_homebrew=1 ;;
        *) echo "知らない引数です: $arg" >&2; exit 1 ;;
    esac
done

if [[ ! -d Meltype.app ]]; then
    echo "Meltype.app が見つかりません。zip を展開したフォルダーで実行してください。" >&2
    exit 1
fi
for helper in install-app.sh start-input-method.sh select-input-source.swift; do
    [[ -f "$helper" ]] || { echo "必要なファイルがありません: ${helper}。zip 全体を展開してください。" >&2; exit 1; }
done

# 入力ソースの「+」の一覧に Meltype が出ない Mac がある (macOS 26、#21)。
# アプリ内の登録処理で、有効な入力ソースを追加し、Meltype の重複だけを整理する。
enable_input_source() {
    "$TARGET/Meltype.app/Contents/MacOS/Meltype" --register-input-source || {
        echo "入力ソースに Meltype を追加できませんでした。ログアウトしてログインし直すと直ります。" >&2
        return 0
    }
    echo "Meltype の入力ソースの登録処理を実行しました"
}

# 入力ソースの登録をやり直す (#134)。Meltype.app を入れ替えるとき、バンドルが無い一瞬に入力ソースの
# 走査が走ると、macOS は登録を消す。消えたあとは、バンドルを戻して走査し直しても戻らないことがあり、
# これまではログアウトするしかなかった。TISRegisterInputSource なら実行中でも戻せる。
register_input_source() {
    local tool="$TARGET/Meltype.app/Contents/MacOS/MeltypeRegisterInputSource"
    # この道具が入っていない Meltype.app (これまでの版) のときは何もしない。
    [[ -x "$tool" ]] || return 0
    "$tool" "$TARGET/Meltype.app" ||
        echo "入力ソースに登録できませんでした。ログアウトしてログインし直すと直ります。" >&2
}

# 配置したあとの手順を動かす。失敗したら、--homebrew のときは印を付けて続け、そうでなければ止める。
after_install_failed=0
after_install() {
    local status=0
    "$@" || status=$?
    [[ $status -ne 0 ]] || return 0
    [[ $from_homebrew -eq 1 ]] || exit "$status"
    after_install_failed=1
}

TARGET="$HOME/Library/Input Methods"
# brew upgrade・reinstall では、先に cask の uninstall が古い Meltype.app を消している。
# ここにあるのは手動で入れたもので、上書きすると brew uninstall で消えてしまう (前の cask の input_method と同じく止める)。
if [[ $from_homebrew -eq 1 && -e "$TARGET/Meltype.app" ]]; then
    echo "手動で入れた Meltype があります: $TARGET/Meltype.app" >&2
    echo "Homebrew で入れるときは、先にこれを消してから、もう一度実行してください。" >&2
    exit 1
fi
bash ./install-app.sh Meltype.app "$TARGET/Meltype.app"
# インターネットから取ってきた印 (隔離属性) を外す。署名が自分用なので、外さないと macOS が起動させない。
xattr -dr com.apple.quarantine "$TARGET/Meltype.app" 2>/dev/null || true

echo "インストールしました: $TARGET/Meltype.app"
enable_input_source
register_input_source
after_install /System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$TARGET/Meltype.app"
after_install bash ./start-input-method.sh "$TARGET/Meltype.app"
if [[ $from_homebrew -eq 1 ]]; then
    if [[ $after_install_failed -eq 1 ]]; then
        echo "Meltype を入れましたが、登録か起動が終わりませんでした。ログアウトしてログインし直してから、入力メニューで Meltype を選んでください。" >&2
    else
        echo "Meltype を起動しました。入力メニューから Meltype を選んでください。"
    fi
    exit 0
fi
# swift はコマンドライン・デベロッパ・ツールが無い Mac では使えない。選べなくてもインストールは終わっている。
# macOS 27 では、入力ソースの有効化がアプリから通らず、ここで選べないことがある。その場合も、
# システム設定の入力ソースに追加すれば、ログアウトしなくても入力メニューから選べるようになる。
if swift ./select-input-source.swift; then
    echo
    echo "Meltype を起動し、入力ソースとして選択しました。"
else
    echo
    echo "Meltype を起動しました。入力メニューに Meltype が無いときは、"
    echo "システム設定 → キーボード →（テキスト入力の）入力ソース →「編集…」→「+」→ 日本語 → Meltype"
    echo "から追加してください。それでも出てこなければ、ログアウトしてログインし直してください。"
fi
