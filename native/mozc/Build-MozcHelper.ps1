# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro
#
# Mozc の変換ヘルパー (meltype_mozc_helper.exe) をビルドして native\mozc\bin に置く。
# Build-Package.ps1 / Install-Meltype.ps1 は、bin にヘルパーがあれば Meltype に同梱する (無ければ Microsoft IME だけで動く)。
# GitHub Actions (.github/workflows/build.yml の mozc) でも同じスクリプトでビルドする。
#
# 必要なもの (Mozc の docs/build_mozc_in_windows.md と同じ):
#   Visual Studio 2022 (「C++ によるデスクトップ開発」、Windows 11 SDK、C++ ATL)、Python 3.12 以降、Bazelisk、Git
#
#   .\native\mozc\Build-MozcHelper.ps1 -Python C:\tools\python\python.exe -Bazelisk C:\tools\bazelisk.exe
param(
    # Mozc のソースを置く場所 (無ければ取得する)。Windows のパスの長さの制限があるので短い場所がよい。
    [string]$MozcSource = (Join-Path $env:USERPROFILE 'mozc'),
    [string]$Python = 'python',
    [string]$Bazelisk = 'bazelisk',
    # Visual Studio の VC フォルダー。省略すると vswhere で探す (Mozc の自動検出は一部の構成で失敗するため)。
    [string]$VcPath = '',
    # Bazel の結果を保存しておく場所 (GitHub Actions でビルドを速くするため)。省略すると使わない。
    [string]$DiskCache = '',
    # Mozc-UT の辞書 (mozcdic-ut.txt) を足す場合に指定する。省略すると環境変数 MELTYPE_UT_DICTIONARY。
    # 指定しなければ、これまでどおり Mozc だけのヘルパーを作る (前回の統合は自動で取り消す)。
    # 辞書は Mozc の辞書に足すだけで、同梱も再配布もしない (ライセンスは README.md)。
    [string]$UtDictionary = $env:MELTYPE_UT_DICTIONARY
)
$ErrorActionPreference = 'Stop'

# 外部のプログラムを実行する。進み具合を標準エラーに出すもの (git・Bazel) を、Windows PowerShell 5.1 が
# エラーとして扱って止まらないようにし、終了コードで成否を見る。
function Invoke-Native([string]$FilePath, [string[]]$Arguments, [string]$Failure) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $FilePath @Arguments 2>&1 | ForEach-Object { Write-Host $_ } }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}

# Python の出す日本語 (UTF-8) を、コンソールの文字コードに関係なく読めるようにしてから実行する。
function Invoke-PythonUtf8([string[]]$Arguments, [string]$Failure) {
    $previousPreference = $ErrorActionPreference
    $previousIo = $env:PYTHONIOENCODING
    $previousEncoding = $null
    try { $previousEncoding = [Console]::OutputEncoding } catch { }
    $ErrorActionPreference = 'Continue'
    try {
        try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
        $env:PYTHONIOENCODING = 'utf-8'
        & $Python @Arguments 2>&1 | ForEach-Object { Write-Host $_ }
    }
    finally {
        $ErrorActionPreference = $previousPreference
        $env:PYTHONIOENCODING = $previousIo
        if ($null -ne $previousEncoding) { try { [Console]::OutputEncoding = $previousEncoding } catch { } }
    }
    if ($LASTEXITCODE -ne 0) { throw $Failure }
}
$here = $PSScriptRoot
$bin = Join-Path $here 'bin'
# ビルドする Mozc の版 (動作を確かめた commit に固定する)
$commit = (Get-Content -Raw -LiteralPath (Join-Path $here 'MOZC_COMMIT')).Trim()

if (-not $VcPath) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual Studio (C++) が見つかりません。' }
    $VcPath = Join-Path $vs 'VC'
}

