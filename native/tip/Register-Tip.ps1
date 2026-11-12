# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 lnkiai
#
# Meltype IME (TSF の DLL) を Windows に登録する / 登録を外す。管理者権限が要る (IME の登録は PC 全体の設定のため)。
#   powershell -ExecutionPolicy Bypass -File Register-Tip.ps1 -Source <x64 と x86 のフォルダーがある所>
#   powershell -ExecutionPolicy Bypass -File Register-Tip.ps1 -Unregister
# DLL は C:\Program Files\Meltype\tip にコピーする (管理者として動くアプリにも読み込まれるので、ユーザーが書き換えられない場所に置く)。
# 登録する DLL は stub (MeltypeTip.dll) で、stub が impl (impl\<SHA-256>\MeltypeTipImpl.dll) をロードする。
# プロセスがロードしている stub は上書きできないので、リネームして残し、次の登録のときに削除する。

param(
    [string]$Source = (Join-Path $PSScriptRoot 'bin'),
    [switch]$Unregister,
    [string]$Log
)

$ErrorActionPreference = 'Stop'
# 32 ビットの PowerShell で動いていても、64 ビットの Program Files と regsvr32 を使う。
# 環境変数 (ProgramW6432・SystemRoot など) はユーザーが上書きでき、管理者として動くこのスクリプトにも引き継がれるので使わない
$programFiles = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64).OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion').GetValue('ProgramFilesDir')
$windows = [Environment]::GetFolderPath('Windows')
$target = Join-Path $programFiles 'Meltype\tip'
$regsvr64 = Join-Path $windows 'System32\regsvr32.exe'
if (-not [Environment]::Is64BitProcess -and [Environment]::Is64BitOperatingSystem) { $regsvr64 = Join-Path $windows 'Sysnative\regsvr32.exe' }
$regsvr32 = Join-Path $windows 'SysWOW64\regsvr32.exe'

function Write-Log([string]$message) {
    Write-Host $message
    # 別の管理者のアカウントで動いていると、ログの場所 (元のユーザーの TEMP) に書けないことがある。書けなくても続ける
    if ($Log) { try { Add-Content -LiteralPath $Log -Value $message -Encoding UTF8 -ErrorAction Stop } catch { } }
}

