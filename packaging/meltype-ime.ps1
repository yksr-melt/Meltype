# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 lnkiai
#
# Meltype IME (Windows の IME として入力欄に直接入力する。native\tip) を入れる / 外す。install.ps1・uninstall.ps1 から読み込んで使う。
#   . .\meltype-ime.ps1
#   Install-MeltypeIme -Source <x64 と x86 と Register-Tip.ps1 があるフォルダー>
#   Uninstall-MeltypeIme
# IME の登録は PC 全体の設定なので、ここだけ管理者権限を求める (UAC の確認が出る)。
# 「言語と地域」のキーボードの一覧に足すのは、ふつうの権限で (今のユーザーの設定なので)。

$MeltypeImeTip = '0411:{417D801B-A9BD-4C26-BD16-356A825A6998}{21F643F4-72D5-4946-BF70-0A126AB52D05}'

# 64 ビットの Program Files (32 ビットの PowerShell から動かしても、64 ビットの方を使う)。
# 管理者として動かす Register-Tip.ps1 は、32 ビットの PowerShell で動いても 64 ビットの場所と regsvr32 を使う。
# 環境変数 (ProgramW6432 など) はユーザーが上書きできるので、Windows の設定 (HKLM) から読む
$MeltypeProgramFiles = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64).OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion').GetValue('ProgramFilesDir')

# ARM64 の Windows か (x64 のエミュレーションで動いている PowerShell では、環境変数が AMD64 になるので、Windows の設定から読む)
function Test-Arm64Windows {
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64' -or $env:PROCESSOR_ARCHITEW6432 -eq 'ARM64') { return $true }
    $native = (Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' -Name PROCESSOR_ARCHITECTURE -ErrorAction SilentlyContinue).PROCESSOR_ARCHITECTURE
    return $native -eq 'ARM64'
}

function Test-MeltypeImeRegistered {
    return Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\CTF\TIP\{417D801B-A9BD-4C26-BD16-356A825A6998}'
}

# DLL の登録 (InprocServer32) が 64 ビット・32 ビットの両方にあり、その DLL があるか。
# TIP のキーだけ残り、これが無いことがある (セキュリティソフトが登録を途中で止めたとき)
function Test-MeltypeImeServer {
    foreach ($view in [Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32) {
        $key = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view).OpenSubKey('SOFTWARE\Classes\CLSID\{417D801B-A9BD-4C26-BD16-356A825A6998}\InprocServer32')
        if (-not $key) { return $false }
        try { $dll = $key.GetValue('') }
        finally { $key.Dispose() }
        if (-not $dll -or -not (Test-Path -LiteralPath $dll)) { return $false }
    }
    return $true
}

# 管理者として動かす処理。確かめてから使うまでの間にファイルをすり替えられないように、ユーザーが書き換えられない
# Program Files の下に写してから、昇格する前に確かめたハッシュと比べ、合ったものだけで Register-Tip.ps1 を動かす。
# この処理はファイルではなくコマンドとして渡す (ユーザーのフォルダーにあるスクリプトを、管理者として直接動かさない)
$MeltypeImeElevatedCommand = {
    param([string]$Source, [string]$Files, [string]$Log, [string]$Mode)
    $ErrorActionPreference = 'Stop'
    $code = 1
    # 環境変数はユーザーが上書きできる (管理者として動くプロセスにも引き継がれる) ので、Windows の設定 (HKLM) から読む
    $programFiles = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64).OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion').GetValue('ProgramFilesDir')
    $stage = Join-Path $programFiles "Meltype\stage-$([Guid]::NewGuid().ToString('N'))"
    try {
        # Files: 'Source からの相対パス=SHA-256' を | でつないだもの
        foreach ($entry in $Files.Split('|')) {
            $relative, $expected = $entry.Split('=')
            $copy = Join-Path $stage $relative
            New-Item -ItemType Directory -Force -Path (Split-Path $copy) | Out-Null
            Copy-Item -LiteralPath (Join-Path $Source $relative) -Destination $copy -Force
            $sha = [System.Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($copy))).Replace('-', '') }
            finally { $sha.Dispose() }
            if ($actual -ne $expected) { throw "$relative が、確かめたときのものと違います (書き換えられた可能性があります)。登録しません。" }
        }
        $arguments = @{ Log = $Log }
        if ($Mode -eq 'Unregister') { $arguments.Unregister = $true }
        else { $arguments.Source = $stage }
        & (Join-Path $stage 'Register-Tip.ps1') @arguments
        $code = $LASTEXITCODE
    }
    catch {
        # 別の管理者のアカウントで動いていると、ログの場所 (元のユーザーの TEMP) に書けないことがある
        try { Add-Content -LiteralPath $Log -Value "失敗: $($_.Exception.Message)" -Encoding UTF8 } catch { }
    }
    finally { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue }
    exit $code
}

