# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 yksr-melt
# PowerShell 7 / Windows 用。テスト署名専用で、本番署名の検証には流用しない。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
if (-not ('Meltype.Signing.Authenticode' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'SignPathAuthenticode.cs')
}

function Get-MeltypePeSigningData([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 64 -or [BitConverter]::ToUInt16($bytes, 0) -ne 0x5A4D) { throw "PE ではありません: $Path" }
    $pe = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($pe -lt 64 -or $pe + 24 + 152 -gt $bytes.Length -or [BitConverter]::ToUInt32($bytes, $pe) -ne 0x4550) {
        throw "PE ヘッダーが不正です: $Path"
    }
    $optional = $pe + 24
    $magic = [BitConverter]::ToUInt16($bytes, $optional)
    $directory = switch ($magic) { 0x10B { $optional + 128 } 0x20B { $optional + 144 } default { throw '未対応の PE 形式です。' } }
    $offset = [BitConverter]::ToUInt32($bytes, $directory)
    $size = [BitConverter]::ToUInt32($bytes, $directory + 4)
    $cms = $null
    if ($size -ne 0) {
        if ($offset -lt $directory + 8 -or $offset % 8 -ne 0 -or [long]$offset + $size -ne $bytes.Length -or $size -lt 8) {
            throw "証明書テーブルが不正です: $Path"
        }
        $length = [BitConverter]::ToUInt32($bytes, $offset)
        if ($length -lt 8 -or $length -gt $size -or [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 0x200 -or [BitConverter]::ToUInt16($bytes, $offset + 6) -ne 2) {
            throw "Authenticode の証明書ではありません: $Path"
        }
        $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
        $cms.Decode([byte[]]$bytes[($offset + 8)..($offset + $length - 1)])
        # 証明書チェーンの信頼と切り離し、CMS 自体の暗号署名を検証する。
        try { $cms.CheckSignature($true) } catch { throw "CMS の暗号署名が不正です: $Path" }
    } elseif ($offset -ne 0) { throw "空の証明書テーブルが不正です: $Path" }
    # 署名で変わる checksum と証明書テーブルへの参照だけを除いて、署名前後の全内容を比べる。
    [Array]::Clear($bytes, $optional + 64, 4)
    [Array]::Clear($bytes, $directory, 8)
    $end = if ($size) { [int]$offset } else { $bytes.Length }
    $content = [byte[]]::new([int]([Math]::Ceiling($end / 8.0) * 8))
    [Array]::Copy($bytes, $content, $end)
    [pscustomobject]@{ Cms = $cms; ContentHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($content)) }
}

