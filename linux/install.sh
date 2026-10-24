#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype (Linux 版テスト版) を入れる。zip を展開したフォルダーで実行する:
#   bash install.sh
# プログラムは /opt/meltype に、IBus のコンポーネントは /usr/share/ibus/component に入れる (管理者のパスワードを聞かれる)。
# fcitx5 を使っていれば、fcitx5 のアドオンも入れる (IBus が無ければ IBus の部品は入れない)。fcitx5 だけに入れるなら --fcitx5 を付ける。
set -euo pipefail
cd "$(dirname "$0")"
target=/opt/meltype

if [[ ! -f libMeltypeNative.so || ! -f ibus-engine-meltype ]]; then
    echo "libMeltypeNative.so が見つかりません。zip を展開したフォルダーで実行してください。" >&2
    exit 1
fi
if [[ "${1:-}" == "--fcitx5" && ! -f fcitx5/meltype.so ]]; then
    echo "fcitx5 のアドオン (fcitx5/meltype.so) が見つかりません。fcitx5 の開発用のパッケージ (fcitx5-modules-dev) と cmake を入れてビルドし直すか、--fcitx5 を付けずに実行してください。" >&2
    exit 1
fi

# fcitx5 を使っているか (fcitx5 が入っていて、zip に fcitx5 のアドオンがある)。IBus が無く fcitx5 だけなら、IBus は入れない
use_fcitx5=false
use_ibus=true
fcitx5_needs_restart=false
if [[ -f fcitx5/meltype.so ]] && { command -v fcitx5 > /dev/null || [[ "${1:-}" == "--fcitx5" ]]; }; then
    use_fcitx5=true
    if [[ "${1:-}" == "--fcitx5" ]] || ! command -v ibus > /dev/null; then use_ibus=false; fi
fi