# 管理者として Register-Tip.ps1 を動かす。$Hashes: Source からの相対パス → SHA-256 (Register-Tip.ps1 を含める)。
# 'ok' / 'failed' / 'cancelled' (UAC で「いいえ」) を返す
function Invoke-Elevated([string]$Source, [hashtable]$Hashes, [string]$Mode) {
    $log = Join-Path $env:TEMP "meltype-ime-$([Guid]::NewGuid().ToString('N')).log"
    $quote = { param([string]$text) "'" + $text.Replace("'", "''") + "'" }
    $files = ($Hashes.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join '|'
    $command = "& { $MeltypeImeElevatedCommand } $(& $quote $Source) $(& $quote $files) $(& $quote $log) $(& $quote $Mode)"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $all = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    # PATH から探さず、Windows のフォルダーの powershell.exe を使う
    $powershell = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe'
    try {
        $process = Start-Process -FilePath $powershell -ArgumentList $all -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    }
    catch {
        # UAC で「いいえ」を選んだ
        return 'cancelled'
    }
    if (Test-Path -LiteralPath $log) {
        Get-Content -LiteralPath $log -Encoding UTF8 | ForEach-Object { Write-Host "  $_" }
        Remove-Item -LiteralPath $log -Force -ErrorAction SilentlyContinue
    }
    if ($process.ExitCode -eq 0) { return 'ok' }
    return 'failed'
}

function Add-MeltypeImeToLanguageList {
    $list = Get-WinUserLanguageList
    $japanese = $list | Where-Object { $_.LanguageTag -like 'ja*' } | Select-Object -First 1
    if (-not $japanese) {
        $list.Add('ja-JP')
        $japanese = $list | Where-Object { $_.LanguageTag -like 'ja*' } | Select-Object -First 1
    }
    if ($japanese.InputMethodTips -notcontains $MeltypeImeTip) {
        $japanese.InputMethodTips.Add($MeltypeImeTip)
        Set-WinUserLanguageList $list -Force
    }
    # 既定の入力方式を自分で決めていなければ、Meltype にする (サインインし直したときも Meltype から始まるように)。
    # 自分で決めているときは変えない
    $override = Get-WinDefaultInputMethodOverride -ErrorAction SilentlyContinue
    if (-not "$($override | Out-String)".Trim()) { Set-WinDefaultInputMethodOverride -InputTip $MeltypeImeTip }
}

function Remove-MeltypeImeFromLanguageList {
    # 既定の入力方式が Meltype のままなら、決めていない状態に戻す
    $override = Get-WinDefaultInputMethodOverride -ErrorAction SilentlyContinue
    if ("$($override | Out-String)" -match '417D801B-A9BD-4C26-BD16-356A825A6998') { Set-WinDefaultInputMethodOverride }
    $list = Get-WinUserLanguageList
    $changed = $false
    foreach ($language in $list) {
        if ($language.InputMethodTips -contains $MeltypeImeTip) {
            $language.InputMethodTips.Remove($MeltypeImeTip) | Out-Null
            $changed = $true
        }
    }
    if ($changed) { Set-WinUserLanguageList $list -Force }
}

$MeltypeImeDeclinedKey = 'HKCU:\Software\Meltype'

# 登録を断ったことを覚える。自動更新と、Meltype.exe が起動したときには聞かない (Install.cmd を自分で実行したときは聞く)
function Set-MeltypeImeDeclined {
    New-Item -Path $MeltypeImeDeclinedKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $MeltypeImeDeclinedKey -Name ImeDeclined -Value 1 -Type DWord
}

# ファイルの SHA-256 (Get-FileHash は、ほかのシェルから起動した PowerShell では読み込めないことがあるので .NET で計算する)
function Get-Sha256([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-', '') }
    finally { $sha.Dispose() }
}

# 登録済みの DLL が、これから入れるものと同じか
function Test-MeltypeImeCurrent([string]$Source) {
    if (-not (Test-MeltypeImeRegistered)) { return $false }
    # 途中で止まった登録は、DLL が同じでも登録し直す
    if (-not (Test-MeltypeImeServer)) { return $false }
    # 登録を外すときに使うスクリプト (Program Files に置いたもの) も同じか (DLL が同じでも、スクリプトを直したら置き直す)
    $installedScript = Join-Path $MeltypeProgramFiles 'Meltype\tip\Register-Tip.ps1'
    $newScript = Join-Path $Source 'Register-Tip.ps1'
    if (-not (Test-Path -LiteralPath $installedScript) -or -not (Test-Path -LiteralPath $newScript)) { return $false }
    if ((Get-Sha256 $installedScript) -ne (Get-Sha256 $newScript)) { return $false }
    foreach ($arch in 'x64', 'x86') {
        $installed = Join-Path $MeltypeProgramFiles "Meltype\tip\$arch\MeltypeTip.dll"
        $new = Join-Path $Source "$arch\MeltypeTip.dll"
        if (-not (Test-Path -LiteralPath $installed) -or -not (Test-Path -LiteralPath $new)) { return $false }
        # 署名する前の中身のハッシュがあれば、それで比べる (署名した DLL は、中身が同じでも毎回違うファイルになる)
        $installedHash = Read-HashFile "$installed.sha256"
        $newHash = Read-HashFile "$new.sha256"
        # 新しい方にハッシュがあるのに、インストール先に無い: 前の登録が途中で失敗した (登録できてからハッシュを置くため)。登録し直す
        if ($newHash -and -not $installedHash) { return $false }
        if ($installedHash -and $newHash) {
            if ($installedHash -ne $newHash) { return $false }
            continue
        }
        if ((Get-Sha256 $installed) -ne (Get-Sha256 $new)) { return $false }
    }
    return $true
}

# ハッシュのファイル (MeltypeTip.dll.sha256) の中身。無い・空なら $null
function Read-HashFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $text = "$(Get-Content -LiteralPath $path -Raw -ErrorAction SilentlyContinue)".Trim()
    if ($text) { return $text.ToUpperInvariant() }
    return $null
}

# 管理者として動かす前に、これから入れる DLL が、ビルドしたときのハッシュと合っているかを確かめる。
# 壊れた・途中までしかコピーされていない DLL を登録しないためのもの。ハッシュのファイルも同じフォルダー (ユーザーが書き換えられる場所)
# にあるので、同じユーザーの権限で動くプログラムによる書き換えは防げない (SECURITY.md)。
# 確かめたファイル (Register-Tip.ps1 も) のハッシュを返す。管理者として動く側は、写したものがこれと合うかを確かめる。合わなければ $null
function Get-MeltypeImeSourceHashes([string]$Source) {
    $hashes = @{}
    foreach ($arch in 'x64', 'x86') {
        $dll = Join-Path $Source "$arch\MeltypeTip.dll"
        # 配布用のパッケージなら署名した後のハッシュ、ソースから入れたとき (署名しない) は署名する前のハッシュ
        $expected = Read-HashFile "$dll.package.sha256"
        if (-not $expected) { $expected = Read-HashFile "$dll.sha256" }
        if (-not (Test-Path -LiteralPath $dll)) { return $null }
        $actual = Get-Sha256 $dll
        if ($expected -and $actual -ne $expected) { return $null }
        $hashes["$arch\MeltypeTip.dll"] = $actual
        # 署名する前の中身のハッシュ (Program Files に置き、次の更新で同じ DLL なら登録し直さないのに使う) も、すり替えられないように一緒に確かめる
        if (Test-Path -LiteralPath "$dll.sha256") { $hashes["$arch\MeltypeTip.dll.sha256"] = Get-Sha256 "$dll.sha256" }
    }
    $hashes['Register-Tip.ps1'] = Get-Sha256 (Join-Path $Source 'Register-Tip.ps1')
    return $hashes
}

# -Ask: 前に断られていても聞く (Install.cmd を自分で実行したとき)。自動更新では聞かない
function Install-MeltypeIme([string]$Source, [switch]$Ask) {
    $register = Join-Path $Source 'Register-Tip.ps1'
    if (-not (Test-Path -LiteralPath $register)) { return $false }
    # ARM64 の Windows では、ARM64 のアプリ (メモ帳・エクスプローラーなど) に x64 / x86 の DLL が読み込まれず、日本語が打てなくなる
    if (Test-Arm64Windows) {
        Write-Host 'ARM64 の Windows には Meltype IME はまだ対応していません。変換ボックスで入力する方式で使えます。'
        return $false
    }
    $hashes = Get-MeltypeImeSourceHashes $Source
    if (-not $hashes) {
        Write-Host 'Meltype IME の DLL が、ビルドしたときのものと違います (壊れているか、書き換えられています)。登録しません。'
        return $false
    }
    if (Test-MeltypeImeCurrent $Source) {
        try { Add-MeltypeImeToLanguageList } catch { }
        return $true
    }
    $declined = (Get-ItemProperty -LiteralPath $MeltypeImeDeclinedKey -Name ImeDeclined -ErrorAction SilentlyContinue).ImeDeclined -eq 1
    if ($declined -and -not $Ask) {
        Write-Host '前に Meltype IME の登録を断ったので、登録しません (Install.cmd か Install-Meltype.ps1 を実行すると、もう一度聞きます)。'
        return $false
    }
    Write-Host 'Meltype IME (入力欄に直接入力) を Windows に登録します。管理者権限の確認が出たら「はい」を選んでください。'
    $result = Invoke-Elevated $Source $hashes 'Register'
    if ($result -ne 'ok') {
        Write-Host 'Meltype IME を登録できませんでした。今までどおり、変換ボックスで入力する方式で使えます。'
        # UAC で断られたときだけ覚えて、自動更新では聞かない (失敗したときは、次の更新でまた試す)
        if ($result -eq 'cancelled') { Set-MeltypeImeDeclined }
        return $false
    }
    Remove-ItemProperty -LiteralPath $MeltypeImeDeclinedKey -Name ImeDeclined -ErrorAction SilentlyContinue
    try {
        Add-MeltypeImeToLanguageList
    }
    catch {
        Write-Host "キーボードの一覧に Meltype を足せませんでした: $($_.Exception.Message)"
        Write-Host '「設定」→「時刻と言語」→「言語と地域」→ 日本語 の「…」→「言語のオプション」→「キーボードの追加」で Meltype を選んでください。'
    }
    Write-Host 'Meltype IME を登録しました。Win + Space で「Meltype」を選ぶと、入力欄に直接入力できます。'
    return $true
}

function Uninstall-MeltypeIme {
    try { Remove-MeltypeImeFromLanguageList } catch { }
    # 登録を断った記録も消す
    Remove-Item -LiteralPath $MeltypeImeDeclinedKey -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-MeltypeImeRegistered)) { return $true }
    $register = Join-Path $MeltypeProgramFiles 'Meltype\tip\Register-Tip.ps1'
    # Program Files に無ければ、同じ場所 (インストール先) の tip か、ソースの native\tip のもの
    if (-not (Test-Path -LiteralPath $register)) { $register = Join-Path $PSScriptRoot 'tip\Register-Tip.ps1' }
    if (-not (Test-Path -LiteralPath $register)) { $register = Join-Path $PSScriptRoot '..\native\tip\Register-Tip.ps1' }
    if (-not (Test-Path -LiteralPath $register)) { return $false }
    Write-Host 'Meltype IME の登録を外します。管理者権限の確認が出たら「はい」を選んでください。'
    return (Invoke-Elevated (Split-Path $register) @{ 'Register-Tip.ps1' = (Get-Sha256 $register) } 'Unregister') -eq 'ok'
}
