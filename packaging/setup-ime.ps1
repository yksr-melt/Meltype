# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 MuNeNICK
#
# Meltype IME を登録する / 登録を外す。元のユーザーの権限で実行する (PC 全体への登録だけ meltype-ime.ps1 が昇格する)。
#   setup-ime.ps1 -Source <tip フォルダー> [-Explain]   setup.exe (PrepareToInstall)・Meltype.exe (トレイの「Meltype IME」)
#   setup-ime.ps1 -Uninstall [-Explain]                アンインストーラー (インストール先を消す前)
# -Explain: UAC の前に、Meltype からの確認だと分かる説明を出す (ウィザードを出さない自動更新・winget で、説明なしに UAC が出ないように: #970)。
# 登録できなければ終了コード 1。setup.exe は、このユーザーが使っている Meltype IME を更新できなければ本体も入れ替えない。

param([string]$Source, [switch]$Explain, [switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'Meltype-setup-ime.log'

try {
    & {
        . (Join-Path $PSScriptRoot 'meltype-ime.ps1')
        $ok = if ($Uninstall) { Uninstall-MeltypeIme -Explain:$Explain } else { Install-MeltypeIme -Source $Source -Explain:$Explain }
        if (-not $ok) { exit 1 }
    } *> $log
}
catch {
    Add-Content -LiteralPath $log -Value "失敗: $($_.Exception.Message)" -Encoding UTF8
    exit 1
}
