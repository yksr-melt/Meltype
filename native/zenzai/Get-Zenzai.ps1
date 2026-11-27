# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Meltype Contributors

# Zenzai を配布物に同梱するために、llama.cpp の llama-server (CPU 版) と zenz のモデルを取ってきて native\zenzai\bin に置く (issue #1120)。
# 版とハッシュはここで固定する。ハッシュが合わなければ止める (取ってきたものを確かめずに配らない)。
# Build-Package.ps1 は native\zenzai\bin があれば app\zenzai に同梱する。
param([string]$OutDir = (Join-Path $PSScriptRoot 'bin'))

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# 版とハッシュは zenzai.env に 1 か所だけ書く (mac/build.sh と共通)
$versions = @{}
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'zenzai.env') | Where-Object { $_ -match '^\s*([A-Z0-9_]+)=(.*)$' } | ForEach-Object { $versions[$Matches[1]] = $Matches[2].Trim() }
# llama.cpp (MIT License)
$llamaBuild = $versions['LLAMA_BUILD']
$llamaUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$llamaBuild/llama-$llamaBuild-bin-win-cpu-x64.zip"
$llamaSha256 = $versions['LLAMA_SHA256']
# zenz-v3.2-small (Apache License 2.0)。版はコミットで固定する
$modelCommit = $versions['ZENZ_COMMIT']
$modelUrl = "https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf/resolve/$modelCommit/ggml-model-Q5_K_M.gguf"
$modelSha256 = $versions['ZENZ_SHA256']
$modelName = $versions['ZENZ_NAME']
if (-not ($llamaBuild -and $llamaSha256 -and $modelCommit -and $modelSha256 -and $modelName)) { throw 'zenzai.env の値が足りません。' }

function Get-Verified([string]$url, [string]$path, [string]$sha256) {
    if ((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $sha256) { return }
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne $sha256) {
        Remove-Item -LiteralPath $path -Force
        throw "ハッシュが合いません: $url (期待 $sha256、実際 $actual)"
    }
}

$cache = Join-Path $PSScriptRoot 'cache'
New-Item -ItemType Directory -Force -Path $cache, $OutDir | Out-Null

$zip = Join-Path $cache "llama-$llamaBuild.zip"
Get-Verified $llamaUrl $zip $llamaSha256
$extract = Join-Path $cache "llama-$llamaBuild"
if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
Expand-Archive -LiteralPath $zip -DestinationPath $extract
# llama-server が使うのは DLL だけ (ほかの exe は配らない)
Copy-Item -LiteralPath (Join-Path $extract 'llama-server.exe') -Destination $OutDir -Force
Get-ChildItem -LiteralPath $extract -Filter '*.dll' -File | Copy-Item -Destination $OutDir -Force
Get-ChildItem -LiteralPath $extract -Filter 'LICENSE*' -File | Copy-Item -Destination $OutDir -Force

