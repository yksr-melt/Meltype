# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# -Ask: Install.cmd から自分で実行したとき。前に Meltype IME の登録を断っていても、もう一度聞く (自動更新では聞かない)
param([switch]$Ask, [string]$UpdateLog)

$ErrorActionPreference = 'Stop'

# 自動更新では PowerShell のウィンドウを表示しないため、標準出力と失敗理由をファイルに残す。
# ログの書き込みに失敗しても、インストールそのものは妨げない。
if ($UpdateLog) {
    try {
        $logDirectory = Split-Path -Parent $UpdateLog
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        Start-Transcript -LiteralPath $UpdateLog -Force -ErrorAction Stop | Out-Null
    }
    catch {
        $UpdateLog = $null
    }
}


# Meltype のインストール。ビルド済みの app フォルダーを %LOCALAPPDATA%\Programs\Meltype にコピーし、
# スタートアップとスタートメニューに登録して起動する。管理者権限は不要 (Meltype IME を登録するときだけ UAC で求める)。.NET は app の dotnet フォルダーに同梱しているので、インストール不要。

$source = Join-Path $PSScriptRoot 'app'
$target = Join-Path $env:LOCALAPPDATA 'Programs\Meltype'
$exe = Join-Path $target 'Meltype.exe'

# zip の中身がそろっているか (展開の途中で止まった・app フォルダーを差し替えた などで Meltype.exe が無いと、起動のところで分かりにくいエラーになる)
if (-not (Test-Path -LiteralPath (Join-Path $source 'Meltype.exe'))) {
    Write-Host "インストールするファイルが見つかりません: $(Join-Path $source 'Meltype.exe')"
    Write-Host 'zip をすべて展開し直してから、展開したフォルダーの Install.cmd を実行してください。'
    exit 1
}

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
            Write-Host 'Meltype を終了できませんでした (管理者として動いているのかもしれません)。'
            Write-Host 'タスクトレイの Meltype のアイコンを右クリックして「終了」を選んでから、もう一度実行してください。'
            throw "Meltype の終了に失敗しました: $($_.Exception.Message)"
        }
    }
    Start-Sleep -Milliseconds 300
}

