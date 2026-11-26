# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Raptor-zip

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Scan-WindowsPackage.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('meltype-scan-test-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $sample = Join-Path $testRoot 'package with spaces.zip'
    Set-Content -LiteralPath $sample -Value 'test fixture' -Encoding ASCII
    $scanner = Join-Path $testRoot 'scanner.cmd'
    # 外部プロセスの終了コードを使う。実際のウイルスや Defender の設定変更は不要。
    Set-Content -LiteralPath $scanner -Value '@exit /b 0' -Encoding ASCII
    Invoke-PackageScan $scanner $sample
    Write-Host 'PASS: 成功した検査を受け入れる'
    foreach ($exitCode in 2, 5) {
        Set-Content -LiteralPath $scanner -Value "@exit /b $exitCode" -Encoding ASCII
        $failure = $null
        try { Invoke-PackageScan $scanner $sample } catch { $failure = $_ }
        if (-not $failure -or $failure.Exception.Message -notlike "*exit code $exitCode*") {
            throw "検出・検査エラー ($exitCode) で公開を止めませんでした。"
        }
        Write-Host "PASS: 検出・検査エラー ($exitCode) で停止する"
    }
    $failure = $null
    try { Invoke-PackageScan $scanner (Join-Path $testRoot 'missing.zip') } catch { $failure = $_ }
    if (-not $failure) { throw '存在しない検査対象を受け入れました。' }
    Write-Host 'PASS: 存在しない検査対象で停止する'
    # リアルタイム保護が検査中に対象を隔離する競合も失敗とする。
    $scanner = Join-Path $testRoot 'remove-sample.ps1'
    Set-Content -LiteralPath $scanner -Encoding ASCII -Value @(
        'param([switch]$Scan, [int]$ScanType, [string]$File, [switch]$DisableRemediation)',
        'Remove-Item -LiteralPath $File',
        '$global:LASTEXITCODE = 0'
    )
    $failure = $null
    try { Invoke-PackageScan $scanner $sample } catch { $failure = $_ }
    if (-not $failure -or $failure.Exception.Message -notlike '*削除・隔離*') {
        throw '検査中に消えた対象を受け入れました。'
    }
    Write-Host 'PASS: 検査中に消えた対象で停止する'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '一時ディレクトリーの範囲外です。' }
    Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
}
