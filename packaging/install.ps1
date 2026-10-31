# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# -Ask: Install.cmd から自分で実行したとき。前に Meltype IME の登録を断っていても、もう一度聞く (自動更新では聞かない)
param([switch]$Ask, [string]$UpdateLog)

$ErrorActionPreference = 'Stop'

# 自動更新では PowerShell のウィンドウを表示しないため、標準出力と失敗理由をファイルに残す。
# ログの書き込みに失敗しても、インストールそのものは妨げない。
# 前の版の Meltype は -UpdateLog を渡さないので、更新用のフォルダーで動いているときは引数が無くても記録する。
$updateDirectory = Join-Path $env:LOCALAPPDATA 'Meltype\update'
$via = if ($UpdateLog) { 'arg' } elseif ($Ask) { 'ask' } else { 'manual' }
if (-not $UpdateLog -and (Split-Path -Parent $PSScriptRoot) -eq $updateDirectory) {
    $UpdateLog = Join-Path $updateDirectory 'install.log'
    $via = 'folder'
}

# ログは Issue に貼られることがある。Transcript の見出しや失敗の文に入る利用者名・コンピューター名・パスを、環境変数の名前に置き換える。
function Hide-Personal([string]$Text) {
    # TEMP は 8.3 形式のことがあるので、利用者名より先に、値が長い順にパスを置き換える
    $paths = 'TEMP', 'TMP', 'LOCALAPPDATA', 'APPDATA', 'USERPROFILE' |
        ForEach-Object { [pscustomobject]@{ Name = $_; Value = [Environment]::GetEnvironmentVariable($_) } } |
        Where-Object { $_.Value } | Sort-Object { $_.Value.Length } -Descending
    foreach ($path in $paths) { $Text = $Text -ireplace [regex]::Escape($path.Value), "%$($path.Name)%" }
    foreach ($name in 'USERDOMAIN', 'USERNAME', 'COMPUTERNAME') {
        $value = [Environment]::GetEnvironmentVariable($name)
        # 日本語が直接続くことがあるので、英数字の語の一部だけを避ける
        if ($value) { $Text = $Text -ireplace "(?<![A-Za-z0-9_])$([regex]::Escape($value))(?![A-Za-z0-9_])", "%$name%" }
    }
    # 8 文字を超える名前の 8.3 形式 (ABCDEF~1) は、上の値と一致しない
    $user = ([string]$env:USERNAME) -replace '[ .]', ''
    if ($user.Length -gt 8) { $Text = $Text -ireplace "$([regex]::Escape($user.Substring(0, 6)))~\d+", '%USERNAME%' }
    $Text
}

function Protect-UpdateLog {
    try {
        if (Test-Path -LiteralPath $UpdateLog) {
            [IO.File]::WriteAllText($UpdateLog, (Hide-Personal ([IO.File]::ReadAllText($UpdateLog))), (New-Object Text.UTF8Encoding $true))
        }
    }
    catch { }
}

function Close-UpdateLog {
    if (-not $UpdateLog) { return }
    try { Stop-Transcript -ErrorAction SilentlyContinue | Out-Null } catch { }
    Protect-UpdateLog
}

# 調べるときに探す語。ログを書かないとき (Install.cmd から実行したとき) は画面に出さない。
function Write-Mark([string]$Text) {
    if ($UpdateLog) { Write-Host "$(Get-Date -Format o) $Text" }
}

