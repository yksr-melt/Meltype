# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Yukishiro

$ErrorActionPreference = 'Stop'

# Inno Setup 6 のコンパイラー (ISCC.exe) の場所を返す。無ければ Chocolatey で入れる (GitHub Actions の Windows のランナー用)。
$candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
$found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $found -and (Get-Command choco -ErrorAction SilentlyContinue)) {
    choco install innosetup -y --no-progress | Out-Host
    $found = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $found) { throw 'Inno Setup 6 (ISCC.exe) が見つかりません。https://jrsoftware.org/isinfo.php から入れてください。' }
$found
