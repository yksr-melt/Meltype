# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# -Version を付けると、公開するリリースの名前 (dist\Meltype-<版>-windows.zip) にする (GitHub Actions がタグから付ける)。
# -Portable を付けると、アプリ内にデータを保存するポータブル版 zip を作る。
# -ReuseBuild を -Portable と一緒に付けると、直前に作った通常版の app を再利用する (CI 用)。
# -Step Build は通常版の app を作って自己診断するところまで、-Step Sign はその app に署名して zip を作るところだけを行う (CI 用)。
# 署名の秘密 (MELTYPE_SIGN_PFX / MELTYPE_SIGN_PASSWORD) がある間に、ビルドしたもの (Meltype.exe・Mozc のヘルパー) を実行しないため。
param([string]$Version = '', [switch]$Portable, [switch]$ReuseBuild, [ValidateSet('All', 'Build', 'Sign')][string]$Step = 'All')

$ErrorActionPreference = 'Stop'

# 協力者に渡すテスト版の zip を作る: dist\Meltype-test-<日付>-windows.zip
# 中身は ビルド済みの app フォルダー (.NET ランタイム同梱) + Install.cmd / Uninstall.cmd + README.txt。
#
# .NET の同梱: この環境は NuGet が使えないので自己完結ビルド (--self-contained) の代わりに、
# この PC にインストール済みの .NET ランタイムを app\dotnet にコピーし、Meltype.exe がそこを使うようにする
# (AppHostDotNetSearch=AppRelative: .NET 9 以降の apphost の機能)。協力者の PC に .NET は不要。

$root = $PSScriptRoot
$dist = Join-Path $root 'dist'
$packageName = if ($Portable) { 'Meltype-portable' } else { 'Meltype' }
$stage = Join-Path $dist $packageName
$app = Join-Path $stage 'app'
$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$zip = if ($Version) {
    if ($Portable) { Join-Path $dist "Meltype-$Version-windows-portable.zip" }
    else { Join-Path $dist "Meltype-$Version-windows.zip" }
}
elseif ($Portable) { Join-Path $dist "Meltype-test-$stamp-windows-portable.zip" }
else { Join-Path $dist "Meltype-test-$stamp-windows.zip" }

if ($Portable -and $Step -ne 'All') { throw '-Step は通常版だけで使えます。' }
if ($Step -ne 'Sign') {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
}

$mozcBin = Join-Path $root 'native\mozc\bin'
$dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
$runtime = Join-Path $app 'dotnet'
$version = $null
$signtool = $null