function Invoke-Regsvr([string]$exe, [string]$dll, [switch]$Remove) {
    if (-not (Test-Path -LiteralPath $dll)) { return }
    $arguments = @('/s')
    if ($Remove) { $arguments += '/u' }
    $arguments += "`"$dll`""
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    Write-Log "$(if ($Remove) { '登録を外しました' } else { '登録しました' }): $dll (終了コード $($process.ExitCode))"
    if ($process.ExitCode -ne 0) { throw "regsvr32 が失敗しました ($($process.ExitCode)): $dll" }
}

$unregisterFailed = $false
try {
    foreach ($arch in 'x64', 'x86') {
        $exe = if ($arch -eq 'x64') { $regsvr64 } else { $regsvr32 }
        $dll = Join-Path $target "$arch\MeltypeTip.dll"
        if ($Unregister) {
            # 外せなくても (DLL が壊れているなど)、もう一方の DLL・レジストリ・ファイルは片付ける
            try { Invoke-Regsvr $exe $dll -Remove }
            catch {
                Write-Log "失敗: $($_.Exception.Message)"
                $unregisterFailed = $true
            }
            continue
        }
        $folder = Join-Path $target $arch
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        # 前の登録の残り (使われていて消せなかったもの) を片付ける
        Get-ChildItem -LiteralPath $folder -Filter '*.old' -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }
        # impl は、署名前のバイナリの SHA-256 のフォルダーに置く (プロセスがロードしている前の impl は、そのまま残る)。
        # SHA-256 をファイル名にすると、Gecko (Firefox など) がロードを拒否する (Stub.cpp の CreateInner)。
        # stub がロードする impl の指定 (MeltypeTipImpl.dll.sha256) は、stub を登録できてから書き換える (前の stub に戻したときは、前の impl を指したまま)
        $implHash = "$(Get-Content -LiteralPath (Join-Path $Source "$arch\MeltypeTipImpl.dll.sha256") -Raw)".Trim().ToUpperInvariant()
        if ($implHash -notmatch '^[0-9A-F]{64}$') { throw "$arch\MeltypeTipImpl.dll.sha256 の中身が SHA-256 ではありません。" }
        $implFolder = Join-Path $folder 'impl'
        $implDirectory = Join-Path $implFolder $implHash
        New-Item -ItemType Directory -Force -Path $implDirectory | Out-Null
        $impl = Join-Path $implDirectory 'MeltypeTipImpl.dll'
        $staged = "$impl.new"
        Copy-Item -LiteralPath (Join-Path $Source "$arch\MeltypeTipImpl.dll") -Destination $staged -Force
        try { Move-Item -LiteralPath $staged -Destination $impl -Force }
        catch {
            # 同じ SHA-256 の impl をプロセスがロードしていて上書きできない: 配置済みのものを使う
            Remove-Item -LiteralPath $staged -Force
            if (-not (Test-Path -LiteralPath $impl)) { throw }
        }
        $old = $null
        if (Test-Path -LiteralPath $dll) {
            $old = "$dll.$([DateTime]::Now.ToString('yyyyMMddHHmmss')).old"
            Move-Item -LiteralPath $dll -Destination $old -Force
        }
        # 前のハッシュは、登録できるまで消しておく (途中で失敗したら、次に入れ直したときに「同じ DLL」と思わずに登録し直す)
        Remove-Item -LiteralPath "$dll.sha256" -Force -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath (Join-Path $Source "$arch\MeltypeTip.dll") -Destination $dll -Force
        try { Invoke-Regsvr $exe $dll }
        catch {
            # 新しい DLL を登録できない (壊れている・必要なものが無い): 登録済みの場所に置いたままにすると、アプリがそれを読み込むので、
            # 前の DLL に戻して登録し直す (登録に失敗した DLL は、前の登録も外してしまうため)
            $registerError = $_
            if ($old) {
                $failed = "$dll.$([DateTime]::Now.ToString('yyyyMMddHHmmss')).failed.old"
                try {
                    Move-Item -LiteralPath $dll -Destination $failed -Force -ErrorAction Stop
                    Move-Item -LiteralPath $old -Destination $dll -Force -ErrorAction Stop
                    Invoke-Regsvr $exe $dll
                    Write-Log '前の DLL に戻しました。'
                }
                catch { Write-Log "前の DLL に戻せませんでした: $($_.Exception.Message)" }
            }
            throw $registerError
        }
        [IO.File]::WriteAllText((Join-Path $folder 'MeltypeTipImpl.dll.sha256'), $implHash)
        # 前の impl は、どのプロセスもロードしていなければ削除できる (ロードされている impl は、フォルダーごと残る)
        Get-ChildItem -LiteralPath $implFolder | Where-Object { $_.FullName -ne $implDirectory } | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue }
        # 署名する前の中身のハッシュ (次の更新で、同じ DLL なら登録し直さないため)
        $hash = Join-Path $Source "$arch\MeltypeTip.dll.sha256"
        if (Test-Path -LiteralPath $hash) { Copy-Item -LiteralPath $hash -Destination "$dll.sha256" -Force }
    }
    if ($Unregister) {
        # DLL が無くて regsvr32 で外せなかったときのために、登録をレジストリから直接消す (有っても消してよい。64 ビットと 32 ビットの両方の場所)
        $clsid = '{417D801B-A9BD-4C26-BD16-356A825A6998}'
        foreach ($view in [Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32) {
            $machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, $view)
            try {
                foreach ($key in "SOFTWARE\Microsoft\CTF\TIP\$clsid", "SOFTWARE\Classes\CLSID\$clsid") { $machine.DeleteSubKeyTree($key, $false) }
            }
            finally { $machine.Dispose() }
        }
        $folder = Join-Path $programFiles 'Meltype'
        Remove-Item -LiteralPath $folder -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $folder) {
            # アプリが読み込んだままの DLL は消せないので、次に Windows を起動したときに消す。
            # 名前を変えてから消す予約をする (元の名前のまま予約すると、再起動の前に入れ直した DLL まで消える)
            Add-Type -Namespace Meltype -Name Native -MemberDefinition '[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern bool MoveFileEx(string from, string to, int flags);'
            $stamp = [DateTime]::Now.ToString('yyyyMMddHHmmss')
            foreach ($file in @(Get-ChildItem -LiteralPath $folder -Recurse -File -ErrorAction SilentlyContinue)) {
                $path = $file.FullName
                if ($file.Extension -ne '.old') {
                    $renamed = "$path.$stamp.old"
                    try { Move-Item -LiteralPath $path -Destination $renamed -Force -ErrorAction Stop; $path = $renamed } catch { }
                }
                [Meltype.Native]::MoveFileEx($path, $null, 4) | Out-Null  # 4 = MOVEFILE_DELAY_UNTIL_REBOOT
            }
            # フォルダーは空のときだけ消える (入れ直していれば残る)
            $folders = @(Get-ChildItem -LiteralPath $folder -Recurse -Directory -ErrorAction SilentlyContinue | Sort-Object { $_.FullName.Length } -Descending | ForEach-Object { $_.FullName }) + $folder
            foreach ($path in $folders) { [Meltype.Native]::MoveFileEx($path, $null, 4) | Out-Null }
            Write-Log '使われていて消せないファイルは、次に Windows を起動したときに消します。'
        }
    }
    elseif ($PSCommandPath -ne (Join-Path $target 'Register-Tip.ps1')) {
        # アンインストールのときに使えるように、このスクリプトも置いておく
        Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $target 'Register-Tip.ps1') -Force
    }
    # regsvr32 で外せなかった登録も、上でレジストリから直接消したので、外せたことにする
    if ($unregisterFailed) { Write-Log 'regsvr32 で外せなかった登録は、レジストリから消しました。' }
    Write-Log 'OK'
    exit 0
}
catch {
    Write-Log "失敗: $($_.Exception.Message)"
    exit 1
}
