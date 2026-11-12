# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 MuNeNICK

# setup-ime.ps1 が、meltype-ime.ps1 の結果を終了コードで返すか (setup.exe は、登録済みの Meltype IME を更新できなければ本体を入れ替えない)。
# meltype-ime.ps1 は、引数と結果を確かめるダミーにする。

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('meltype-setup-test-' + [guid]::NewGuid())
$fixture = Join-Path $testRoot "app with spaces and 'quotes'"
$originalTemp = $env:TEMP
$powershell = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe'
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\packaging\setup-ime.ps1') -Destination $fixture
    $env:TEMP = $testRoot
    $helper = Join-Path $fixture 'meltype-ime.ps1'
    $log = Join-Path $testRoot 'Meltype-setup-ime.log'
    $source = Join-Path $fixture 'tip'
    foreach ($case in @(
        @{ Explain = $false; Result = '$true'; Code = 0 },
        @{ Explain = $true; Result = '$true'; Code = 0 },
        @{ Explain = $false; Result = '$false'; Code = 1 }
    )) {
        $stub = @'
function Install-MeltypeIme([string]$Source, [switch]$Explain) {
    if ($Source -ne SOURCE) { throw "Incorrect source directory: $Source" }
    if ([bool]$Explain -ne EXPLAIN) { throw 'Incorrect -Explain value' }
    Write-Host 'Registration attempted'
    return RESULT
}
function Uninstall-MeltypeIme { throw 'Uninstall-MeltypeIme called during install' }
'@
        $explain = if ($case.Explain) { '$true' } else { '$false' }
        Set-Content -LiteralPath $helper -Value $stub.Replace('SOURCE', "'" + $source.Replace("'", "''") + "'").Replace('EXPLAIN', $explain).Replace('RESULT', $case.Result) -Encoding UTF8
        $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $fixture 'setup-ime.ps1'), '-Source', $source)
        if ($case.Explain) { $arguments += '-Explain' }
        & $powershell @arguments
        if ($LASTEXITCODE -ne $case.Code) { throw "Unexpected exit code $LASTEXITCODE" }
        if ((Get-Content -LiteralPath $log -Raw) -notmatch 'Registration attempted') { throw 'Registration output was not logged' }
        Write-Host "PASS: Explain=$($case.Explain), Result=$($case.Result)"
    }
    foreach ($case in @(@{ Result = '$true'; Code = 0 }, @{ Result = '$false'; Code = 1 })) {
        $stub = @'
function Install-MeltypeIme { throw 'Install-MeltypeIme called during uninstall' }
function Uninstall-MeltypeIme {
    Write-Host 'Unregistration attempted'
    return RESULT
}
'@
        Set-Content -LiteralPath $helper -Value $stub.Replace('RESULT', $case.Result) -Encoding UTF8
        & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'setup-ime.ps1') -Uninstall
        if ($LASTEXITCODE -ne $case.Code) { throw "Unexpected exit code $LASTEXITCODE" }
        if ((Get-Content -LiteralPath $log -Raw) -notmatch 'Unregistration attempted') { throw 'Unregistration output was not logged' }
        Write-Host "PASS: Uninstall, Result=$($case.Result)"
    }
    Set-Content -LiteralPath $helper -Value "throw 'Registration fixture failure'" -Encoding UTF8
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'setup-ime.ps1') -Source $source
    if ($LASTEXITCODE -ne 1 -or (Get-Content -LiteralPath $log -Raw) -notmatch 'Registration fixture failure') {
        throw 'Unexpected registration error was not reported'
    }
    Write-Host 'PASS: unexpected registration error is logged with exit code 1'
    Remove-Item -LiteralPath $helper
    & $powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $fixture 'setup-ime.ps1') -Source $source
    if ($LASTEXITCODE -ne 1) { throw 'Missing registration helper was not reported' }
    Write-Host 'PASS: missing registration helper is reported'
    # The last child intentionally failed; do not propagate its exit code to CI.
    $global:LASTEXITCODE = 0
}
finally {
    $env:TEMP = $originalTemp
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedRoot) -notlike 'meltype-setup-test-*') {
        throw 'Refusing to remove an unexpected test directory'
    }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
