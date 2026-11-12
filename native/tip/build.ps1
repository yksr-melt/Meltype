# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 lnkiai
#
# Meltype IME (TSF の DLL) をビルドする。Visual Studio Build Tools (C++) が要る。
#   powershell -ExecutionPolicy Bypass -File native\tip\build.ps1 [-Configuration Release|Debug]
# 出力: native\tip\bin\x64 と native\tip\bin\x86 (32bit のアプリ用) に、MeltypeTip.dll (stub) と MeltypeTipImpl.dll (impl)

param([ValidateSet('Release', 'Debug')][string]$Configuration = 'Release')

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio (Build Tools) の C++ ビルドツールが見つかりません。Meltype IME をビルドするには、Visual Studio Build Tools の「C++ によるデスクトップ開発」を入れてください。' }
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
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

foreach ($arch in 'x64', 'x86') {
    $out = Join-Path $here "bin\$arch"
    $obj = Join-Path $here "obj\$arch"
    New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null
    $vcarch = if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' }
    $cmd = @(
        "`"$vcvars`" $vcarch >nul",
        "cd /d `"$here`"",
        "rc /nologo /fo `"$obj\MeltypeTip.res`" MeltypeTip.rc"
    ) + @($dlls | ForEach-Object {
        $name = $_.Name
        "cl /nologo /Brepro /std:c++17 /EHsc /W4 /wd4100 /utf-8 /DUNICODE /D_UNICODE $optimize /Fo`"$obj\\`" /Fd`"$obj\\`" /LD $($_.Sources -join ' ') `"$obj\MeltypeTip.res`" /link /Brepro /DEF:$name.def /IMPLIB:`"$obj\$name.lib`" /OUT:`"$out\$name.dll`" $libs"
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