# 同梱したランタイムで自己診断を走らせる。通常版・ポータブル版の両方の最終配置で使う。
function Invoke-SelfTest([string]$label, [string]$executable) {
    $report = Join-Path $dist 'selftest.txt'
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $env:DOTNET_DISABLE_GUI_ERRORS = '1'
    try {
        $process = Start-Process -FilePath $executable -ArgumentList '--selftest', "`"$report`"" -PassThru
        if (-not $process.WaitForExit(120000)) { $process.Kill(); throw "自己診断 ($label) が終わりませんでした。" }
    } finally {
        Remove-Item Env:\DOTNET_DISABLE_GUI_ERRORS -ErrorAction SilentlyContinue
    }
    $lines = if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Encoding UTF8 } else { @() }
    Write-Host "--- 自己診断 ($label) ---"
    $lines | Where-Object { $_ -notlike 'LOADED:*' } | Write-Host
    if ($process.ExitCode -ne 0) { throw "自己診断 ($label) に失敗しました (exit code $($process.ExitCode))。ランタイムを削りすぎている可能性があります。" }
    $loaded = $lines | Where-Object { $_ -like 'LOADED:*' } | Select-Object -First 1
    return @(($loaded -replace '^LOADED:\s*', '') -split ' ' | Where-Object { $_ })
}

if ($Portable -and $ReuseBuild) {
    $baseApp = Join-Path $dist 'Meltype\app'
    if (-not (Test-Path -LiteralPath (Join-Path $baseApp 'Meltype.exe'))) {
        throw '再利用できる通常版のビルドがありません。先に Build-Package.ps1 を実行してください。'
    }
    New-Item -ItemType Directory -Force -Path $app | Out-Null
    Copy-Item -Path (Join-Path $baseApp '*') -Destination $app -Recurse -Force
    $runtime = Join-Path $app 'dotnet'
    $installedRuntime = Get-ChildItem (Join-Path $runtime 'shared\Microsoft.NETCore.App') -Directory |
        Sort-Object { [version]$_.Name } | Select-Object -Last 1
    if (-not $installedRuntime) { throw '通常版のビルドに同梱された .NET ランタイムが見つかりません。' }
    $version = $installedRuntime.Name
    # ポータブル版は Meltype キーボード固定。IME 登録・更新スクリプトは持たせない。
    Remove-Item -LiteralPath (Join-Path $app 'tip') -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $app 'meltype-ime.ps1'), (Join-Path $app 'update.ps1') -Force -ErrorAction SilentlyContinue
    Write-Host '通常版のビルド済みアプリを再利用し、ポータブル版を作ります。'
}
else {
if ($Step -eq 'Sign') {
    # -Step Build で作った app をそのまま使う。ここでは何も実行しない。
    if (-not (Test-Path -LiteralPath (Join-Path $app 'Meltype.exe'))) {
        throw '署名する通常版のビルドがありません。先に Build-Package.ps1 -Step Build を実行してください。'
    }
    $version = (Get-ChildItem (Join-Path $runtime 'shared\Microsoft.NETCore.App') -Directory | Sort-Object { [version]$_.Name } | Select-Object -Last 1).Name
    $imeBuilt = Test-Path -LiteralPath (Join-Path $app 'tip')
}
else {
dotnet publish (Join-Path $root 'src\Meltype\Meltype.csproj') -c Release -o $app -p:DebugType=none `
    -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=dotnet
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました (exit code $LASTEXITCODE)。" }

# Mozc の変換ヘルパー (native\mozc\Build-MozcHelper.ps1 で作ったもの) を同梱する。無ければ Microsoft IME だけで動く。
if (Test-Path -LiteralPath (Join-Path $mozcBin 'meltype_mozc_helper.exe')) {
    # ヘルパーが使う Visual C++ のランタイム (MSVCP140.dll など) は、Build-MozcHelper.ps1 がビルドに使った Visual Studio から bin に置いている。
    # 前の版の Build-MozcHelper.ps1 で作った bin には無い。版がずれないよう、ほかから補わずに止める
    # (無いまま配ると、「Visual C++ 再頒布可能パッケージ」が入っていない PC でヘルパーが起動できない)
    # どれを置いたかは、Build-MozcHelper.ps1 が VC-RUNTIME.txt に書いている (ヘルパーを dumpbin で調べて決めたもの)
    $runtimeList = Join-Path $mozcBin 'VC-RUNTIME.txt'
    $runtimeDlls = if (Test-Path -LiteralPath $runtimeList) { @(Get-Content -LiteralPath $runtimeList | Where-Object { $_.Trim() }) } else { @() }
    $missing = $runtimeDlls | Where-Object { -not (Test-Path -LiteralPath (Join-Path $mozcBin $_)) }
    if ($runtimeDlls.Count -eq 0 -or $missing) {
        throw "Mozc の変換ヘルパーと一緒に置く Visual C++ のランタイムがありません ($(if ($missing) { $missing -join ', ' } else { 'VC-RUNTIME.txt' }))。native\mozc\Build-MozcHelper.ps1 でヘルパーを作り直してください。"
    }
    New-Item -ItemType Directory -Force -Path (Join-Path $app 'mozc') | Out-Null
    Copy-Item -Path (Join-Path $mozcBin '*') -Destination (Join-Path $app 'mozc') -Force
    Write-Host 'Mozc の変換ヘルパーを同梱しました。'
}
else {
    Write-Warning 'Mozc の変換ヘルパーがありません (native\mozc\Build-MozcHelper.ps1)。Microsoft IME だけで変換します。'
}

# Meltype が使うランタイムの版 (runtimeconfig.json に書かれている) と同じものを、インストール済みの .NET から探してコピーする。
$config = Get-Content -Raw (Join-Path $app 'Meltype.runtimeconfig.json') | ConvertFrom-Json
$frameworks = @($config.runtimeOptions.frameworks) + @($config.runtimeOptions.framework) | Where-Object { $_ }
$runtime = Join-Path $app 'dotnet'
$version = $null
foreach ($framework in $frameworks) {
    $major = ($framework.version -split '\.')[0..1] -join '.'
    $installed = Get-ChildItem (Join-Path $dotnetRoot "shared\$($framework.name)") -Directory |
        Where-Object { $_.Name -like "$major.*" } | Sort-Object { [version]$_.Name } | Select-Object -Last 1
    if (-not $installed) { throw "$($framework.name) $major がインストールされていません。" }
    Copy-Item -LiteralPath $installed.FullName -Destination (Join-Path $runtime "shared\$($framework.name)\$($installed.Name)") -Recurse
    if ($framework.name -eq 'Microsoft.NETCore.App') { $version = $installed.Name }
}
$fxr = Join-Path $dotnetRoot "host\fxr\$version"
if (-not (Test-Path -LiteralPath $fxr)) { throw "hostfxr $version が見つかりません。" }
Copy-Item -LiteralPath $fxr -Destination (Join-Path $runtime "host\fxr\$version") -Recurse
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination $runtime -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination $runtime -ErrorAction SilentlyContinue

# スマホからも受け取れる大きさ (30MB 未満) にするため、ランタイムから Meltype が使わないものを 2 段階で削る。
#   1. 参照をたどって削る: Meltype.dll が実際に使う型からたどって必要なアセンブリだけ残す (Meltype.Tests の --runtime-closure)。
#      ネイティブの DLL は、デバッグ用 (mscordaccore, mscordbi, DiaSymReader, createdump) と WPF の描画用を削る。
#   2. 実際に読み込まれたかで削る: 1 の状態で自己診断を走らせ、読み込まれなかった大きなアセンブリ (512KB 超) を削る
#      (XML・ネットワーク・暗号などは WinForms から参照されているが、Meltype の使い方では読み込まれない)。
#      設定で ON にしたときだけ使う部品 (Zenzai・GitHub の題名の HTTP など) は、自己診断 (SelfTest.Run) の中で一度使って読み込ませる。
#      読み込まれないと、ここで削られ、ON にしたとたん動かなくなる (#973)。機能を足すときは、自己診断にも足す。
#      (Zenzai は loopback の偽の llama-server に変換を頼む。GitHub の題名の HTTPS は型に触るだけで、TLS の接続そのものは自己診断では確かめていない)
#   どちらの後も自己診断を走らせ、削りすぎていないことを確かめる。
# フレームワークの .deps.json は、一覧にあるファイルが無いと起動できないので、残したファイルだけの一覧に書き直す
# (.deps.json 自体を消すと、そのフレームワークが無いものとして扱われる)。

function Update-DepsJson {
    foreach ($deps in Get-ChildItem (Join-Path $runtime 'shared') -Recurse -Filter '*.deps.json') {
        $json = Get-Content -Raw -LiteralPath $deps.FullName | ConvertFrom-Json
        foreach ($target in $json.targets.PSObject.Properties) {
            foreach ($library in $target.Value.PSObject.Properties) {
                foreach ($section in 'runtime', 'native') {
                    $assets = $library.Value.$section
                    if (-not $assets) { continue }
                    foreach ($asset in @($assets.PSObject.Properties)) {
                        $leaf = Split-Path -Leaf $asset.Name
                        if (-not (Test-Path -LiteralPath (Join-Path $deps.DirectoryName $leaf))) { $assets.PSObject.Properties.Remove($asset.Name) }
                    }
                }
            }
        }
        [System.IO.File]::WriteAllText($deps.FullName, ($json | ConvertTo-Json -Depth 32), (New-Object System.Text.UTF8Encoding $false))
    }
}

function Test-Managed([string]$path) {
    try { [System.Reflection.AssemblyName]::GetAssemblyName($path) | Out-Null; return $true } catch { return $false }
}

# 1. 参照をたどって削る
$keep = dotnet run --project (Join-Path $root 'src\Meltype.Tests\Meltype.Tests.csproj') -c Release -- --runtime-closure $app
if ($LASTEXITCODE -ne 0) { throw "必要なアセンブリの洗い出しに失敗しました。" }
$keep = @($keep | ForEach-Object { $_.Trim() } | Where-Object { $_ -like '*.dll' })
$dropNative = @('Microsoft.DiaSymReader.Native.*', 'mscordaccore*', 'mscordbi.dll', 'createdump.exe', 'D3DCompiler_47_cor3.dll', 'PenImc_cor3.dll')
# WPF (PresentationCore) を使わないなら、WPF の描画用のネイティブ DLL も要らない。
if ($keep -notcontains 'PresentationCore.dll') { $dropNative += @('wpfgfx_cor3.dll', 'PresentationNative_cor3.dll', 'vcruntime140_cor3.dll') }
foreach ($file in Get-ChildItem (Join-Path $runtime 'shared') -Recurse -File) {
    $name = $file.Name
    if ($name -notlike '*.dll' -and $name -notlike '*.exe') { continue }
    if (Test-Managed $file.FullName) {
        if ($keep -notcontains $name) { Remove-Item -LiteralPath $file.FullName }
    } elseif ($dropNative | Where-Object { $name -like $_ }) {
        Remove-Item -LiteralPath $file.FullName
    }
}
Update-DepsJson
$loaded = Invoke-SelfTest '参照をたどって削った後' (Join-Path $app 'Meltype.exe')

# 2. 読み込まれなかった大きなアセンブリを削る
foreach ($file in Get-ChildItem (Join-Path $runtime 'shared') -Recurse -Filter '*.dll') {
    if ($file.Length -le 512KB -or -not (Test-Managed $file.FullName)) { continue }
    if ($loaded -notcontains $file.BaseName) {
        # System.Reflection.Metadata.dll は ClipBoard.SetText() で使われるため continue する
        if ($file.Name -eq 'System.Reflection.Metadata.dll') { continue }
        Write-Host "使わないので削除: $($file.Name) ($([math]::Round($file.Length / 1MB, 1)) MB)"
        Remove-Item -LiteralPath $file.FullName
    }
}
Update-DepsJson
Invoke-SelfTest '読み込まれない部品も削った後' (Join-Path $app 'Meltype.exe') | Out-Null

# Meltype IME (入力欄に直接入力する TSF の DLL)。ポータブル版は登録を行わないので含めない。
$tip = Join-Path $app 'tip'
$imeBuilt = $false
if (-not $Portable) {
    $imeBuilt = $true
    try { & (Join-Path $root 'native\tip\build.ps1') -Configuration Release }
    catch {
        $imeBuilt = $false
        Write-Warning "Meltype IME をビルドできないので、入れずに作ります: $($_.Exception.Message)"
    }
    if ($imeBuilt) {
        foreach ($arch in 'x64', 'x86') {
            New-Item -ItemType Directory -Force -Path (Join-Path $tip $arch) | Out-Null
            Copy-Item -LiteralPath (Join-Path $root "native\tip\bin\$arch\MeltypeTip.dll"), (Join-Path $root "native\tip\bin\$arch\MeltypeTip.dll.sha256") -Destination (Join-Path $tip $arch)
        }
        Copy-Item -LiteralPath (Join-Path $root 'native\tip\Register-Tip.ps1') -Destination $tip
        Copy-Item -LiteralPath (Join-Path $root 'packaging\meltype-ime.ps1') -Destination $app
    }
}
}
if ($Step -eq 'Build') {
    Write-Host "通常版の app を作りました: $app (署名と zip は -Step Sign で行います)"
    return
}

# コード署名 (証明書があるときだけ): 環境変数 MELTYPE_SIGN_PFX (証明書の .pfx) と MELTYPE_SIGN_PASSWORD を設定すると、
# Meltype.exe・Meltype.dll・Mozc のヘルパーに署名する (SmartScreen の警告とウイルス対策ソフトの誤検知を減らすため)。
# GitHub Actions では、秘密 SIGN_PFX_BASE64 / SIGN_PASSWORD を登録すると build.yml が設定する。
if ($env:MELTYPE_SIGN_PFX -and (Test-Path -LiteralPath $env:MELTYPE_SIGN_PFX)) {
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\*' } | Sort-Object FullName | Select-Object -Last 1
    if (-not $signtool) { throw 'signtool.exe が見つかりません (Windows SDK が必要です)。' }
    $targets = @('Meltype.exe', 'Meltype.dll', 'Meltype.Core.dll', 'mozc\meltype_mozc_helper.exe', 'tip\x64\MeltypeTip.dll', 'tip\x86\MeltypeTip.dll') |
        ForEach-Object { Join-Path $app $_ } | Where-Object { Test-Path -LiteralPath $_ }
    & $signtool.FullName sign /f $env:MELTYPE_SIGN_PFX /p $env:MELTYPE_SIGN_PASSWORD /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $targets
    if ($LASTEXITCODE -ne 0) { throw "署名に失敗しました (exit code $LASTEXITCODE)。" }
    Write-Host "署名しました: $($targets.Count) 個のファイル"
}
else {
    Write-Host '証明書が無いので署名しません (MELTYPE_SIGN_PFX)。'
}

# 配布する Meltype IME の DLL (署名した後) のハッシュ。インストールのとき、管理者として登録する前に、DLL が書き換えられていないかを確かめるのに使う。
# (MeltypeTip.dll.sha256 は署名する前の中身のハッシュで、更新のときに「同じ DLL か」を比べるのに使う)
foreach ($arch in @(if ($imeBuilt) { 'x64', 'x86' })) {
    $dll = Join-Path $app "tip\$arch\MeltypeTip.dll"
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($dll))).Replace('-', '') } finally { $sha.Dispose() }
    [IO.File]::WriteAllText("$dll.package.sha256", $hash)
}
}