if ($UpdateLog) {
    try {
        $logDirectory = Split-Path -Parent $UpdateLog
        New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
        # 前回が途中で止まると、伏せる前の見出しが残っている。成功した実行で失敗の記録を消さないよう、追記する
        Protect-UpdateLog
        if ((Test-Path -LiteralPath $UpdateLog) -and (Get-Item -LiteralPath $UpdateLog).Length -gt 1MB) {
            Move-Item -LiteralPath $UpdateLog -Destination ([IO.Path]::ChangeExtension($UpdateLog, '.old.log')) -Force
        }
        Start-Transcript -LiteralPath $UpdateLog -Append -Force -ErrorAction Stop | Out-Null
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
$innoKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype_is1'
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

# 不具合を調べるための環境。利用者名・コンピューター名・パスは書かない。ここの失敗でインストールは止めない。
function Write-Environment {
    try {
        $version = { param($path) if (Test-Path -LiteralPath $path) { (Get-Item -LiteralPath $path).VersionInfo.ProductVersion } else { 'none' } }
        $os = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $free = [IO.DriveInfo]::new($target.Substring(0, 1)).AvailableFreeSpace
        $running = @(Get-Process Meltype -ErrorAction SilentlyContinue).Count
        Write-Mark "start run=$([guid]::NewGuid().ToString('N')) via=$via new=$(& $version (Join-Path $source 'Meltype.exe')) old=$(& $version $exe) inno=$(Test-Path -LiteralPath $innoKey)"
        Write-Mark "env os=$($os.EditionID)/$($os.DisplayVersion)/$($os.CurrentBuild).$($os.UBR) arch=$env:PROCESSOR_ARCHITECTURE/$env:PROCESSOR_ARCHITEW6432 ps=$($PSVersionTable.PSVersion)/$($ExecutionContext.SessionState.LanguageMode) elevated=$admin ui=$PSUICulture free_mb=$([long]($free / 1MB)) pathascii=$($target -match '^[\x00-\x7F]*$') running=$running"
    }
    catch {
        Write-Mark "env error=$($_.Exception.GetType().FullName)"
    }
}
Write-Environment

function Start-InstalledMeltype {
    # 管理者として実行していても、Meltype はふつうの権限で起動する (エクスプローラーから起動すると、ふつうの権限になる)。
    # 管理者として動かすと、次のインストール・アンインストールでも管理者権限が必要になってしまう。
    if ($admin) { Start-Process -FilePath explorer.exe -ArgumentList "`"$exe`"" }
    else { Start-Process -FilePath $exe -WorkingDirectory $target }
}

# 更新の失敗で Meltype が止まったままだと、次のサインインまで入力できない。動いていなければ、残っている版を起動する。
function Start-RemainingMeltype {
    if ((Get-Process Meltype -ErrorAction SilentlyContinue) -or -not (Test-Path -LiteralPath $exe)) { return }
    try {
        Start-InstalledMeltype
        Write-Mark 'fallback=started'
    }
    catch {
        Write-Mark "fallback=failed type=$($_.Exception.GetType().FullName)"
    }
}

# zip の中身がそろっているか (展開の途中で止まった・app フォルダーを差し替えた などで Meltype.exe が無いと、起動のところで分かりにくいエラーになる)
if (-not (Test-Path -LiteralPath (Join-Path $source 'Meltype.exe'))) {
    Write-Host "インストールするファイルが見つかりません: $(Join-Path $source 'Meltype.exe')"
    Write-Host 'zip をすべて展開し直してから、展開したフォルダーの Install.cmd を実行してください。'
    Write-Mark 'result=failed reason=missing-exe'
    # 起動時の更新では、前の版はもう終わっている
    if ($UpdateLog) { Start-RemainingMeltype }
    Close-UpdateLog
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
    Write-Mark 'phase=stop'
    Stop-Meltype
    # 実行中の DLL / EXE を退避しない。終了後にバックアップする。
    if ($hadTarget) {
        Write-Mark 'phase=backup'
        Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force -ErrorAction Stop
    }

# 旧名 (AutoIME) のときのものがあれば片付ける (設定と学習データは Meltype の初回起動時に引き継ぐ)。
$oldShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\AutoIME.lnk'
if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
$oldProgram = Join-Path $env:LOCALAPPDATA 'Programs\AutoIME'
if (Test-Path -LiteralPath $oldProgram) { Remove-Item -LiteralPath $oldProgram -Recurse -Force }
New-Item -ItemType Directory -Force -Path $target | Out-Null
Write-Mark 'phase=copy'
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force

Write-Mark 'phase=verify'
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
Write-Mark 'phase=register'
$uninstaller = Join-Path $target 'uninstall.ps1'
$uninstallSource = Join-Path $PSScriptRoot 'uninstall.ps1'
# インストーラー (Meltype-<版>-setup.exe) で入れていたら、その「設定 → アプリ」の項目の版だけ新しくする
# (自動更新もこのスクリプトを使うので、項目が二重にならないように)。
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
    Write-Mark 'phase=ime'
    try {
        . $imeScript
        $imeRegistered = Test-MeltypeImeRegistered
        Write-Mark "ime registered=$imeRegistered"
        # 自分で実行したとき (-Ask) か、もう登録してある (DLL を入れ替える) ときだけ。頼まれていないのに管理者権限の確認を出さない
        if ($Ask -or $imeRegistered) { Install-MeltypeIme -Source (Join-Path $target 'tip') -Ask:$Ask | Out-Null }
    }
    catch {
        Write-Host "Meltype IME を登録できませんでした (変換ボックスで入力する方式で使えます): $($_.Exception.Message)"
    }
}

Write-Mark 'phase=shortcut'
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

Write-Mark 'phase=launch'
Start-InstalledMeltype
Write-Host 'Meltype をインストールして起動しました。画面右下のタスクトレイに「あ」のアイコンが出ます。'
Write-Mark 'result=ok'
}
catch {
    $errorMessage = $_.Exception.Message
    Write-Host "インストールに失敗しました: $errorMessage"
    Write-Mark "error type=$($_.Exception.GetType().FullName) hresult=$('0x{0:X8}' -f $_.Exception.HResult) line=$($_.InvocationInfo.ScriptLineNumber)"
    try {
        if ($hadTarget -and (Test-Path -LiteralPath $backup)) {
            # 復元先を削除してからコピーすると、復元失敗時に旧版まで失う。
            New-Item -ItemType Directory -Path $target -Force | Out-Null
            Copy-Item -Path (Join-Path $backup '*') -Destination $target -Recurse -Force -ErrorAction Stop
            Start-InstalledMeltype
            Write-Host '更新前の Meltype を復元して起動しました。'
            Write-Mark 'result=rolled-back'
        }
        else {
            Write-Mark 'result=failed'
            if ($hadTarget) { Start-RemainingMeltype }
        }
    }
    catch {
        Write-Host "更新前の Meltype を復元できませんでした: $($_.Exception.Message)"
        Write-Host "バックアップ: $backup"
        Write-Mark 'result=rollback-failed'
        $preserveBackup = $true
        Start-RemainingMeltype
        exit 1
    }
    exit 1
}
finally {
    if (-not $preserveBackup -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
    }
    Close-UpdateLog
}