# ヘルパーが使う Visual C++ のランタイム (MSVCP140.dll など) は、ビルドに使った Visual Studio から横に置く。
# 「Visual C++ 再頒布可能パッケージ」が入っていない PC でも動くように (Microsoft が、アプリと一緒に配ることを認めているファイル)。
# ビルドに使った Visual Studio に入っている、いちばん新しいランタイムにする (古い版のランタイムでは、新しい版でビルドしたヘルパーが落ちることがある。
# ランタイムとビルドのツールは版の番号が一致しないので、番号では合わせない)。
# どの DLL が要るかは、ビルドしたヘルパーを dumpbin で調べて決める (Mozc やビルドの設定で変わるので決め打ちしない)。
# 無ければ配れないので、時間のかかるビルドの前に確かめて止める
# Redist\MSVC\<版>\x64\Microsoft.VC*.CRT なので、2 つ上のフォルダーの名前が版
$crt = Get-ChildItem (Join-Path $VcPath 'Redist\MSVC\*\x64\Microsoft.VC*.CRT') -Directory -ErrorAction SilentlyContinue |
    Sort-Object { $v = $null; if ([version]::TryParse($_.Parent.Parent.Name, [ref]$v)) { $v } else { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $crt -or -not (Get-ChildItem -LiteralPath $crt.FullName -Filter '*.dll' -ErrorAction SilentlyContinue)) {
    throw "Visual C++ のランタイム (Redist\MSVC) が見つかりません ($VcPath)。Visual Studio Installer で「C++ によるデスクトップ開発」を入れ直してください。"
}
# Tools\MSVC\<版>\bin\Hostx64\x64\dumpbin.exe (いちばん新しいツール)
$dumpbin = Get-ChildItem (Join-Path $VcPath 'Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe') -ErrorAction SilentlyContinue |
    Sort-Object { $v = $null; if ([version]::TryParse($_.Directory.Parent.Parent.Parent.Name, [ref]$v)) { $v } else { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $dumpbin) { throw "dumpbin.exe が見つかりません ($VcPath)。Visual Studio Installer で「C++ によるデスクトップ開発」を入れ直してください。" }

# src の有無ではなく .git で見る (GitHub Actions のキャッシュが src\third_party_cache だけを先に戻すため)。
if (-not (Test-Path (Join-Path $MozcSource '.git'))) {
    New-Item -ItemType Directory -Force -Path $MozcSource | Out-Null
    Invoke-Native git @('-C', $MozcSource, 'init', '-q') 'git init に失敗しました。'
    Invoke-Native git @('-C', $MozcSource, 'remote', 'add', 'origin', 'https://github.com/google/mozc.git') 'git remote に失敗しました。'
}
# 前に取ってきた版が MOZC_COMMIT と違えば取り直す (取ってきていなければ HEAD は空)
$head = & git -C $MozcSource rev-parse -q --verify HEAD
if ($head) {
    # 前に足したヘルパーの定義を消し、取ってきたときの BUILD.bazel に戻す (BUILD.fragment の変更を反映し、checkout が止まらないように)
    Invoke-Native git @('-C', $MozcSource, 'checkout', '-q', 'HEAD', '--', 'src/converter/BUILD.bazel') 'BUILD.bazel を戻せませんでした。'
    # 辞書の BUILD.bazel は利用者が書き換えていることがあるので、checkout せずに、足した dictionary_ut.txt の行だけを消す
    # (build-mozc-helper.sh は Mozc の版を変えないので、この処理が無い。版を合わせる処理を足すときは同じ手当てが要る)
    $dictionaryBuild = Join-Path $MozcSource 'src\data\dictionary_oss\BUILD.bazel'
    $buildText = [IO.File]::ReadAllText($dictionaryBuild)
    if ($buildText -match '"dictionary_ut\.txt"') {
        [IO.File]::WriteAllText($dictionaryBuild, ($buildText -replace '(?m)^ *"dictionary_ut\.txt",\r?\n', ''))
    }
}
if ($head -ne $commit) {
    Invoke-Native git @('-C', $MozcSource, 'fetch', '-q', '--depth', '1', 'origin', $commit) "Mozc ($commit) を取得できませんでした。"
    Invoke-Native git @('-C', $MozcSource, 'checkout', '-q', 'FETCH_HEAD') 'git checkout に失敗しました。'
    Invoke-Native git @('-C', $MozcSource, 'submodule', 'update', '-q', '--init', '--recursive', '--depth', '1') 'Mozc のサブモジュールを取得できませんでした。'
}
$src = Join-Path $MozcSource 'src'
$merge = Join-Path $here 'merge-ut-dictionary.py'
if ($UtDictionary) {
    if (-not (Test-Path -LiteralPath $UtDictionary -PathType Leaf)) {
        throw "Mozc-UT の辞書が見つかりません: $UtDictionary"
    }
    $UtDictionary = (Resolve-Path -LiteralPath $UtDictionary).Path
}
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$utReport = Join-Path $bin 'MOZC-UT.json'
# 統合した内容 (辞書と Mozc のハッシュ) は、ビルドが通ってから印 (MOZC-UT.json) にする。
# 途中で失敗したときに印だけ消すと、前のビルドのヘルパー (Mozc-UT 入りのことがある) が
# 標準のヘルパーに見えてしまうため、ビルドが終わるまでは書かない。
$utPending = Join-Path $bin 'MOZC-UT.json.tmp'
if (Test-Path -LiteralPath $utPending) { Remove-Item -LiteralPath $utPending -Force }

# Mozc-UT の語は Mozc の辞書とは別の dictionary_ut.txt に置き、ビルドのたびに作り直す
# (Mozc の辞書は書き換えないので、手元で書き換えた辞書も残る)。
# dictionary_ut.txt はいつもビルドに含め、-UtDictionary が無いときは空にする。
# BUILD.bazel は "dictionary09.txt", の行を目印に、その次の行に足す。同じ書き換えを build-mozc-helper.sh も行い、
# merge-ut-dictionary.py (ensure_registered) が足した行を確かめるので、Mozc の BUILD.bazel の形が変わったら 3 か所を直す。
$dictionaryBuild = Join-Path $src 'data\dictionary_oss\BUILD.bazel'
$buildText = [IO.File]::ReadAllText($dictionaryBuild)
if ($buildText -notmatch '"dictionary_ut\.txt"') {
    $buildText = $buildText -replace '(?m)^( *)"dictionary09\.txt",(\r?)$', "`$0`n`$1`"dictionary_ut.txt`",`$2"
    if ($buildText -notmatch '"dictionary_ut\.txt"') {
        throw "$dictionaryBuild に dictionary_ut.txt を足せませんでした (Mozc の BUILD.bazel の形が変わった可能性があります)。"
    }
    [IO.File]::WriteAllText($dictionaryBuild, $buildText)
}
[IO.File]::WriteAllText((Join-Path $src 'data\dictionary_oss\dictionary_ut.txt'), '')

if ($UtDictionary) {
    Invoke-PythonUtf8 @($merge, '--mozc-src', $src, '--dictionary', $UtDictionary, '--report', $utPending) 'Mozc-UT の辞書を統合できませんでした。'
}

# ヘルパーのソースと BUILD の定義を Mozc の converter パッケージに入れる (エンジンを使えるのがこのパッケージのため)。
Copy-Item -LiteralPath (Join-Path $here 'meltype_mozc_helper.cc') -Destination (Join-Path $src 'converter') -Force
$build = Join-Path $src 'converter\BUILD.bazel'
Add-Content -LiteralPath $build -Value ("`n" + (Get-Content -Raw -LiteralPath (Join-Path $here 'BUILD.fragment'))) -Encoding utf8

Push-Location $src
try {
    # LLVM・MSYS2・Ninja (Qt・WiX・Android NDK は使わない)
    Invoke-Native $Python @('build_tools/update_deps.py', '--noqt', '--nowix', '--nondk') '依存関係を取得できませんでした。'
    $env:BAZEL_VC = $VcPath
    $options = @('build', '//converter:meltype_mozc_helper', '--config', 'release_build')
    if ($DiskCache) { $options += "--disk_cache=$DiskCache" }
    # シンボリックリンクは Windows の開発者モードか管理者権限が要るので使わない。
    Invoke-Native $Bazelisk (@('--nowindows_enable_symlinks') + $options) 'ビルドに失敗しました。'
}
catch {
    # ビルドできなかったので、前のヘルパーとその印はそのまま残す。
    if (Test-Path -LiteralPath $utPending) { Remove-Item -LiteralPath $utPending -Force }
    throw
}
finally {
    Pop-Location
}

Copy-Item -LiteralPath (Join-Path $src 'bazel-bin\converter\meltype_mozc_helper.exe') -Destination $bin -Force
# ビルドが通ったので、このフォルダーのヘルパーに合わせて印を入れ替える。
if (Test-Path -LiteralPath $utReport) { Remove-Item -LiteralPath $utReport -Force }
if (Test-Path -LiteralPath $utPending) { Move-Item -LiteralPath $utPending -Destination $utReport -Force }
# Bazel の出力は読み取り専用なので、上書きやアンインストールで困らないように外す。
(Get-Item -LiteralPath (Join-Path $bin 'meltype_mozc_helper.exe')).IsReadOnly = $false
# Mozc と辞書 (IPAdic など)・ライブラリのライセンス
Copy-Item -LiteralPath (Join-Path $src 'data\installer\credits_en.html') -Destination (Join-Path $bin 'MOZC-CREDITS.html') -Force
Copy-Item -LiteralPath (Join-Path $MozcSource 'LICENSE') -Destination (Join-Path $bin 'MOZC-LICENSE.txt') -Force
# ヘルパーが読み込む DLL のうち、Visual C++ のランタイムにあるものを横に置く。置いた DLL がさらに読み込むものも辿る。
# ランタイムにも Windows にも無い DLL を使っていたら、配っても動かないので止める
$helper = Join-Path $bin 'meltype_mozc_helper.exe'
$system = [Environment]::SystemDirectory
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$placed = New-Object 'System.Collections.Generic.List[string]'
$queue = New-Object 'System.Collections.Generic.Queue[string]'
$queue.Enqueue($helper)
while ($queue.Count -gt 0) {
    $file = $queue.Dequeue()
    $dependents = & $dumpbin.FullName /nologo /dependents $file | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[\w.-]+\.dll$' }
    if ($LASTEXITCODE -ne 0) { throw "dumpbin で $file を調べられませんでした。" }
    foreach ($dll in $dependents) {
        if (-not $seen.Add($dll)) { continue }
        # 名前の大文字・小文字は、ランタイムのファイルに合わせる (dumpbin は大文字で出すことがある)
        $runtime = Get-ChildItem -LiteralPath $crt.FullName -Filter $dll -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($runtime) {
            Copy-Item -LiteralPath $runtime.FullName -Destination $bin -Force
            $placed.Add($runtime.Name)
            $queue.Enqueue($runtime.FullName)
        }
        elseif ($dll -notlike 'api-ms-win-*' -and $dll -notlike 'ext-ms-*' -and -not (Test-Path -LiteralPath (Join-Path $system $dll))) {
            throw "ヘルパーが使う $dll が、Visual C++ のランタイム ($($crt.FullName)) にも Windows にもありません。"
        }
    }
}
if ($placed.Count -eq 0) { throw 'ヘルパーが Visual C++ のランタイムを使っていません (dumpbin の出力を読めなかった可能性があります)。' }
# 置いたランタイムの一覧 (Build-Package.ps1 が、全部そろっているかを確かめるのに使う)
Set-Content -LiteralPath (Join-Path $bin 'VC-RUNTIME.txt') -Value $placed -Encoding ASCII
Write-Host "Visual C++ のランタイムを置きました ($($crt.FullName)): $($placed -join ', ')"
Write-Host "作成しました: $bin"
if ($UtDictionary) {
    Write-Host "このヘルパーには Mozc-UT の辞書が入っています ($utReport)。"
    Write-Host '再配布するときは、辞書ごとのライセンスに従ってください (native\mozc\README.md)。'
}
else {
    Write-Host 'Mozc-UT の辞書は入っていません (MELTYPE_UT_DICTIONARY を指定すると入ります)。'
}
