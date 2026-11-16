# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# Meltype の更新を確認して、新しい版があればダウンロードして展開しておく (Meltype が定期的に呼ぶ)。
#   update.ps1 -Repository yksr-melt/Meltype -CurrentVersion 0.1.2 -Directory %LOCALAPPDATA%\Meltype\update [-Prerelease]
# -Prerelease を付けると、プレリリース版 (v1.3.1-pre.4 など) も含めて一番新しい版を見る (付けなければ正式版の最新だけ)。
# GitHub の最新のリリースにインストーラー (Meltype-<版>-setup.exe) があり、版 (タグ v0.1.3) が今の版より新しければ、
# <Directory> にダウンロードし、<Directory>\ready.txt に「版 TAB インストーラーの場所」を書く。インストールは Meltype がインストーラーを /VERYSILENT で実行して行う。
# 結果は標準出力に 1 行で返す: none (更新なし) / ready <版> / error <理由>

param(
    [Parameter(Mandatory)] [string]$Repository,
    [Parameter(Mandatory)] [string]$CurrentVersion,
    [Parameter(Mandatory)] [string]$Directory,
    [switch]$Prerelease
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# 結果を UTF-8 で返す (Meltype が UTF-8 として読む)
[Console]::OutputEncoding = [Text.Encoding]::UTF8

# 版 (1.3.1・1.3.1-pre.3) を比べる (Semantic Versioning の順。プレリリースは同じ数字の正式版より前)。
# Left が新しければ 1、同じなら 0、古ければ -1。Meltype.Core の ProjectInfo.CompareVersions と同じ順。
function Compare-MeltypeVersion([string]$Left, [string]$Right) {
    $l = ($Left -split '\+')[0] -split '-', 2
    $r = ($Right -split '\+')[0] -split '-', 2
    $core = ([version]$l[0]).CompareTo([version]$r[0])
    if ($core -ne 0 -or ($l.Count -eq 1 -and $r.Count -eq 1)) { return [Math]::Sign($core) }
    if ($l.Count -eq 1) { return 1 }
    if ($r.Count -eq 1) { return -1 }
    $p = $l[1] -split '\.'
    $q = $r[1] -split '\.'
    for ($i = 0; $i -lt [Math]::Min($p.Count, $q.Count); $i++) {
        $pn = $p[$i] -match '^\d+$'
        $qn = $q[$i] -match '^\d+$'
        if ($pn -and $qn) { $c = ([long]$p[$i]).CompareTo([long]$q[$i]) }
        elseif ($pn) { $c = -1 }
        elseif ($qn) { $c = 1 }
        else { $c = [string]::CompareOrdinal($p[$i], $q[$i]) }
        if ($c -ne 0) { return [Math]::Sign($c) }
    }
    return [Math]::Sign($p.Count - $q.Count)
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'Meltype-Updater'; 'Accept' = 'application/vnd.github+json' }
    if ($Prerelease) {
        # releases/latest はプレリリースを返さないので、最近のリリースから一番新しい版を選ぶ (下書きと、版として読めないタグは除く)
        $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=30" -Headers $headers -TimeoutSec 30
        $release = $null
        foreach ($candidate in $releases) {
            if ($candidate.draft -or $candidate.tag_name -notmatch '^v?\d+(\.\d+){1,3}(-[0-9A-Za-z.]+)?$') { continue }
            if (-not $release -or (Compare-MeltypeVersion ($candidate.tag_name -replace '^v', '') ($release.tag_name -replace '^v', '')) -gt 0) { $release = $candidate }
        }
        if (-not $release) { Write-Output 'none'; exit 0 }
    }
    else {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers $headers -TimeoutSec 30
    }
    $latest = $release.tag_name -replace '^v', ''
    if ((Compare-MeltypeVersion $latest $CurrentVersion) -le 0) { Write-Output 'none'; exit 0 }

    $asset = $release.assets | Where-Object { $_.name -like 'Meltype-*-setup.exe' } | Select-Object -First 1
    if (-not $asset) { Write-Output "error リリース $latest にインストーラーがありません"; exit 0 }

    $setup = Join-Path $Directory $asset.name
    $ready = Join-Path $Directory 'ready.txt'
    # 1.6.0 以前の Meltype が書いた ready.txt (2 列目が zip を展開したフォルダー) は、同じ版でもダウンロードし直す
    if ((Test-Path -LiteralPath $ready) -and ((Get-Content -LiteralPath $ready -Raw).Trim() -eq "$latest`t$setup") -and (Test-Path -LiteralPath $setup -PathType Leaf)) { Write-Output "ready $latest"; exit 0 }

    New-Item -ItemType Directory -Force -Path $Directory | Out-Null
    $download = "$setup.download"
    Invoke-WebRequest -Uri $asset.browser_download_url -Headers @{ 'User-Agent' = 'Meltype-Updater' } -OutFile $download -UseBasicParsing -TimeoutSec 600

    # GitHub が公開している SHA-256 と同じか (ダウンロードの途中で壊れていないか) を確かめる。
    if ($asset.digest -like 'sha256:*') {
        $expected = $asset.digest.Substring(7).ToLowerInvariant()
        $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $expected) { Remove-Item -LiteralPath $download -Force; Write-Output 'error ダウンロードしたインストーラーのハッシュが違います'; exit 0 }
    }
    Move-Item -LiteralPath $download -Destination $setup -Force
    # インターネットから落とした印 (Zone.Identifier) を外す (実行したときの SmartScreen の確認を出さない)
    Unblock-File -LiteralPath $setup -ErrorAction SilentlyContinue

    # 前の版のインストーラーと、1.6.0 以前の Meltype が展開した zip を片付ける
    Get-ChildItem -LiteralPath $Directory | Where-Object { $_.FullName -ne $setup -and $_.Name -notin 'ready.txt', 'applying.txt', 'install.log' } | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    [IO.File]::WriteAllText($ready, "$latest`t$setup", (New-Object Text.UTF8Encoding $false))
    Write-Output "ready $latest"
}
catch {
    Write-Output "error $($_.Exception.Message)"
}
