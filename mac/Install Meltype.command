#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# ダブルクリックで Meltype をインストールする (中身は install.sh)。
# 「開発元が未確認のため開けません」と出たら、右クリック (control + クリック) →「開く」→「開く」。
cd "$(dirname "$0")"
bash ./install.sh
echo
read -r -p "Enter キーを押すと閉じます" _
