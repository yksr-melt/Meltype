# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 yksr-melt
# PowerShell 7 / Windows 用。SignPath で署名した成果物を確かめる。
# テスト署名では、SHA-256 で固定した自己署名証明書を実行中だけ CurrentUser Root に登録し、必ず削除する。
# 本番署名では信頼ストアを変更しない。どちらも Windows の検証結果 Valid のみを受け入れる。

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
        [switch]$TrustPinnedTestCertificate
    )
    $report = [ordered]@{ trustPinnedTestCertificate = [bool]$TrustPinnedTestCertificate; verified = $false; files = @(); error = $null }
    $temporaryStore = $null
    $temporaryCertificate = $null
    try {
        $before = Get-MeltypeArtifactFiles $UnsignedDirectory
        $after = Get-MeltypeArtifactFiles $SignedDirectory
        if (Compare-Object @($before.Keys) @($after.Keys) -CaseSensitive) { throw '署名前後でファイルの一覧が違います。' }
        foreach ($target in $Targets) {
            if (-not $before.ContainsKey($target)) { throw "署名対象がありません: $target" }
        }
        if ($TrustPinnedTestCertificate) {
            # 署名者の公開証明書をピン留めしてから、一時的に CurrentUser Root に登録する。
            $candidate = (Get-AuthenticodeSignature -LiteralPath $after[$Targets[0]]).SignerCertificate
            if ($null -eq $candidate) { throw 'テスト署名の証明書が見つかりません。' }
            if ($candidate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256) -ine $ExpectedCertificateSha256) {
                throw 'テスト署名の証明書 SHA-256 が設定値と一致しません。'
            }
            if ($candidate.Subject -cne $candidate.Issuer) { throw '一時信頼は自己署名テスト証明書にのみ対応します。' }
            $temporaryStore = [Security.Cryptography.X509Certificates.X509Store]::new(
                [Security.Cryptography.X509Certificates.StoreName]::Root,
                [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
            $temporaryStore.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
            $existing = $temporaryStore.Certificates.Find(
                [Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
                $candidate.Thumbprint, $false)
            if ($existing.Count -ne 0) { throw 'テスト証明書が既に CurrentUser Root に存在します。' }
            $temporaryStore.Add($candidate)
            $temporaryCertificate = $candidate
        }
        foreach ($name in @($before.Keys | Sort-Object)) {
            $unsignedHash = (Get-FileHash -LiteralPath $before[$name] -Algorithm SHA256).Hash
            $signedHash = (Get-FileHash -LiteralPath $after[$name] -Algorithm SHA256).Hash
            if ($Targets -cnotcontains $name) {
                if ($unsignedHash -cne $signedHash) { throw "署名対象外のファイルが変わっています: $name" }
                continue
            }
            if ((Get-AuthenticodeSignature -LiteralPath $before[$name]).Status -ne 'NotSigned') { throw "署名前のファイルに署名があります: $name" }
            # Windows の検証が Valid 以外なら、原因を問わず拒否する。
            $signature = Get-AuthenticodeSignature -LiteralPath $after[$name]
            if ($signature.Status.ToString() -ne 'Valid') { throw "署名の検証に失敗しました: $name ($($signature.Status): $($signature.StatusMessage))" }
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
        if ($null -ne $temporaryStore) {
            try { if ($null -ne $temporaryCertificate) { $temporaryStore.Remove($temporaryCertificate) } }
            finally { $temporaryStore.Close() }
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))) | Out-Null
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
    }
}
