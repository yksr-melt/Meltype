# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# -FromSettings: Windows の「設定 → アプリ」から実行したとき (画面が無いので、結果をメッセージボックスで知らせる)
param([switch]$FromSettings)

$ErrorActionPreference = 'Stop'

# Meltype のアンインストール。設定と学習データ (%LOCALAPPDATA%\Meltype) も消す。
# zip の Uninstall.cmd からも、インストール先にコピーしたもの (設定 → アプリ から) からも動く。

function Say([string]$message) {
    if ($FromSettings) { (New-Object -ComObject WScript.Shell).Popup($message, 0, 'Meltype', 0x40) | Out-Null }
    else { Write-Host $message }
}

# インストール先 (このスクリプトがある場所かもしれない) を消せるように、別の場所に移っておく。
Set-Location -LiteralPath $env:TEMP

# 動いている Meltype を止める。管理者として動いている Meltype は Stop-Process では止められないので、
# まず Meltype.exe --exit で終了の合図を送る (新しい版の Meltype なら、権限に関係なく終了する)。
function Stop-Meltype {
    $installed = Join-Path $env:LOCALAPPDATA 'Programs\Meltype\Meltype.exe'
    if ((Get-Process Meltype -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $installed)) {
        Start-Process -FilePath $installed -ArgumentList '--exit' -Wait -ErrorAction SilentlyContinue
        for ($i = 0; $i -lt 30 -and (Get-Process Meltype -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 100 }
    }
    foreach ($process in Get-Process Meltype, meltype_mozc_helper, AutoIME -ErrorAction SilentlyContinue) {
        try {
            $process | Stop-Process -Force -ErrorAction Stop
        }
        catch {
            Say "Meltype を終了できませんでした (管理者として動いているのかもしれません)。`nタスクトレイの Meltype のアイコンを右クリックして「終了」を選んでから、もう一度アンインストールしてください。"
            exit 1
        }
    }
    Start-Sleep -Milliseconds 300
}

# 旧名 (AutoIME) のときのものも一緒に消す。
Stop-Meltype

# Meltype IME の登録を外す (インストール先を消す前に。管理者権限の確認が出る)
$imeScript = Join-Path $env:LOCALAPPDATA 'Programs\Meltype\meltype-ime.ps1'
if (-not (Test-Path -LiteralPath $imeScript)) { $imeScript = Join-Path $PSScriptRoot 'app\meltype-ime.ps1' }
if (Test-Path -LiteralPath $imeScript) {
    . $imeScript
    if (-not (Uninstall-MeltypeIme)) { Say "Meltype IME の登録を外せませんでした。管理者として、次を実行してください:`nregsvr32 /u `"$MeltypeProgramFiles\Meltype\tip\x64\MeltypeTip.dll`"`n%SystemRoot%\SysWOW64\regsvr32 /u `"$MeltypeProgramFiles\Meltype\tip\x86\MeltypeTip.dll`"" }
}

# サインインしたらすぐ起動するタスク (Meltype が作る: #283)
& cmd.exe /c 'schtasks.exe /Delete /TN Meltype /F >nul 2>&1'

foreach ($name in 'Meltype.lnk', 'AutoIME.lnk') {
    $shortcut = Join-Path ([Environment]::GetFolderPath('Startup')) $name
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Meltype.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }

$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype'
if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force }

foreach ($folder in 'Programs\Meltype', 'Meltype', 'Programs\AutoIME', 'AutoIME' | ForEach-Object { Join-Path $env:LOCALAPPDATA $_ }) {
    if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force }
}
Say 'Meltype をアンインストールしました (設定と学習データも削除しました)。'