# llama.cpp の Windows 版は Visual C++ のランタイム (VCRUNTIME140.dll など) を使うが、zip には入っていない。
# 「Visual C++ 再頒布可能パッケージ」が入っていない PC でも llama-server が起動できるよう、Visual Studio の Redist から横に置く
# (Mozc の変換ヘルパーの app\mozc とはフォルダーが別なので、そちらのランタイムは使われない)。
# どれが要るかは llama-server と DLL を dumpbin で調べて決める (llama.cpp の版で変わるので決め打ちしない)。
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = if (Test-Path -LiteralPath $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath }
if (-not $vs) { throw 'Visual Studio (C++) が見つかりません。llama-server と一緒に置く Visual C++ のランタイムを取れません。' }
$vcPath = Join-Path $vs 'VC'
$crt = Get-ChildItem (Join-Path $vcPath 'Redist\MSVC\*\x64\Microsoft.VC*.CRT') -Directory -ErrorAction SilentlyContinue |
    Sort-Object { $v = $null; if ([version]::TryParse($_.Parent.Parent.Name, [ref]$v)) { $v } else { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $crt) { throw "Visual C++ のランタイム (Redist\MSVC) が見つかりません ($vcPath)。" }
$dumpbin = Get-ChildItem (Join-Path $vcPath 'Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe') -ErrorAction SilentlyContinue |
    Sort-Object { $v = $null; if ([version]::TryParse($_.Directory.Parent.Parent.Parent.Name, [ref]$v)) { $v } else { [version]'0.0' } } -Descending |
    Select-Object -First 1
if (-not $dumpbin) { throw "dumpbin.exe が見つかりません ($vcPath)。" }

$system = [Environment]::GetFolderPath('System')
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$placed = New-Object 'System.Collections.Generic.List[string]'
$queue = New-Object 'System.Collections.Generic.Queue[string]'
# 起点は zip から置いた llama-server と DLL。$OutDir には前回置いたランタイムも残っているので、それを起点にすると調べ済み扱いになって置き直せない
Get-ChildItem -LiteralPath $extract -File | Where-Object { $_.Extension -in '.exe', '.dll' } | ForEach-Object { [void]$seen.Add($_.Name); $queue.Enqueue($_.FullName) }
while ($queue.Count -gt 0) {
    $file = $queue.Dequeue()
    $dependents = & $dumpbin.FullName /nologo /dependents $file | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^[\w.-]+\.dll$' }
    if ($LASTEXITCODE -ne 0) { throw "dumpbin で $file を調べられませんでした。" }
    foreach ($dll in $dependents) {
        if (-not $seen.Add($dll)) { continue }
        $runtime = Get-ChildItem -LiteralPath $crt.FullName -Filter $dll -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($runtime) {
            Copy-Item -LiteralPath $runtime.FullName -Destination $OutDir -Force
            $placed.Add($runtime.Name)
            $queue.Enqueue($runtime.FullName)
        }
        elseif ($dll -notlike 'api-ms-win-*' -and $dll -notlike 'ext-ms-*' -and -not (Test-Path -LiteralPath (Join-Path $system $dll))) {
            throw "llama-server が使う $dll が、Visual C++ のランタイム ($($crt.FullName)) にも Windows にもありません。"
        }
    }
}
if ($placed.Count -eq 0) { throw 'llama-server が Visual C++ のランタイムを使っていません (dumpbin の出力を読めなかった可能性があります)。' }
# 置いたランタイムの一覧 (Build-Package.ps1 が、全部そろっているかを確かめるのに使う)
Set-Content -LiteralPath (Join-Path $OutDir 'VC-RUNTIME.txt') -Value $placed -Encoding ASCII
Write-Host "Visual C++ のランタイムを置きました ($($crt.FullName)): $($placed -join ', ')"

# zip に llama.cpp 自身のライセンスは入っていないので、同じ版のものを置く
Invoke-WebRequest -Uri "https://raw.githubusercontent.com/ggml-org/llama.cpp/$llamaBuild/LICENSE" -OutFile (Join-Path $OutDir 'LICENSE-llama.cpp.txt') -UseBasicParsing
Invoke-WebRequest -Uri 'https://www.apache.org/licenses/LICENSE-2.0.txt' -OutFile (Join-Path $OutDir 'LICENSE-zenz.txt') -UseBasicParsing

$model = Join-Path $cache $modelName
Get-Verified $modelUrl $model $modelSha256
Copy-Item -LiteralPath $model -Destination (Join-Path $OutDir $modelName) -Force

@"
llama.cpp $llamaBuild (MIT License, Copyright (c) 2023-2026 The ggml authors): https://github.com/ggml-org/llama.cpp
zenz-v3.2-small $modelCommit (Apache License 2.0, Miwa-Keita): https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf
"@ | Set-Content -LiteralPath (Join-Path $OutDir 'ZENZAI-NOTICE.txt') -Encoding UTF8
Write-Host "Zenzai (llama-server $llamaBuild と $modelName) を $OutDir に置きました。"
