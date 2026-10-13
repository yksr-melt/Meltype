# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# -NoIme: Meltype IME を入れない (変換ボックスで入力する方式だけ。管理者権限が要らない)
param([switch]$NoIme)

$ErrorActionPreference = 'Stop'

# Meltype はタスクトレイに常駐するプロセス (Meltype.exe) と、Windows の IME として入力欄に直接入力する
# Meltype IME (native\tip の TSF の DLL。入力の本体は Meltype.exe に問い合わせる) でできている。
$project = Join-Path $PSScriptRoot 'src\Meltype\Meltype.csproj'
$output = Join-Path $PSScriptRoot 'app-build'

# 旧名 (AutoIME) のときのものがあれば片付ける (設定と学習データは Meltype の初回起動時に引き継ぐ)。
Get-Process AutoIME -ErrorAction SilentlyContinue | Stop-Process -Force
$oldShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.lnk'
if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
# 強制終了の前に終了の合図を送り、まだ書いていない学習データを書かせる
$running = Join-Path $PSScriptRoot 'app-build\Meltype.exe'
if (Test-Path -LiteralPath $running) { Start-Process -FilePath $running -ArgumentList '--exit' -Wait -ErrorAction SilentlyContinue }
Get-Process Meltype, meltype_mozc_helper -ErrorAction SilentlyContinue | Stop-Process -Force

dotnet build $project -c Release -o $output
if ($LASTEXITCODE -ne 0) { throw "Meltype のビルドに失敗しました (exit code $LASTEXITCODE)。" }

# Mozc の変換ヘルパー (native\mozc\Build-MozcHelper.ps1 で作ったもの) があれば一緒に置く。
$mozcBin = Join-Path $PSScriptRoot 'native\mozc\bin'
if (Test-Path -LiteralPath (Join-Path $mozcBin 'meltype_mozc_helper.exe')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $output 'mozc') | Out-Null
    Copy-Item -Path (Join-Path $mozcBin '*') -Destination (Join-Path $output 'mozc') -Force
}

$exe = Join-Path $output 'Meltype.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "Meltype.exe が作成されませんでした: $exe" }

if (-not $NoIme) {
    try {
        & (Join-Path $PSScriptRoot 'native\tip\build.ps1') -Configuration Release
        $tip = Join-Path $output 'tip'
        foreach ($arch in 'x64', 'x86') {
            New-Item -ItemType Directory -Force -Path (Join-Path $tip $arch) | Out-Null
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot "native\tip\bin\$arch\MeltypeTip.dll"), (Join-Path $PSScriptRoot "native\tip\bin\$arch\MeltypeTip.dll.sha256") -Destination (Join-Path $tip $arch) -Force
        }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'native\tip\Register-Tip.ps1') -Destination $tip -Force
        . (Join-Path $PSScriptRoot 'packaging\meltype-ime.ps1')
        # 自分で実行したときなので、前に登録を断っていても、もう一度聞く
        Install-MeltypeIme -Source $tip -Ask | Out-Null
    }
    catch {
        # C++ のビルドツールが無いなど。Meltype 本体 (変換ボックスで入力する方式) は入れる
        Write-Warning "Meltype IME を入れられませんでした (変換ボックスで入力する方式で使えます): $($_.Exception.Message)"
    }
}

# 自動起動と、スタートメニュー・Windows 検索からの起動用 (現在のユーザー)
$shell = New-Object -ComObject WScript.Shell
foreach ($folderName in 'Startup', 'Programs') {
    $folder = [Environment]::GetFolderPath($folderName)
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Meltype.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $output
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'Meltype: 入力開始時に日本語入力を自動判定する'
    $shortcut.Save()
}

Start-Process -FilePath $exe -WorkingDirectory $output
Write-Host 'Meltype をインストールして起動しました。タスクトレイのアイコンから設定・ログ・一時停止 (Ctrl+半角/全角) ができます。'
