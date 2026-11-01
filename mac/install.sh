#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# テスト版の Meltype.app を ~/Library/Input Methods に入れる。zip を展開したフォルダーで実行する:
#   bash install.sh
set -euo pipefail
cd "$(dirname "$0")"

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

TARGET="$HOME/Library/Input Methods"
bash ./install-app.sh Meltype.app "$TARGET/Meltype.app"
# インターネットから取ってきた印 (隔離属性) を外す。署名が自分用なので、外さないと macOS が起動させない。
xattr -dr com.apple.quarantine "$TARGET/Meltype.app" 2>/dev/null || true

echo "インストールしました: $TARGET/Meltype.app"
enable_input_source
register_input_source
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$TARGET/Meltype.app"
bash ./start-input-method.sh "$TARGET/Meltype.app"
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
