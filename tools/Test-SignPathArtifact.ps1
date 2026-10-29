# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 yksr-melt
# 実際の未信頼テスト証明書と PE を使う。SignPath の秘密や信頼ストアの変更は不要。

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Verify-SignPathArtifact.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('meltype-signpath-test-' + [guid]::NewGuid())
$unsigned = Join-Path $testRoot 'unsigned'
$signed = Join-Path $testRoot 'signed'
$certificate = $null
New-Item -ItemType Directory -Path $unsigned, $signed | Out-Null
try {
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Test certificate for 'Meltype [OSS]'" -CertStoreLocation Cert:\CurrentUser\My -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(1)
    $fingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
    $fixture = Join-Path $unsigned 'Meltype.dll'
    Add-Type -TypeDefinition @'
using System.Reflection;
[assembly: AssemblyProduct("Meltype")]
[assembly: AssemblyInformationalVersion("0.0.0-test")]
public class SignPathFixture { public static string Value() { return "Meltype signature fixture"; } }
'@ -OutputAssembly $fixture -OutputType Library
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $unsigned 'Meltype.exe')
    Copy-Item -LiteralPath $fixture -Destination (Join-Path $unsigned 'Meltype.Core.dll')
    Set-Content -LiteralPath (Join-Path $unsigned 'LICENSE.txt') -Value 'fixture license'
    Copy-Item -Path (Join-Path $unsigned '*') -Destination $signed
    foreach ($name in 'Meltype.exe', 'Meltype.dll', 'Meltype.Core.dll') {
        Set-AuthenticodeSignature -LiteralPath (Join-Path $signed $name) -Certificate $certificate -HashAlgorithm SHA256 | Out-Null
    }
    $report = Join-Path $testRoot 'report.json'
    Test-MeltypeSignPathArtifact -UnsignedDirectory $unsigned -SignedDirectory $signed -ExpectedCertificateSha256 $fingerprint -ReportPath $report
    $result = Get-Content -Raw -LiteralPath $report | ConvertFrom-Json
    if (-not $result.verified -or @($result.files).Count -ne 3 -or @($result.files | Where-Object { $_.chainTrusted }).Count -ne 0) {
        throw '未信頼のテスト証明書を、暗号署名の成功と分けて記録できませんでした。'
    }
    Write-Host 'PASS: 未信頼の署名の暗号検証に成功し、信頼されない結果を残す'

    function Assert-Rejected([string]$Name, [scriptblock]$Change, [string]$ExpectedError, [string]$Pin = $fingerprint) {
        $case = Join-Path $testRoot ('case-' + [guid]::NewGuid())
        New-Item -ItemType Directory -Path $case | Out-Null
        Copy-Item -Path (Join-Path $signed '*') -Destination $case
        & $Change $case
        $failure = $null
        $caseReport = Join-Path $testRoot 'failure.json'
        try { Test-MeltypeSignPathArtifact -UnsignedDirectory $unsigned -SignedDirectory $case -ExpectedCertificateSha256 $Pin -ReportPath $caseReport } catch { $failure = $_ }
        if (-not $failure -or $failure.Exception.Message -notlike "*$ExpectedError*") { throw "拒否できませんでした: $Name ($failure)" }
        $failedResult = Get-Content -Raw -LiteralPath $caseReport | ConvertFrom-Json
        if ($failedResult.verified -or -not $failedResult.error) { throw "失敗の記録が不正です: $Name" }
        Write-Host "PASS: $Name を拒否する"
    }
    Assert-Rejected '未署名の PE' { param($case) Copy-Item (Join-Path $unsigned 'Meltype.exe') (Join-Path $case 'Meltype.exe') -Force } '署名がない'
    Assert-Rejected '違う証明書のピン' {} 'テスト証明書と署名者が違います' ('0' * 64)
    Assert-Rejected '署名対象外の変更' { param($case) Add-Content (Join-Path $case 'LICENSE.txt') 'changed' } '署名対象外'
    Assert-Rejected 'ファイルの追加' { param($case) Set-Content (Join-Path $case 'extra.txt') 'extra' } 'ファイルの一覧'
    Assert-Rejected 'ファイルの欠落' { param($case) Remove-Item (Join-Path $case 'Meltype.dll') } 'ファイルの一覧'
    Assert-Rejected '署名後のコードの改変' {
        param($case)
        $path = Join-Path $case 'Meltype.exe'
        $bytes = [IO.File]::ReadAllBytes($path)
        $bytes[512] = $bytes[512] -bxor 1
        [IO.File]::WriteAllBytes($path, $bytes)
    } '署名以外の内容'
    Assert-Rejected 'CMS の暗号署名の改変' {
        param($case)
        $path = Join-Path $case 'Meltype.exe'
        $bytes = [IO.File]::ReadAllBytes($path)
        $signatureBytes = (Get-MeltypePeSigningData $path).Cms.SignerInfos[0].GetSignature()
        $needle = [Convert]::ToHexString($signatureBytes)
        $hex = [Convert]::ToHexString($bytes)
        $index = $hex.IndexOf($needle, [StringComparison]::Ordinal)
        if ($index -lt 0) { throw 'テスト用の暗号署名が見つかりません。' }
        $bytes[[int]($index / 2)] = $bytes[[int]($index / 2)] -bxor 1
        [IO.File]::WriteAllBytes($path, $bytes)
    } 'CMS の暗号署名'

    # CMS は正しいまま、署名対象のバイトを変える。信頼されない証明書でもハッシュの不一致を検出する。
    $tampered = Join-Path $testRoot 'tampered.dll'
    Copy-Item (Join-Path $signed 'Meltype.dll') $tampered
    $bytes = [IO.File]::ReadAllBytes($tampered)
    $bytes[512] = $bytes[512] -bxor 1
    [IO.File]::WriteAllBytes($tampered, $bytes)
    if ([Meltype.Signing.Authenticode]::VerifyFileHash($tampered) -eq 0) { throw 'WinVerifyTrust が PE の改変を見逃しました。' }
    Write-Host 'PASS: 未信頼の署名でも PE のハッシュの不一致を検出する'
} finally {
    if ($certificate) { Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -DeleteKey }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
