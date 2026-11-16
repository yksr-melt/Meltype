# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 penta2himajin
# Copyright (C) 2026 aster-void

# インストーラー (packaging/Meltype.iss) を、ダミーの Meltype.exe で作り、インストール → 自動更新 → 1.6.0 以前の自動更新 (互換の zip の install.ps1)
# → アンインストールの順に通す。場面ごとに前の場面の状態を引き継ぐ。Meltype IME (管理者権限が要る) は入れない。
# 実際の %LOCALAPPDATA%\Programs\Meltype を入れ替えるので、CI の使い捨ての環境でしか動かさない。

$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_ACTIONS) { throw 'このテストはインストール先を入れ替えるので、GitHub Actions の中でしか動かしません。' }

$repo = Split-Path -Parent $PSScriptRoot
$powershell = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe'
$iscc = & (Join-Path $repo 'packaging\Find-InnoSetup.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('meltype-install-test-' + [guid]::NewGuid())
$target = Join-Path $env:LOCALAPPDATA 'Programs\Meltype'
$exe = Join-Path $target 'Meltype.exe'
$config = Join-Path $env:LOCALAPPDATA 'Meltype\config.json'
$log = Join-Path $testRoot 'install.log'
$innoKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype_is1'
$zipKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype'
$shortcuts = 'Startup', 'Programs' | ForEach-Object { Join-Path ([Environment]::GetFolderPath($_)) 'Meltype.lnk' }

# 引数があれば (--exit) すぐ終わり、無ければ本物の Meltype のように動き続ける。
$dummy = @'
using System.Reflection;
using System.Threading;
[assembly: AssemblyInformationalVersion("VERSION")]
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0) Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}
'@

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "失敗: $Message" }
}

# Build-Package.ps1 と同じ形の dist\Meltype を作り、インストーラーにする
function New-Setup([string]$Version) {
    $stage = Join-Path $testRoot "stage-$Version"
    New-Item -ItemType Directory -Path (Join-Path $stage 'app') -Force | Out-Null
    Add-Type -TypeDefinition $dummy.Replace('VERSION', $Version) -OutputAssembly (Join-Path $stage 'app\Meltype.exe') -OutputType WindowsApplication
    Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $repo 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')
    & $iscc /Q "/DAppVersion=$Version" "/DSourceDir=$stage" "/DOutputDir=$testRoot" (Join-Path $repo 'packaging\Meltype.iss')
    if ($LASTEXITCODE -ne 0) { throw "インストーラーを作れませんでした (exit code $LASTEXITCODE)。" }
    return Join-Path $testRoot "Meltype-$Version-setup.exe"
}

# 起動された Meltype は残り続ける。Start-Process -Wait は子孫のプロセスも待つので使わない。
function Invoke-Wait([string]$FilePath, [string[]]$Arguments) {
    $process = Start-Process -FilePath $FilePath -ArgumentList $Arguments -WorkingDirectory $env:TEMP -PassThru
    $null = $process.Handle # 取っておかないと、終了後に ExitCode を読めない
    if (-not $process.WaitForExit(180000)) {
        Stop-Process -Id $process.Id -Force
        throw "$FilePath が 3 分で終わりませんでした。"
    }
    return $process.ExitCode
}

# Updater.Apply と同じ引数
function Invoke-Setup([string]$Setup) { Invoke-Wait $Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`"") }

function Wait-Meltype {
    for ($i = 0; $i -lt 300; $i++) {
        $process = Get-Process Meltype -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
        if ($process) { return $process }
        Start-Sleep -Milliseconds 100
    }
    throw "Meltype が起動しませんでした: $exe"
}

function Get-InstalledVersion { (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion }

New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $code = Invoke-Setup (New-Setup '1.0.0')
    Assert-True ($code -eq 0) "新規インストールの終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '1.0.0') 'Meltype.exe が入っていない'
    $running = Wait-Meltype
    foreach ($shortcut in $shortcuts) { Assert-True (Test-Path -LiteralPath $shortcut) "ショートカットが無い: $shortcut" }
    Assert-True ((Get-ItemProperty -LiteralPath $innoKey).DisplayVersion -eq '1.0.0') '「設定 → アプリ」の版が違う'
    Write-Host 'PASS: 新規インストール'

    New-Item -ItemType Directory -Path (Split-Path $config) -Force | Out-Null
    Set-Content -LiteralPath $config -Value '{}'
    $code = Invoke-Setup (New-Setup '2.0.0')
    Assert-True ($code -eq 0) "更新の終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '2.0.0') '新しい版に置き換わっていない'
    $updated = Wait-Meltype
    Assert-True ($updated.Id -ne $running.Id) '動いていた Meltype が止まっていない'
    Assert-True (Test-Path -LiteralPath $config) '更新で設定が消えた'
    Assert-True ((Get-ItemProperty -LiteralPath $innoKey).DisplayVersion -eq '2.0.0') '「設定 → アプリ」の版が更新されていない'
    Assert-True (Test-Path -LiteralPath $log) '更新のログが無い'
    Write-Host 'PASS: 自動更新'

    # 1.6.0 以前の zip 版 (Install.cmd) で入れた人を、1.6.0 以前の Meltype が互換の zip で更新する
    New-Item -Path $zipKey -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $target 'uninstall.ps1') -Value '# 1.6.0 以前のアンインストーラー'
    $bridge = Join-Path $testRoot 'bridge'
    New-Item -ItemType Directory -Path $bridge | Out-Null
    Copy-Item -LiteralPath (New-Setup '3.0.0'), (Join-Path $repo 'packaging\install.ps1') -Destination $bridge
    Remove-Item -LiteralPath $log
    $code = Invoke-Wait $powershell @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $bridge 'install.ps1')`"", '-UpdateLog', "`"$log`"")
    Assert-True ($code -eq 0) "互換の zip の更新の終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '3.0.0') '互換の zip で新しい版に置き換わっていない'
    $null = Wait-Meltype
    Assert-True (Test-Path -LiteralPath $log) '互換の zip の更新のログが無い'
    Assert-True (-not (Test-Path -LiteralPath $zipKey)) 'zip 版の「設定 → アプリ」の項目が残った'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $target 'uninstall.ps1'))) 'zip 版のアンインストーラーが残った'
    Write-Host 'PASS: 1.6.0 以前の自動更新 (互換の zip)'

    $code = Invoke-Wait (Join-Path $target 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    Assert-True ($code -eq 0) "アンインストールの終了コード: $code"
    # アンインストーラーは自分を消すために、一時フォルダーに写した自分を起動して終わる
    for ($i = 0; $i -lt 300 -and (Test-Path -LiteralPath $target); $i++) { Start-Sleep -Milliseconds 100 }
    Assert-True (-not (Test-Path -LiteralPath $target)) 'インストール先が残った'
    Assert-True (-not (Test-Path -LiteralPath $innoKey)) '「設定 → アプリ」の項目が残った'
    foreach ($shortcut in $shortcuts) { Assert-True (-not (Test-Path -LiteralPath $shortcut)) "ショートカットが残った: $shortcut" }
    Assert-True (-not (Get-Process Meltype -ErrorAction SilentlyContinue)) 'Meltype が動いたまま'
    Assert-True (Test-Path -LiteralPath $config) '画面の無いアンインストールで設定が消えた'
    Write-Host 'PASS: アンインストール'
}
finally {
    Get-Process Meltype -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
