# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 penta2himajin

# install.ps1 と uninstall.ps1 を、ダミーの Meltype.exe で順に通す。場面ごとに前の場面の状態を引き継ぐ。
# 実際の %LOCALAPPDATA%\Meltype (設定と学習データ) を消すので、CI の使い捨ての環境でしか動かさない。

$ErrorActionPreference = 'Stop'
if (-not $env:GITHUB_ACTIONS) { throw 'このテストは設定と学習データを消すので、GitHub Actions の中でしか動かしません。' }

$repo = Split-Path -Parent $PSScriptRoot
$powershell = Join-Path ([Environment]::GetFolderPath('Windows')) 'System32\WindowsPowerShell\v1.0\powershell.exe'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('meltype-install-test-' + [guid]::NewGuid())
$target = Join-Path $env:LOCALAPPDATA 'Programs\Meltype'
$exe = Join-Path $target 'Meltype.exe'
$data = Join-Path $env:LOCALAPPDATA 'Meltype'
$updateDirectory = Join-Path $data 'update'
$log = Join-Path $updateDirectory 'install.log'
$config = Join-Path $data 'config.json'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype'
$innoKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Meltype_is1'
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

function New-Package([string]$Version, [string]$Folder = (Join-Path $testRoot $Version)) {
    $app = Join-Path $Folder 'app'
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    foreach ($name in 'install.ps1', 'uninstall.ps1') {
        Copy-Item -LiteralPath (Join-Path $repo "packaging\$name") -Destination $Folder
    }
    Add-Type -TypeDefinition $dummy.Replace('VERSION', $Version) -OutputAssembly (Join-Path $app 'Meltype.exe') -OutputType WindowsApplication
    return $Folder
}