function Get-MeltypeArtifactFiles([string]$Directory) {
    $root = (Resolve-Path -LiteralPath $Directory).Path
    $result = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '成果物にリンクは含められません。' }
        if (-not $item.PSIsContainer) { $result[[IO.Path]::GetRelativePath($root, $item.FullName).Replace('\', '/')] = $item.FullName }
    }
    if ($result.Count -eq 0) { throw '成果物が空です。' }
    return $result
}

function Test-MeltypeSignPathArtifact {
    param(
        [Parameter(Mandatory)][string]$UnsignedDirectory,
        [Parameter(Mandatory)][string]$SignedDirectory,
        [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{64}$')][string]$ExpectedCertificateSha256,
        [Parameter(Mandatory)][string]$ReportPath
    )
    $targets = @('Meltype.exe', 'Meltype.dll', 'Meltype.Core.dll')
    $report = [ordered]@{ purpose = 'test-signing only; NOT FOR RELEASE'; verified = $false; files = @(); error = $null }
    try {
        $before = Get-MeltypeArtifactFiles $UnsignedDirectory
        $after = Get-MeltypeArtifactFiles $SignedDirectory
        if (Compare-Object @($before.Keys) @($after.Keys) -CaseSensitive) { throw '署名前後でファイルの一覧が違います。' }
        foreach ($target in $targets) {
            if (-not $before.ContainsKey($target)) { throw "必須の署名対象がありません: $target" }
        }
        foreach ($name in @($before.Keys | Sort-Object)) {
            $unsignedHash = (Get-FileHash -LiteralPath $before[$name] -Algorithm SHA256).Hash
            $signedHash = (Get-FileHash -LiteralPath $after[$name] -Algorithm SHA256).Hash
            if ($targets -cnotcontains $name) {
                if ($unsignedHash -cne $signedHash) { throw "署名対象外のファイルが変わっています: $name" }
                continue
            }
            $original = Get-MeltypePeSigningData $before[$name]
            $signed = Get-MeltypePeSigningData $after[$name]
            if ($original.Cms -or -not $signed.Cms -or $original.ContentHash -cne $signed.ContentHash) {
                throw "署名がないか、署名以外の内容も変わっています: $name"
            }
            if ($signed.Cms.SignerInfos.Count -ne 1) { throw "署名者の数が不正です: $name" }
            if ($signed.Cms.SignerInfos[0].DigestAlgorithm.Value -ne '2.16.840.1.101.3.4.2.1') { throw 'SHA-256 の署名ではありません。' }
            $certificate = $signed.Cms.SignerInfos[0].Certificate
            $fingerprint = $certificate.GetCertHashString([Security.Cryptography.HashAlgorithmName]::SHA256)
            if ($fingerprint -ine $ExpectedCertificateSha256 -or $certificate.Subject -cne "CN=Test certificate for 'Meltype [OSS]'") {
                throw "設定したテスト証明書と署名者が違います: $name"
            }
            $now = [DateTime]::UtcNow
            if ($now -lt $certificate.NotBefore.ToUniversalTime() -or $now -gt $certificate.NotAfter.ToUniversalTime()) { throw 'テスト証明書が有効期間外です。' }
            $eku = @($certificate.Extensions | Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | ForEach-Object { $_.Value })
            if ($eku -notcontains '1.3.6.1.5.5.7.3.3') { throw 'コード署名用の証明書ではありません。' }
            $hashStatus = [Meltype.Signing.Authenticode]::VerifyFileHash($after[$name])
            if ($hashStatus -ne 0) { throw ('Authenticode のハッシュ検証に失敗しました: {0} (0x{1:X8})' -f $name, $hashStatus) }
            $signature = Get-AuthenticodeSignature -LiteralPath $after[$name]
            $chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
            try {
                # テスト証明書のチェーンの結果を記録。信頼ストアへの追加・失効検証の本番用代替はしない。
                $chain.ChainPolicy.RevocationMode = [Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
                $chain.ChainPolicy.ExtraStore.AddRange($signed.Cms.Certificates)
                $chainTrusted = $chain.Build($certificate)
                $chainErrors = @($chain.ChainStatus | ForEach-Object { $_.Status.ToString() })
            } finally { $chain.Dispose() }
            if (@($chainErrors | Where-Object { $_ -notin 'UntrustedRoot', 'PartialChain' }).Count -gt 0) { throw "信頼されないこと以外の証明書エラーがあります: $name ($chainErrors)" }
            if ($signature.Status.ToString() -notin 'Valid', 'NotTrusted', 'UnknownError') { throw "Windows の署名検証に失敗しました: $name ($($signature.Status))" }
            $report.files += [ordered]@{
                path = $name; unsignedSha256 = $unsignedHash; signedSha256 = $signedHash
                certificateSha256 = $fingerprint; subject = $certificate.Subject
                cmsSignatureValid = $true; authenticodeHashValid = $true; originalContentPreserved = $true
                windowsStatus = $signature.Status.ToString(); windowsStatusMessage = $signature.StatusMessage
                chainTrusted = $chainTrusted; chainStatus = $chainErrors; revocationMode = 'NoCheck (test only)'
            }
        }
        $report.verified = $true
        Write-Host 'PASS: 3 ファイルのテスト署名・証明書・改変のないことを確認しました。配布用ではありません。'
        $report.files | ForEach-Object { Write-Host "$($_.path): Windows=$($_.windowsStatus), chainTrusted=$($_.chainTrusted)" }
    } catch {
        $report.error = $_.Exception.Message
        throw
    } finally {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([IO.Path]::GetFullPath($ReportPath))) | Out-Null
        $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
        if ($env:GITHUB_STEP_SUMMARY) {
            @('## SignPath テスト署名', '', "検証成功: $($report.verified)。**正式配布には使いません。**", '', '署名の暗号検証・証明書の照合と、Windows の信頼チェーンの結果は report.json に別々に記録します。') >> $env:GITHUB_STEP_SUMMARY
        }
    }
}
