# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 cet-t
# Build only. The default is x64 (bin/tests/InputEvents.exe).
# Use -Architecture x86 for bin/tests/x86/InputEvents.exe.
param([ValidateSet('x64', 'x86')][string]$Architecture = 'x64')
$ErrorActionPreference = 'Stop'
$nativeDir = Split-Path $PSScriptRoot -Parent
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio C++ build tools were not found.' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
# vcvarsall.bat looks up vswhere.exe on PATH.
$env:PATH = (Split-Path $vswhere) + ';' + $env:PATH
$testOut = Join-Path $nativeDir 'bin\tests'
$testObj = Join-Path $nativeDir 'obj\tests'
if ($Architecture -eq 'x86') {
    $testOut = Join-Path $testOut 'x86'
    $testObj = Join-Path $testObj 'x86'
}
New-Item -ItemType Directory -Force -Path $testOut, $testObj | Out-Null
$sources = 'tests\InputEvents.cpp TextService.cpp CandidateWindow.cpp DisplayAttributes.cpp LangBar.cpp Json.cpp'
$command = @(
    "call `"$vcvars`" $Architecture >nul",
    "cd /d `"$nativeDir`"",
    "cl /nologo /std:c++17 /EHsc /W4 /wd4100 /utf-8 /DUNICODE /D_UNICODE /I. /Fo`"$testObj\\`" $sources /Fe:`"$testOut\InputEvents.exe`" /link ole32.lib oleaut32.lib uuid.lib user32.lib gdi32.lib advapi32.lib d2d1.lib dwrite.lib dwmapi.lib"
) -join ' && '
cmd /d /c $command
if ($LASTEXITCODE -ne 0) { throw 'Input event test build failed.' }