# 起動された Meltype は残り続ける。Start-Process -Wait は子孫のプロセスも待つので使わない。
function Invoke-Script([string]$Script, [string[]]$Arguments = @()) {
    $output = Join-Path $testRoot ('output-' + [guid]::NewGuid() + '.txt')
    $list = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Script`"") + $Arguments
    $process = Start-Process -FilePath $powershell -ArgumentList $list -WorkingDirectory $env:TEMP -RedirectStandardOutput $output -NoNewWindow -PassThru
    $null = $process.Handle # 取っておかないと、終了後に ExitCode を読めない
    if (-not $process.WaitForExit(120000)) {
        Stop-Process -Id $process.Id -Force
        throw "$Script が 2 分で終わりませんでした。"
    }
    Get-Content -LiteralPath $output | ForEach-Object { Write-Host $_ }
    return $process.ExitCode
}

function Wait-Meltype {
    for ($i = 0; $i -lt 300; $i++) {
        $process = Get-Process Meltype -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | Select-Object -First 1
        if ($process) { return $process }
        Start-Sleep -Milliseconds 100
    }
    throw "Meltype が起動しませんでした: $exe"
}

function Get-InstalledVersion { (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion }
function Get-Backups { @(Get-ChildItem -LiteralPath $env:TEMP -Directory -Filter 'Meltype-update-backup-*') }
function Get-KeyValue([string]$Key, [string]$Name) { (Get-ItemProperty -LiteralPath $Key).$Name }
# Transcript の文字コードは BOM で判定させる
function Get-Log { [IO.File]::ReadAllText($log) }

New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $code = Invoke-Script (Join-Path (New-Package '1.0.0') 'install.ps1')
    Assert-True ($code -eq 0) "新規インストールの終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '1.0.0') 'Meltype.exe がコピーされていない'
    $running = Wait-Meltype
    foreach ($shortcut in $shortcuts) { Assert-True (Test-Path -LiteralPath $shortcut) "ショートカットが無い: $shortcut" }
    Assert-True ((Get-KeyValue $uninstallKey 'DisplayVersion') -eq '1.0.0') '「設定 → アプリ」の版が違う'
    Assert-True (Test-Path -LiteralPath (Join-Path $target 'uninstall.ps1')) 'uninstall.ps1 がコピーされていない'
    Assert-True (-not (Test-Path -LiteralPath $log)) '更新ではないのにログができた'
    Write-Host 'PASS: 新規インストール'

    # 今の版の Meltype: -UpdateLog を渡す
    New-Item -ItemType Directory -Path $updateDirectory -Force | Out-Null
    Set-Content -LiteralPath $config -Value '{}'
    $code = Invoke-Script (Join-Path (New-Package '2.0.0') 'install.ps1') @('-UpdateLog', "`"$log`"")
    Assert-True ($code -eq 0) "更新の終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '2.0.0') '新しい版に置き換わっていない'
    $updated = Wait-Meltype
    Assert-True ($updated.Id -ne $running.Id) '動いていた Meltype が止まっていない'
    Assert-True (Test-Path -LiteralPath $config) '更新で設定が消えた'
    Assert-True ((Get-KeyValue $uninstallKey 'DisplayVersion') -eq '2.0.0') '「設定 → アプリ」の版が更新されていない'
    Assert-True ((Get-Log) -match 'インストールして起動しました') '更新のログに完了が記録されていない'
    Assert-True ((Get-Backups).Count -eq 0) '更新のあとにバックアップが残った'
    Remove-Item -LiteralPath $log
    Write-Host 'PASS: 更新 (-UpdateLog あり)'

    # 前の版の Meltype: -UpdateLog を渡さない。ダウンロード先のフォルダーから実行する
    $code = Invoke-Script (Join-Path (New-Package '3.0.0' (Join-Path $updateDirectory '3.0.0')) 'install.ps1')
    Assert-True ($code -eq 0) "前の版からの更新の終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '3.0.0') '前の版から更新できていない'
    $null = Wait-Meltype
    Assert-True (Test-Path -LiteralPath $log) '-UpdateLog なしの更新でログができなかった'
    Remove-Item -LiteralPath $log
    Write-Host 'PASS: 更新 (-UpdateLog なし)'

    # 新しい Meltype.exe までコピーしたあとで失敗させる。名前の順にコピーされるので、zz- より前の zy- が届いていれば確かめられる
    $package = New-Package '4.0.0' (Join-Path $updateDirectory '4.0.0')
    Set-Content -LiteralPath (Join-Path $package 'app\zy-copied.txt') -Value 'copied'
    $locked = Join-Path $package 'app\zz-locked.bin'
    Set-Content -LiteralPath $locked -Value 'locked'
    $before = Wait-Meltype
    $lock = [IO.File]::Open($locked, 'Open', 'Read', 'None')
    try { $code = Invoke-Script (Join-Path $package 'install.ps1') }
    finally { $lock.Dispose() }
    Assert-True ($code -eq 1) "更新の失敗の終了コード: $code"
    Assert-True (Test-Path -LiteralPath (Join-Path $target 'zy-copied.txt')) 'コピーの途中まで進んでいない (復元を確かめられない)'
    Assert-True ((Get-InstalledVersion) -eq '3.0.0') '旧版が復元されていない'
    Assert-True ((Wait-Meltype).Id -ne $before.Id) '復元した旧版が起動していない'
    Assert-True ((Get-Backups).Count -eq 0) '復元のあとにバックアップが残った'
    $text = Get-Log
    Assert-True ($text -match 'インストールに失敗しました') 'ログに失敗の理由が無い'
    Assert-True ($text -match '復元して起動しました') 'ログに復元の結果が無い'
    Remove-Item -LiteralPath $log
    Write-Host 'PASS: 更新の失敗で旧版を復元する'

    # 読み取りだけ許すと、退避はできて、上書きは更新でも復元でも失敗する
    $package = New-Package '5.0.0' (Join-Path $updateDirectory '5.0.0')
    $kept = Join-Path $target 'zz-kept.bin'
    Set-Content -LiteralPath $kept -Value 'old'
    Set-Content -LiteralPath (Join-Path $package 'app\zz-kept.bin') -Value 'new'
    $lock = [IO.File]::Open($kept, 'Open', 'Read', 'Read')
    try { $code = Invoke-Script (Join-Path $package 'install.ps1') }
    finally { $lock.Dispose() }
    Assert-True ($code -eq 1) "復元の失敗の終了コード: $code"
    $backups = @(Get-Backups)
    Assert-True ($backups.Count -eq 1) "バックアップが残っていない: $($backups.Count)"
    Assert-True (Test-Path -LiteralPath (Join-Path $backups[0].FullName 'Meltype.exe')) 'バックアップに Meltype.exe が無い'
    Assert-True ((Get-Log) -match [regex]::Escape($backups[0].Name)) 'ログにバックアップの場所が無い'
    Remove-Item -LiteralPath $backups[0].FullName -Recurse -Force
    Remove-Item -LiteralPath $kept, $log
    Write-Host 'PASS: 復元の失敗でバックアップを残す'

    # Inno Setup で入れた環境は「設定 → アプリ」の項目を自分で持つ。uninstall.ps1 はこの項目を消さない (Inno Setup のアンインストーラーの役目)
    Remove-Item -LiteralPath $uninstallKey -Recurse
    New-Item -Path $innoKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $innoKey -Name DisplayVersion -Value '3.0.0'
    $unins = Join-Path $target 'unins000.exe'
    Set-Content -LiteralPath $unins -Value 'dummy'
    $package = New-Package '6.0.0'
    $code = Invoke-Script (Join-Path $package 'install.ps1')
    Assert-True ($code -eq 0) "Inno Setup の環境の更新の終了コード: $code"
    Assert-True ((Get-InstalledVersion) -eq '6.0.0') 'Inno Setup の環境で新しい版に置き換わっていない'
    Assert-True ((Get-KeyValue $innoKey 'DisplayVersion') -eq '6.0.0') 'Inno Setup の項目の版が更新されていない'
    Assert-True (-not (Test-Path -LiteralPath $uninstallKey)) '「設定 → アプリ」の項目が二重になった'
    Write-Host 'PASS: Inno Setup で入れた環境の更新'

    # アンインストールの前に、ふつうのインストールの状態へ戻す
    Remove-Item -LiteralPath $innoKey -Recurse
    Remove-Item -LiteralPath $unins
    $code = Invoke-Script (Join-Path $package 'install.ps1')
    Assert-True ($code -eq 0) "入れ直しの終了コード: $code"
    $null = Wait-Meltype

    $code = Invoke-Script (Join-Path $target 'uninstall.ps1')
    Assert-True ($code -eq 0) "アンインストールの終了コード: $code"
    Assert-True (-not (Test-Path -LiteralPath $target)) 'インストール先が残った'
    Assert-True (-not (Test-Path -LiteralPath $data)) '設定と学習データが残った'
    Assert-True (-not (Test-Path -LiteralPath $uninstallKey)) '「設定 → アプリ」の項目が残った'
    foreach ($shortcut in $shortcuts) { Assert-True (-not (Test-Path -LiteralPath $shortcut)) "ショートカットが残った: $shortcut" }
    Assert-True (-not (Get-Process Meltype -ErrorAction SilentlyContinue)) 'Meltype が動いたまま'
    Write-Host 'PASS: アンインストール'
}
finally {
    Get-Process Meltype -ErrorAction SilentlyContinue | Stop-Process -Force
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
