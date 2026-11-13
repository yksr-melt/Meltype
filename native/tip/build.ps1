# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 lnkiai
#
# Meltype IME (TSF の DLL) をビルドする。Visual Studio Build Tools (C++) が要る。
#   powershell -ExecutionPolicy Bypass -File native\tip\build.ps1 [-Configuration Release|Debug]
# 出力: native\tip\bin\x64・native\tip\bin\arm64・native\tip\bin\x86 (32bit のアプリ用) に、MeltypeTip.dll (stub) と MeltypeTipImpl.dll (impl)
# arm64 は ARM64X (ARM64 と ARM64EC のコードを 1 つの DLL に入れたもの)。ARM64 の Windows では、ARM64 のアプリと x64 のアプリが
# 64 ビットの同じレジストリ (InprocServer32) を見るので、どちらのプロセスにも読み込める DLL を 1 つ登録する

param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio (Build Tools) の C++ ビルドツールが見つかりません。Meltype IME をビルドするには、Visual Studio Build Tools の「C++ によるデスクトップ開発」と「MSVC ARM64 ビルド ツール」を入れてください。' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 Microsoft.VisualStudio.Component.VC.Tools.ARM64 -property installationPath
if (-not $vs) { throw 'Visual Studio の C++ ビルドツールが見つかりません。' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
# vcvarsall.bat は vswhere.exe を PATH から探すので、見つからないという表示が出ないように足しておく
$env:PATH = (Split-Path $vswhere) + ';' + $env:PATH

# ファイルの SHA-256 (Get-FileHash は、ほかのシェルから起動した PowerShell では読み込めないことがあるので .NET で計算する)
function Get-Sha256([string]$path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($path))).Replace('-', '') }
    finally { $sha.Dispose() }
}

$dlls = @(
    @{ Name = 'MeltypeTip'; Sources = 'Dll.cpp', 'Stub.cpp', 'Common.cpp' },
    @{ Name = 'MeltypeTipImpl'; Sources = 'Impl.cpp', 'TextService.cpp', 'CandidateWindow.cpp', 'DisplayAttributes.cpp', 'LangBar.cpp', 'Pipe.cpp', 'Json.cpp', 'Common.cpp' }
)
# /Brepro: 同じソースからは同じ DLL にする (日時を埋め込まない)。入れ直したときに、変わっていない DLL を登録し直さない (UAC を出さない) ため
$optimize = if ($Configuration -eq 'Release') { '/O2 /MT /DNDEBUG' } else { '/Od /MTd /Zi' }
$libs = 'ole32.lib oleaut32.lib uuid.lib user32.lib gdi32.lib advapi32.lib d2d1.lib dwrite.lib dwmapi.lib'

$cl = "cl /nologo /c /Brepro /std:c++17 /EHsc /W4 /wd4100 /utf-8 /DUNICODE /D_UNICODE $optimize"
$sources = @($dlls.Sources | Select-Object -Unique)

foreach ($arch in 'x64', 'arm64', 'x86') {
    $out = Join-Path $here "bin\$arch"
    $obj = Join-Path $here "obj\$arch"
    $vcarch = @{ x64 = 'x64'; arm64 = 'x64_arm64'; x86 = 'x64_x86' }[$arch]
    # ARM64X は、同じソースを ARM64 と ARM64EC で 1 回ずつコンパイルし、まとめてリンクする
    $passes = if ($arch -eq 'arm64') { @(@{ Dir = "$obj\native"; Flag = '' }, @{ Dir = "$obj\ec"; Flag = '/arm64EC' }) } else { @(@{ Dir = $obj; Flag = '' }) }
    New-Item -ItemType Directory -Force -Path (@($out, $obj) + $passes.Dir) | Out-Null
    $machine = if ($arch -eq 'arm64') { '/MACHINE:ARM64X' } else { '' }
    $cmd = @(
        "`"$vcvars`" $vcarch >nul",
        "cd /d `"$here`"",
        "rc /nologo /fo `"$obj\MeltypeTip.res`" MeltypeTip.rc"
    ) + @($passes | ForEach-Object { "$cl $($_.Flag) /Fo`"$($_.Dir)\\`" /Fd`"$($_.Dir)\\`" $($sources -join ' ')" }) + @($dlls | ForEach-Object {
        $name = $_.Name
        $objects = foreach ($pass in $passes) { foreach ($source in $_.Sources) { "`"$($pass.Dir)\$([IO.Path]::ChangeExtension($source, 'obj'))`"" } }
        # ARM64X の DLL は、ARM64 から見える関数 (/DEFARM64NATIVE) と x64 から見える関数 (/DEF) を別々に指定する
        $def = if ($arch -eq 'arm64') { "/DEFARM64NATIVE:$name.def /DEF:$name.def" } else { "/DEF:$name.def" }
        "link /nologo /DLL /Brepro $machine $def /IMPLIB:`"$obj\$name.lib`" /OUT:`"$out\$name.dll`" $($objects -join ' ') `"$obj\MeltypeTip.res`" $libs"
    }) -join ' && '
    Write-Host "== $arch =="
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "$arch のビルドに失敗しました。" }
    # 署名する前の中身のハッシュ。インストールのとき、登録済みの DLL と同じかを比べるのに使う (署名には毎回違う時刻が入るため)。
    # impl のハッシュは、インストール先での impl のフォルダー名にもなり、Meltype.exe が要求の impl と照合する
    foreach ($name in $dlls.Name) {
        $dll = Join-Path $out "$name.dll"
        [IO.File]::WriteAllText("$dll.sha256", (Get-Sha256 $dll))
    }
}
Write-Host 'できました:' (Join-Path $here 'bin')