# IBus と、Python から IBus を使う部品 (Ubuntu の標準の画面なら、ふつうは入っている)
if $use_ibus; then
    missing=()
    command -v ibus > /dev/null || missing+=(ibus)
    python3 -c 'import gi; gi.require_version("IBus", "1.0"); from gi.repository import IBus' 2> /dev/null || missing+=(python3-gi gir1.2-ibus-1.0)
    if [[ ${#missing[@]} -gt 0 ]]; then
        echo "必要な部品を入れます: ${missing[*]}"
        sudo apt-get install -y "${missing[@]}"
    fi
fi

echo "$target に入れます (管理者のパスワードを聞かれます)"
sudo rm -rf "$target"
sudo mkdir -p "$target"
sudo cp -R libMeltypeNative.so ibus-engine-meltype icon.png mozc LICENSE THIRD-PARTY-NOTICES.md "$target/"
sudo chmod 755 "$target/ibus-engine-meltype" "$target/mozc/meltype_mozc_helper"
if $use_ibus; then
    sed "s|@DIR@|$target|g" meltype.xml | sudo tee /usr/share/ibus/component/meltype.xml > /dev/null
    # IBus に読み込み直させる (動いている Meltype も止まり、次に使うときに新しいものが起動する)
    ibus write-cache 2> /dev/null || true
    ibus restart 2> /dev/null || true
fi

# ---- 動いている fcitx5 に読み直させる ----
# 新しいアドオンと入力メソッドは、動いている fcitx5 に DBus の Refresh で読ませられる (止めなくてよい)。
# ただし、すでに Meltype のアドオンを読み込んでいる fcitx5 は、その共有ライブラリを読み直せない。
# 新しい版に切り替えるには起動し直す必要があるが、KDE Plasma の Wayland では KWin が fcitx5 を
# 起動していて、fcitx5 は自分を起動し直せない (fcitx5 -r が効かない)。そのときはログインし直してもらう。

# 走っている fcitx5 に Meltype の入力メソッドが見えているか
fcitx5_has_meltype() {
    [[ "$(fcitx5-remote -m meltype 2> /dev/null)" == "meltype" ]]
}

# 動いている fcitx5 に、新しいアドオンと入力メソッドを読ませる (DBus の Refresh)
fcitx5_refresh() {
    if command -v busctl > /dev/null 2>&1; then
        busctl --user call org.fcitx.Fcitx5 /controller org.fcitx.Fcitx.Controller1 Refresh > /dev/null 2>&1 && return 0
    fi
    if command -v gdbus > /dev/null 2>&1; then
        gdbus call --session --dest org.fcitx.Fcitx5 --object-path /controller \
            --method org.fcitx.Fcitx.Controller1.Refresh > /dev/null 2>&1 && return 0
    fi
    if command -v dbus-send > /dev/null 2>&1; then
        dbus-send --session --print-reply --dest=org.fcitx.Fcitx5 /controller \
            org.fcitx.Fcitx.Controller1.Refresh > /dev/null 2>&1 && return 0
    fi
    return 1
}

# 動いている fcitx5 の PID (自分のユーザーのものだけ)。動いていなければ空。
# pgrep -x fcitx5 だけだと、複数人でログインしている PC ではほかのユーザーの fcitx5 を拾う。
# その PID には kill -0 が権限なしで失敗するので、起動し直せていないのに成功と判定してしまう。
fcitx5_pid() {
    pgrep -u "$(id -u)" -x fcitx5 2> /dev/null | head -n 1 || true
}

# fcitx5 を起動し直す。起動し直せたら 0、起動し直せない (KDE Plasma の Wayland など) なら 1
fcitx5_restart() {
    local before
    before="$(fcitx5_pid)"
    [[ -n "$before" ]] || return 0
    (fcitx5 -r -d > /dev/null 2>&1 &)
    # 前の fcitx5 が終わるのを待つ。終わらなければ起動し直せていない
    # (起動しかけてすぐ終わる fcitx5 を、起動し直せたと数えないようにする)
    # 待ち方 (0.2 秒 × 25 回) は uninstall.sh の同じ待ちと対になっている。片方だけ変えない。
    for _ in {1..25}; do
        sleep 0.2
        if ! kill -0 "$before" 2> /dev/null; then break; fi
    done
    if kill -0 "$before" 2> /dev/null; then return 1; fi
    # 新しい fcitx5 が Meltype を読むのを待つ
    for _ in {1..25}; do
        if fcitx5_has_meltype; then return 0; fi
        sleep 0.2
    done
    return 1
}

# 走っている fcitx5 に、新しい Meltype を読ませる。読ませられたら 0、新しいものが使われていなければ 1
fcitx5_reload() {
    local before
    before="$(fcitx5_pid)"
    [[ -n "$before" ]] || return 0    # 動いていなければ、次に起動したときに読まれる
    if fcitx5_has_meltype; then
        # 前から Meltype を読み込んでいる。共有ライブラリは読み直せないので、Refresh しても
        # 古いアドオンのまま。起動し直して、新しいものに切り替える (Refresh は呼ばない)
        fcitx5_restart || return 1
        return 0
    fi
    # 新しいアドオンと入力メソッドを読ませる (fcitx5 を止めずに読める)
    if fcitx5_refresh; then
        for _ in {1..25}; do
            if fcitx5_has_meltype; then return 0; fi
            sleep 0.2
        done
    fi
    # Refresh では読めなかった。起動し直して読ませる
    fcitx5_restart || return 1
    return 0
}

if $use_fcitx5; then
    # fcitx5 のアドオンの置き場所 (fcitx5 の部品 libclassicui.so と同じフォルダー)
    classicui=$({ find /usr/lib /usr/lib64 -name libclassicui.so -path '*fcitx5*' 2> /dev/null || true; } | head -n 1)
    if [[ -z "$classicui" ]]; then
        echo "fcitx5 のアドオンの置き場所が見つかりません。fcitx5 には入れません。" >&2
        use_fcitx5=false
    else
        sudo cp fcitx5/meltype.so "$(dirname "$classicui")/meltype.so"
        sudo mkdir -p /usr/share/fcitx5/addon /usr/share/fcitx5/inputmethod
        sudo cp fcitx5/meltype-addon.conf /usr/share/fcitx5/addon/meltype.conf
        sed "s|@DIR@|$target|g" fcitx5/meltype.conf | sudo tee /usr/share/fcitx5/inputmethod/meltype.conf > /dev/null
        # 動いている fcitx5 に読み直させる。読ませられなかったときは、あとで知らせる
        # (動いていなければ、fcitx5_reload は何もせずに 0 を返す)
        fcitx5_reload || fcitx5_needs_restart=true
    fi
fi

echo
echo "インストールしました。"
if $use_ibus; then
    echo "IBus で使うときは、初めてのときは:"
    echo "  1. いったんログアウトしてログインし直す"
    echo "  2. 設定 → キーボード → 入力ソース →「+ 入力ソースを追加」→ 日本語 → Meltype を追加"
    echo "  3. 画面右上の入力ソースのメニュー (または Super + Space) で Meltype を選ぶ"
fi
if $use_fcitx5; then
    echo "fcitx5 で使うときは:"
    if $fcitx5_needs_restart; then
        echo "  ※ 動いている fcitx5 には、新しい Meltype を読み込ませられませんでした。"
        echo "     KDE Plasma (Wayland) では KWin が fcitx5 を起動しているため、"
        echo "     いったんログアウトしてログインし直してください。"
        echo "     それ以外の環境では、端末で次を実行して fcitx5 を起動し直してください。"
        echo "       fcitx5-remote -e; fcitx5 -d"
    fi
    echo "  1. fcitx5 の設定 (fcitx5-configtool) →「入力メソッド」で Meltype を追加する (検索欄に Meltype と入れる)"
    echo "  2. Ctrl + Space などで Meltype に切り替える"
fi
