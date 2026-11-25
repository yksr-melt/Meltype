# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Raptor-zip

# 完成したフォルダーと ZIP を検査し、検出・検査不能なら公開を止める。
param([string[]]$Path)
$ErrorActionPreference = 'Stop'

function Invoke-PackageScan([string]$Scanner, [string]$ScanPath) {
    $resolved = (Resolve-Path -LiteralPath $ScanPath -ErrorAction Stop).ProviderPath
    # 通常の検査は「検出して駆除した」場合も 0 を返すため、駆除なしで検査する。
    # このオプションでは ZIP も検査し、対象ファイルの除外設定も無視する。
    # https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus
    & $Scanner -Scan -ScanType 3 -File $resolved -DisableRemediation | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Defender が脅威を検出したか、検査に失敗しました (exit code $LASTEXITCODE): $resolved。公開せず、検出内容を確認してください。"
    }
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "検査中にファイルが削除・隔離された可能性があります: $resolved"
    }
}

# テストから検査処理だけを読み込む。
if ($MyInvocation.InvocationName -eq '.') { return }
if (-not $Path) { throw '検査するフォルダーと ZIP を -Path で指定してください。' }
$status = Get-MpComputerStatus -ErrorAction Stop
if (-not $status.AMServiceEnabled -or -not $status.AntivirusEnabled -or $status.AMRunningMode -ne 'Normal') {
    throw 'Microsoft Defender Antivirus が有効ではないため、配布物を検査できません。'
}

# 更新済みプラットフォームを優先し、無ければ Windows 同梱のものを使う。
$platform = Join-Path $env:ProgramData 'Microsoft\Windows Defender\Platform'
$scanner = Get-ChildItem -LiteralPath $platform -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'MpCmdRun.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $scanner) { $scanner = Join-Path $env:ProgramFiles 'Windows Defender\MpCmdRun.exe' }
if (-not (Test-Path -LiteralPath $scanner)) { throw 'MpCmdRun.exe が見つかりません。配布物を検査できません。' }
& $scanner -SignatureUpdate | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Defender の定義の更新に失敗しました (exit code $LASTEXITCODE)。" }
$status = Get-MpComputerStatus -ErrorAction Stop
Write-Host "Defender 定義: $($status.AntivirusSignatureVersion) / エンジン: $($status.AMEngineVersion)"
foreach ($item in $Path) { Invoke-PackageScan $scanner $item }
Write-Host '配布物の Defender 検査が完了しました (この判定時点での結果です)。'
