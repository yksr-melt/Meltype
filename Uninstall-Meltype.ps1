# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

param(
    # 設定と学習データ (%LOCALAPPDATA%\Meltype) も削除する
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'

foreach ($folderName in 'Startup', 'Programs') {
    $shortcut = Join-Path ([Environment]::GetFolderPath($folderName)) 'Meltype.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
}
# サインインしたらすぐ起動するタスク (Meltype が作る: #283)
& cmd.exe /c 'schtasks.exe /Delete /TN Meltype /F >nul 2>&1'
Get-Process Meltype, meltype_mozc_helper -ErrorAction SilentlyContinue | Stop-Process -Force
. (Join-Path $PSScriptRoot 'packaging\meltype-ime.ps1')
if (-not (Uninstall-MeltypeIme)) {
    Write-Warning 'Meltype IME の登録を外せませんでした (管理者権限の確認で「いいえ」を選んだときなど)。もう一度このスクリプトを実行してください。続けて外せないときは、管理者の PowerShell で native\tip\Register-Tip.ps1 -Unregister を実行してください。'
}

if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'Meltype'
    if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
    Write-Host 'Meltype を停止し、自動起動・スタートメニューのショートカットと設定・学習データを削除しました。'
} else {
    Write-Host 'Meltype を停止し、自動起動・スタートメニューのショートカットを削除しました。設定と学習データは %LOCALAPPDATA%\Meltype に残っています (-RemoveData で削除)。'
}
