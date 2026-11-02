# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

# Zenzai を配布物に同梱するために、llama.cpp の llama-server (CPU 版) と zenz のモデルを取ってきて native\zenzai\bin に置く (issue #1120)。
# 版とハッシュはここで固定する。ハッシュが合わなければ止める (取ってきたものを確かめずに配らない)。
# Build-Package.ps1 は native\zenzai\bin があれば app\zenzai に同梱する。
param([string]$OutDir = (Join-Path $PSScriptRoot 'bin'))

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# llama.cpp (MIT License)。docs/WINDOWS.md で確かめた版
$llamaBuild = 'b11719'
$llamaUrl = "https://github.com/ggml-org/llama.cpp/releases/download/$llamaBuild/llama-$llamaBuild-bin-win-cpu-x64.zip"
$llamaSha256 = '1b1234d208a8b851cc27cd3d96b850d1547a4e5cfc5cd75f0a9fdd1d4c5bea20'
# zenz-v3.2-small (Apache License 2.0)。版はコミットで固定する
$modelCommit = 'c67e03e07d215c869f591b274c1631170d3e11fe'
$modelUrl = "https://huggingface.co/Miwa-Keita/zenz-v3.2-small-gguf/resolve/$modelCommit/ggml-model-Q5_K_M.gguf"
$modelSha256 = '29c223d4c23327b80fd13ebb5ab2555057a46317997d5da391584ffbef0db673'
$modelName = 'zenz-v3.2-small-Q5_K_M.gguf'

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