# インストールの失敗で Meltype が起動しないままになるのを防ぐ。
# 更新前の実行ファイルと同じフォルダーを退避しておき、コピーや後処理が失敗したら復元する。
$backup = Join-Path $env:TEMP ('Meltype-update-backup-' + [guid]::NewGuid().ToString('N'))
$hadTarget = Test-Path -LiteralPath $target
try {
    Stop-Meltype
    # 実行中の DLL / EXE を退避しない。終了後にバックアップする。
    if ($hadTarget) {
        Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force -ErrorAction Stop
    }

# 旧名 (AutoIME) のときのものがあれば片付ける (設定と学習データは Meltype の初回起動時に引き継ぐ)。
$oldShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.lnk'
if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
$oldProgram = Join-Path $env:LOCALAPPDATA 'Programs\AutoIME'
if (Test-Path -LiteralPath $oldProgram) { Remove-Item -LiteralPath $oldProgram -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

# コピーした直後にウイルス対策ソフトが Meltype.exe を隔離することがある。誤検知かどうかはここでは判断できない。
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "コピーした Meltype.exe が見つかりません: $exe"
    Write-Host 'ウイルス対策ソフトが Meltype を止めた可能性があります。'
    Write-Host 'Windows セキュリティ →「ウイルスと脅威の防止」→「保護の更新」で定義を更新し、公式の ZIP を再ダウンロードして再検査してください。'
    Write-Host 'Windows セキュリティ →「ウイルスと脅威の防止」→「保護の履歴」で検出名を確認してください。誤検知かどうかは、この状態だけでは判断できません。'
    Write-Host '保護を無効にしたり「許可」に変更したりせず、版と検出名を https://github.com/yksr-melt/Meltype/issues に報告してください (個人名やパスは隠してください)。'
    throw "コピーした Meltype.exe が見つかりません: $exe"
}

# インターネットから落とした印 (Zone.Identifier) はコピーにも付いてくるので外す (起動時の SmartScreen の確認を減らす)
Get-ChildItem -LiteralPath $target -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# Windows の「設定 → アプリ → インストールされているアプリ」からアンインストールできるようにする。
# 前は zip を展開したフォルダーの Uninstall.cmd しか無く、zip を消していると探し直す手間がかかった。
$uninstaller = Join-Path $target 'uninstall.ps1'
$uninstallSource = Join-Path $PSScriptRoot 'uninstall.ps1'
# インストーラー (Meltype-<版>-setup.exe) で入れていたら、その「設定 → アプリ」の項目の版だけ新しくする
# (自動更新もこのスクリプトを使うので、項目が二重にならないように)。
$innoKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype_is1'
if ((Test-Path -LiteralPath $innoKey) -and (Test-Path -LiteralPath (Join-Path $target 'unins000.exe'))) {
    $version = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
    Set-ItemProperty -Path $innoKey -Name DisplayVersion -Value $version
    Set-ItemProperty -Path $innoKey -Name DisplayName -Value 'Meltype'
}
elseif (Test-Path -LiteralPath $uninstallSource) {
    Copy-Item -LiteralPath $uninstallSource -Destination $uninstaller -Force
    Unblock-File -LiteralPath $uninstaller -ErrorAction SilentlyContinue
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype'
    New-Item -Path $key -Force | Out-Null
    $size = [int]((Get-ChildItem -LiteralPath $target -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    $values = @{
        DisplayName     = 'Meltype'
        DisplayVersion  = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
        Publisher       = 'Yukishiro'
        DisplayIcon     = $exe
        InstallLocation = $target
        # PATH に PowerShell のフォルダーが無い PC もあるので、フルパスで書く
        UninstallString = "`"$(Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe')`" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$uninstaller`" -FromSettings"
        URLInfoAbout    = 'https://github.com/yksr-melt/Meltype'
    }
    foreach ($name in $values.Keys) { Set-ItemProperty -Path $key -Name $name -Value $values[$name] }
    foreach ($name in 'NoModify', 'NoRepair') { Set-ItemProperty -Path $key -Name $name -Value 1 -Type DWord }
    Set-ItemProperty -Path $key -Name EstimatedSize -Value $size -Type DWord
}

# Meltype IME (入力欄に直接入力)。登録には管理者権限が要る。断られたら、今までどおり変換ボックスで入力する方式で使う
$imeScript = Join-Path $target 'meltype-ime.ps1'
if ((Test-Path -LiteralPath $imeScript) -and (Test-Path -LiteralPath (Join-Path $target 'tip'))) {
    # 失敗しても、ショートカットを作って Meltype を起動するところまでは続ける (自動更新で Meltype が止まったままにならないように)
    try {
        . $imeScript
        # 自分で実行したとき (-Ask) か、もう登録してある (DLL を入れ替える) ときだけ。頼まれていないのに管理者権限の確認を出さない
        if ($Ask -or (Test-MeltypeImeRegistered)) { Install-MeltypeIme -Source (Join-Path $target 'tip') -Ask:$Ask | Out-Null }
    }
    catch {
        Write-Host "Meltype IME を登録できませんでした (変換ボックスで入力する方式で使えます): $($_.Exception.Message)"
    }
}

# 自動起動と、スタートメニュー・Windows 検索からの起動用 (現在のユーザー)
$shell = New-Object -ComObject WScript.Shell
foreach ($folderName in 'Startup', 'Programs') {
    $folder = [Environment]::GetFolderPath($folderName)
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'Meltype.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $target
    $shortcut.IconLocation = "$exe,0"
    $shortcut.Description = 'Meltype: 日本語と英語を自動で打ち分ける'
    $shortcut.Save()
}

# 管理者として実行していても、Meltype はふつうの権限で起動する (エクスプローラーから起動すると、ふつうの権限になる)。
# 管理者として動かすと、次のインストール・アンインストールでも管理者権限が必要になってしまう。
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin) { Start-Process -FilePath explorer.exe -ArgumentList "`"$exe`"" }
else { Start-Process -FilePath $exe -WorkingDirectory $target }
Write-Host 'Meltype をインストールして起動しました。画面右下のタスクトレイに「あ」のアイコンが出ます。'
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Host "インストールに失敗しました: $errorMessage"
    try {
        if ($hadTarget -and (Test-Path -LiteralPath $backup)) {
            # 復元先を削除してからコピーすると、復元失敗時に旧版まで失う。
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Copy-Item -Path (Join-Path $backup '*') -Destination $target -Recurse -Force -ErrorAction Stop
            Start-Process -FilePath $exe -WorkingDirectory $target
            Write-Host '更新前の Meltype を復元して起動しました。'
        }
    }
    catch {
        Write-Host "更新前の Meltype を復元できませんでした: $($_.Exception.Message)"
        Write-Host "バックアップ: $backup"
        $preserveBackup = $true
        exit 1
    }
    exit 1
}
finally {
    if (-not $preserveBackup -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($UpdateLog) { Stop-Transcript -ErrorAction SilentlyContinue | Out-Null }
}

