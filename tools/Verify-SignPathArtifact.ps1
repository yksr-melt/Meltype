# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 yksr-melt
# PowerShell 7 / Windows 用。SignPath で署名した成果物を確かめる。
# テスト署名では -AllowUntrustedRoot を付ける (テスト証明書は信頼されないので、Windows の検証は UnknownError になる)。
# 本番署名では付けずに使い、Windows の検証が Valid であることを必須にする。

$ErrorActionPreference = 'Stop'

function Get-MeltypeArtifactFiles([string]$Directory) {
    $root = (Resolve-Path -LiteralPath $Directory).Path
    $result = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
        $result[[IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')] = $item.FullName
    }
    if ($result.Count -eq 0) { throw '成果物が空です。' }
    return $result
}

function Test-MeltypeSignPathArtifact {
    param(
        [Parameter(Mandatory)][string]$UnsignedDirectory,
        [Parameter(Mandatory)][string]$SignedDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedCertificateSha256,
        [Parameter(Mandatory)][string]$ReportPath,
        [string[]]$Targets = @('Meltype.exe', 'Meltype.dll', 'Meltype.Core.dll'),
        [switch]$AllowUntrustedRoot
    )
    $report = [ordered]@{ allowUntrustedRoot = [bool]$AllowUntrustedRoot; verified = $false; files = @(); error = $null }
    try {
        $before = Get-MeltypeArtifactFiles $UnsignedDirectory
        $after = Get-MeltypeArtifactFiles $SignedDirectory
        if (Compare-Object @($before.Keys) @($after.Keys) -CaseSensitive) { throw '署名前後でファイルの一覧が違います。' }
        foreach ($target in $Targets) {
            if (-not $before.ContainsKey($target)) { throw "署名対象がありません: $target" }
        }
        foreach ($name in @($before.Keys | Sort-Object)) {
            $unsignedHash = (Get-FileHash -LiteralPath $before[$name] -Algorithm SHA256).Hash
            $signedHash = (Get-FileHash -LiteralPath $after[$name] -Algorithm SHA256).Hash
            if ($Targets -cnotcontains $name) {
                if ($unsignedHash -cne $signedHash) { throw "署名対象外のファイルが変わっています: $name" }
                continue
            }
            if ((Get-AuthenticodeSignature -LiteralPath $before[$name]).Status -ne 'NotSigned') { throw "署名前のファイルに署名があります: $name" }
            # 署名後に中身が変わっていれば HashMismatch になる (信頼されない証明書でも、ハッシュの検証が先)。
            $signature = Get-AuthenticodeSignature -LiteralPath $after[$name]
            $allowed = if ($AllowUntrustedRoot) { 'Valid', 'UnknownError', 'NotTrusted' } else { 'Valid' }
            if ($signature.Status.ToString() -notin $allowed) { throw "署名の検証に失敗しました: $name ($($signature.Status): $($signature.StatusMessage))" }
            $certificate = $signature.SignerCertificate
            $fingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
            if ($fingerprint -ine $ExpectedCertificateSha256) { throw "設定した証明書と署名者が違います: $name" }
            $report.files += [ordered]@{
                path = $name; unsignedSha256 = $unsignedHash; signedSha256 = $signedHash
                certificateSha256 = $fingerprint; subject = $certificate.Subject
                windowsStatus = $signature.Status.ToString(); windowsStatusMessage = $signature.StatusMessage
            }
        }
        $report.verified = $true
        $report.files | ForEach-Object { Write-Host "$($_.path): $($_.windowsStatus)" }
    }
    catch {
        $report.error = $_.Exception.Message
        throw
    }
    finally {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))) | Out-Null
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    }
}
