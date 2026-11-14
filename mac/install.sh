#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# テスト版の Meltype.app を ~/Library/Input Methods に入れる。zip を展開したフォルダーで実行する:
#   bash install.sh
# Homebrew の cask からは --homebrew を付けて動かす。付けたときは:
#   - 最後に入力ソースを Meltype に切り替えない (brew install の途中で入力ソースを変えない)
#   - 初回に手動版があれば止める。既存 Cask の更新では、旧版は先に削除される。
#   - 登録・起動の失敗は、アプリを残して警告付きで正常終了する
#     brew upgrade が先に削除した旧版の復元は行わない。
set -euo pipefail
cd "$(dirname "$0")"

from_homebrew=0
while [[ $# -gt 0 ]]; do
    case "$1" in
        --homebrew) from_homebrew=1; shift ;;
        *) echo "知らない引数です: $1" >&2; exit 1 ;;
    esac
done

if [[ ! -d Meltype.app ]]; then
    echo "Meltype.app が見つかりません。zip を展開したフォルダーで実行してください。" >&2
    exit 1
fi
for helper in install-app.sh start-input-method.sh select-input-source.swift; do
    [[ -f "$helper" ]] || { echo "必要なファイルがありません: ${helper}。zip 全体を展開してください。" >&2; exit 1; }
done

TARGET="$HOME/Library/Input Methods"
# 既存 Cask の契約を維持し、手動版があれば上書きしない。
if [[ $from_homebrew -eq 1 && -e "$TARGET/Meltype.app" ]]; then
    echo "手動で入れた Meltype があります: $TARGET/Meltype.app" >&2
    echo "Homebrew で入れるときは、先にこれを消してから、もう一度実行してください。" >&2
    exit 1
fi
if [[ $from_homebrew -eq 1 ]]; then
    exec bash ./install-app.sh Meltype.app "$TARGET/Meltype.app" homebrew
fi
receipt="$(mktemp "${TMPDIR:-/tmp}/meltype-install-result.XXXXXX")"
trap 'rm -f "$receipt"' EXIT
bash ./install-app.sh Meltype.app "$TARGET/Meltype.app" manual "$receipt"
IFS= read -r installation_phase < "$receipt"
case "$installation_phase" in
    initial) updating=0 ;;
    update) updating=1 ;;
    *) echo "インストール結果を確認できませんでした。" >&2; exit 1 ;;
esac
echo "インストールしました: $TARGET/Meltype.app"
if [[ $updating -eq 1 ]]; then
    echo "Meltype を更新しました。"
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
