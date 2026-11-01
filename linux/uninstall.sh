#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Meltype (Linux 版テスト版) を外す。設定と学習データ (~/.local/share/Meltype) も消すなら --data を付ける:
#   bash uninstall.sh [--data]
set -euo pipefail
echo "Meltype を外します (管理者のパスワードを聞かれます)"
sudo rm -f /usr/share/ibus/component/meltype.xml
# fcitx5 のアドオン
sudo rm -f /usr/share/fcitx5/addon/meltype.conf /usr/share/fcitx5/inputmethod/meltype.conf
{ find /usr/lib /usr/lib64 -name meltype.so -path '*fcitx5*' 2> /dev/null || true; } | xargs -r sudo rm -f
# 動いている fcitx5 は Meltype のアドオンを読み込んだままなので、起動し直して外す。
# KDE Plasma の Wayland では KWin が fcitx5 を起動していて、fcitx5 は自分を起動し直せない
# (自分のユーザーの fcitx5 だけを見る。ほかのユーザーの PID だと kill -0 が権限なしで失敗し、
# 起動し直せていないのに成功と判定してしまう)
before="$(pgrep -u "$(id -u)" -x fcitx5 2> /dev/null | head -n 1)" || true
if [[ -n "$before" ]]; then
    (fcitx5 -r -d > /dev/null 2>&1 &)
    # 前の fcitx5 が終わるのを待つ。終わらなければ起動し直せていない
    # 待ち方 (0.2 秒 × 25 回) は install.sh の fcitx5_restart の同じ待ちと対になっている。片方だけ変えない。
    for _ in {1..25}; do
        sleep 0.2
        if ! kill -0 "$before" 2> /dev/null; then break; fi
    done
    if kill -0 "$before" 2> /dev/null; then
        echo "※ fcitx5 を起動し直すまで Meltype が残ります。"
        echo "   KDE Plasma (Wayland) では、いったんログアウトしてログインし直してください。"
        echo "   それ以外の環境では、端末で次を実行して fcitx5 を起動し直してください。"
        echo "     fcitx5-remote -e; fcitx5 -d"
    fi
fi
sudo rm -rf /opt/meltype
pkill -f ibus-engine-meltype 2> /dev/null || true
ibus write-cache 2> /dev/null || true
ibus restart 2> /dev/null || true
if [[ "${1:-}" == "--data" ]]; then
    rm -rf "${XDG_DATA_HOME:-$HOME/.local/share}/Meltype"
    echo "設定と学習データも消しました。"
fi
echo "外しました。入力ソースに Meltype が残っていたら、設定 → キーボード → 入力ソース から外してください。"