if ($Portable) {
    [IO.File]::WriteAllText((Join-Path $app 'Meltype.portable'), 'portable', [Text.Encoding]::ASCII)
    # ZIP 内で Meltype.exe を見つけやすくし、ランタイムや辞書を含むフォルダーごと持ち運べるようにする。
    Copy-Item -Path (Join-Path $app '*') -Destination $stage -Recurse -Force
    Remove-Item -LiteralPath $app -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $root 'packaging\README-portable.txt') -Destination (Join-Path $stage 'README.txt')
    if (Test-Path -LiteralPath (Join-Path $mozcBin 'meltype_mozc_helper.exe')) {
        Copy-Item -LiteralPath (Join-Path $mozcBin 'MOZC-LICENSE.txt') -Destination $stage
        Copy-Item -LiteralPath (Join-Path $mozcBin 'MOZC-CREDITS.html') -Destination $stage
        Copy-Item -LiteralPath (Join-Path $mozcBin 'VC-RUNTIME.txt') -Destination $stage
    }
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination (Join-Path $stage 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination (Join-Path $stage 'DOTNET-THIRD-PARTY-NOTICES.txt')
}
else {
    # 自動更新とインストール用スクリプトは通常版だけに含める。
    Copy-Item -LiteralPath (Join-Path $root 'packaging\update.ps1') -Destination $app
    foreach ($file in 'Install.cmd', 'Uninstall.cmd', 'install.ps1', 'uninstall.ps1', 'README.txt') {
        Copy-Item -LiteralPath (Join-Path $root "packaging\$file") -Destination $stage
    }
}
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination (Join-Path $stage 'THIRD-PARTY-NOTICES.txt')

if ($Portable) {
    # 配布時と同じ配置 (exe とマーカーが同じフォルダー) で起動し、data の保存先を含めて確認する。
    Invoke-SelfTest 'ポータブル版の最終配置' (Join-Path $stage 'Meltype.exe') | Out-Null
    # 自己診断が作ったデータは配布物に含めない。
    Remove-Item -LiteralPath (Join-Path $stage 'data') -Recurse -Force -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath (Join-Path $dist 'selftest.txt') -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "パッケージを作成しました: $zip ($size MB, .NET $version 同梱)"
